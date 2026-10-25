using System.Security.Principal;
using Cleaner.Core;

// M1 preview: scan only. Cleaning from the CLI arrives in M4.
// Usage: cleaner scan

if (args is not ["scan"])
{
    Console.Error.WriteLine("Usage: cleaner scan   (dry run: shows what would be removed, deletes nothing)");
    return 2;
}

ProcessHardening.Apply();
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
