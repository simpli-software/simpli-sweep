using System.Diagnostics;
using System.Reflection;
using System.Security.Principal;
using System.Text.Json;
using Cleaner.Core;
using Cleaner.Core.DiskUsage;
using Cleaner.Core.Jobs;
using Cleaner.Core.Settings;

//   cleaner scan                                   dry run: what would be removed
//   cleaner clean [--preset safe|selected] [--yes] [--json] [--include-recycle-bin] [--no-settings]
//   cleaner disk <path>                            largest folders and files under a path
//
// Exit codes: 0 done, 1 error, 2 bad arguments, 3 settings need checking in the app,
// 4 no settings for this account / running as SYSTEM. Nothing is deleted for 2-4.

ProcessHardening.Apply();

try
{
    return args switch
    {
        ["scan"] => Scan(),
        ["clean", .. var options] => Clean(options),
        ["disk", var path] => Disk(path),
        _ => Usage(),
    };
}
catch (Exception e)
{
    Console.Error.WriteLine("Error: " + e.Message);
    return 1;
}

static int Usage()
{
    Console.Error.WriteLine("Usage:");
    Console.Error.WriteLine("  cleaner scan          dry run: shows what would be removed, deletes nothing");
    Console.Error.WriteLine("  cleaner clean [--preset safe|selected] [--yes] [--json] [--include-recycle-bin] [--no-settings]");
    Console.Error.WriteLine("                        without --yes this is a dry run; the Recycle Bin is only emptied with --include-recycle-bin");
    Console.Error.WriteLine("  cleaner disk <path>   largest folders and files under <path>");
    return 2;
}

static (PathTokens Tokens, Scanner Scanner, CleanRunner Runner) Engine()
{
    var tokens = PathTokens.FromEnvironment();
    var guard = SafetyGuard.ForCurrentUser(tokens);
    var recycleBin = new ShellRecycleBin();
    var elevated = new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);
    return (tokens, new Scanner(tokens, guard, recycleBin, TimeProvider.System, elevated), new CleanRunner(guard, recycleBin, TimeProvider.System));
}

static string SettingsFolder()
{
    // Same folder as the app: %LOCALAPPDATA%\<Product> (product name from the build, not code).
    var product = Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyProductAttribute>()?.Product ?? "Cleaner";
    return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), product);
}

static int Scan()
{
    var (_, scanner, _) = Engine();
    scanner.Options = new SettingsStore(SettingsFolder()).Load().ToScanOptions();
    var report = scanner.Scan(RuleLoader.LoadBuiltIn());

    foreach (var r in report.Results)
        Console.WriteLine($"{r.Rule.Name,-36} {Detail(r)}{(r.Rule.EnabledByDefault ? "" : "  (off by default)")}");

    Console.WriteLine(new string('-', 66));
    var defaultTotal = report.Results.Where(r => r.Rule.EnabledByDefault).Sum(r => r.Bytes);
    Console.WriteLine($"{"Total with default selection",-36} {ByteSize.Format(defaultTotal),10}");
    Console.WriteLine("Dry run: nothing was deleted.");
    return 0;
}

static int Clean(string[] options)
{
    var job = new CleanJobOptions();
    var json = false;
    for (var i = 0; i < options.Length; i++)
    {
        switch (options[i])
        {
            case "--yes":
                job = job with { DryRun = false };
                break;
            case "--json":
                json = true;
                break;
            case "--include-recycle-bin":
                job = job with { IncludeRecycleBin = true };
                break;
            case "--no-settings":
                job = job with { AllowMissingSettings = true };
                break;
            case "--preset" when i + 1 < options.Length && ParsePreset(options[i + 1]) is { } preset:
                job = job with { Preset = preset };
                i++;
                break;
            default:
                return Usage();
        }
    }

    // Under SYSTEM (or a service account) this would read that account's settings, not the user's,
    // so their exclusions would silently be missing. Refuse unless that is clearly intended.
    if (!job.DryRun && WindowsIdentity.GetCurrent().IsSystem && !job.AllowMissingSettings)
    {
        Console.Error.WriteLine("Not cleaning: running as SYSTEM, so your settings and exclusions can't be read. "
            + "Schedule the task to run as your own account, or add --no-settings to clean without them.");
        return 4;
    }

    var (_, scanner, runner) = Engine();
    var result = new CleanJob(scanner, runner, new SettingsStore(SettingsFolder())).Run(RuleLoader.LoadBuiltIn(), job);

    if (json)
        WriteJson(result, job.Preset);
    else
        WriteText(result);

    return result.Outcome switch
    {
        CleanJobOutcome.SettingsNeedReview => 3,
        CleanJobOutcome.NoSettings => 4,
        _ => 0,
    };
}

// Only the exact names: no numbers or comma lists that Enum.TryParse would accept.
static CleanPreset? ParsePreset(string value) => value.ToLowerInvariant() switch
{
    "safe" => CleanPreset.Safe,
    "selected" => CleanPreset.Selected,
    _ => null,
};

static void WriteText(CleanJobResult result)
{
    if (result.Outcome == CleanJobOutcome.SettingsNeedReview)
    {
        Console.Error.WriteLine("Your settings file couldn't be read. Open the app and check the Settings tab; nothing was deleted.");
        return;
    }

    if (result.Outcome == CleanJobOutcome.NoSettings)
    {
        Console.Error.WriteLine("Not cleaning: no settings found for this account, so exclusions can't be applied. "
            + "Open the app once as this user, or add --no-settings to clean without them. Nothing was deleted.");
        return;
    }

    foreach (var r in result.Scan.Results)
    {
        var cleaned = result.Clean?.Results.FirstOrDefault(c => c.RuleId == r.Rule.Id);
        var detail = cleaned is null
            ? Detail(r)
            : $"{ByteSize.Format(cleaned.BytesFreed),10}  freed{(cleaned.Skipped > 0 ? $", {cleaned.Skipped} in use or changed" : "")}";
        Console.WriteLine($"{r.Rule.Name,-36} {detail}");
    }

    Console.WriteLine(new string('-', 66));
    if (result.Clean is { } clean)
        Console.WriteLine($"{"Freed",-36} {ByteSize.Format(clean.BytesFreed),10}");
    else
        Console.WriteLine($"{"Would free",-36} {ByteSize.Format(result.Scan.TotalBytes),10}\nDry run: nothing was deleted. Add --yes to clean.");
}

// Counts and sizes only: never paths or file names.
static void WriteJson(CleanJobResult result, CleanPreset preset)
{
    var payload = new
    {
        outcome = result.Outcome.ToString(),
        preset = preset.ToString(),
        bytesFound = result.Scan.TotalBytes,
        bytesFreed = result.Clean?.BytesFreed ?? 0,
        deleted = result.Clean?.Deleted ?? 0,
        skipped = result.Clean?.Skipped ?? 0,
        categories = result.Scan.Results.Select(r =>
        {
            var c = result.Clean?.Results.FirstOrDefault(x => x.RuleId == r.Rule.Id);
            return new { id = r.Rule.Id, status = r.Status.ToString(), bytesFound = r.Bytes, bytesFreed = c?.BytesFreed ?? 0, skipped = c?.Skipped ?? 0 };
        }),
    };
    Console.WriteLine(JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }));
}

static string Detail(RuleScanResult r) => r.Status switch
{
    RuleStatus.Ok when r.Rule.Kind == RuleKind.RecycleBin => $"{ByteSize.Format(r.Bytes),10}",
    RuleStatus.Ok => $"{ByteSize.Format(r.Bytes),10}  {r.FileCount,7} files",
    RuleStatus.NeedsAdmin => "      (run as administrator)",
    RuleStatus.NothingFound => "         -",
    _ => "      (rule error)",
};

static int Disk(string path)
{
    if (!Directory.Exists(path))
    {
        Console.Error.WriteLine("Folder not found.");
        return 2;
    }

    var timer = Stopwatch.StartNew();
    var report = new DiskUsageScanner(largestFileCount: 10).Scan(path);

    Console.WriteLine($"{report.Root.FullPath}  {ByteSize.Format(report.Root.Size)} in {report.Root.FileCount:N0} files ({timer.Elapsed.TotalSeconds:0.0} s)");
    if (report.InaccessibleFolders > 0)
        Console.WriteLine($"{report.InaccessibleFolders:N0} folders could not be read (run as administrator to include them).");

    Console.WriteLine();
    Console.WriteLine("Largest folders:");
    foreach (var folder in report.Root.Children.Take(10))
        Console.WriteLine($"  {ByteSize.Format(folder.Size),10}  {folder.Name}");

    Console.WriteLine();
    Console.WriteLine("Largest files:");
    foreach (var file in report.LargestFiles)
        Console.WriteLine($"  {ByteSize.Format(file.Size),10}  {file.Path}");
    return 0;
}
