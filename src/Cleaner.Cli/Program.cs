using System.Diagnostics;
using System.Security.Principal;
using Cleaner.Core;
using Cleaner.Core.DiskUsage;

// Read-only commands. Cleaning from the CLI arrives in M4.
//   cleaner scan          dry run: what would be removed
//   cleaner disk <path>   largest folders and files under a path

ProcessHardening.Apply();

return args switch
{
    ["scan"] => Scan(),
    ["disk", var path] => Disk(path),
    _ => Usage(),
};

static int Usage()
{
    Console.Error.WriteLine("Usage:");
    Console.Error.WriteLine("  cleaner scan          dry run: shows what would be removed, deletes nothing");
    Console.Error.WriteLine("  cleaner disk <path>   largest folders and files under <path>");
    return 2;
}

static int Scan()
{
    var tokens = PathTokens.FromEnvironment();
    var elevated = new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);
    var scanner = new Scanner(tokens, SafetyGuard.ForCurrentUser(tokens), new ShellRecycleBin(), TimeProvider.System, elevated);

    var report = scanner.Scan(RuleLoader.LoadBuiltIn());

    foreach (var r in report.Results)
    {
        var detail = r.Status switch
        {
            RuleStatus.Ok when r.Rule.Kind == RuleKind.RecycleBin => $"{ByteSize.Format(r.Bytes),10}",
            RuleStatus.Ok => $"{ByteSize.Format(r.Bytes),10}  {r.FileCount,7} files",
            RuleStatus.NeedsAdmin => "      (run as administrator)",
            RuleStatus.NothingFound => "         -",
            _ => "      (rule error)",
        };
        var off = r.Rule.EnabledByDefault ? "" : "  (off by default)";
        Console.WriteLine($"{r.Rule.Name,-36} {detail}{off}");
    }

    Console.WriteLine(new string('-', 66));
    var defaultTotal = report.Results.Where(r => r.Rule.EnabledByDefault).Sum(r => r.Bytes);
    Console.WriteLine($"{"Total with default selection",-36} {ByteSize.Format(defaultTotal),10}");
    Console.WriteLine("Dry run: nothing was deleted.");
    return 0;
}

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
