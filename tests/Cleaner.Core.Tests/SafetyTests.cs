using System.Diagnostics;
using Cleaner.Core;

namespace Cleaner.Core.Tests;

public sealed class SafetyTests : IDisposable
{
    private readonly FakeMachine _m = new();

    public void Dispose() => _m.Dispose();

    private static CleanRule Rule(string path, bool recursive = true, double minAge = 0, params string[] patterns) => new()
    {
        Id = "test",
        Name = "test",
        Targets = [new RuleTarget { Path = path, Recursive = recursive, MinAgeHours = minAge, Patterns = patterns.Length > 0 ? patterns : ["*"], RemoveEmptyDirs = true }],
    };

    [Fact]
    public void Scan_never_deletes_anything()
    {
        var file = _m.File(Path.Combine(_m.Temp, "a.tmp"));

        var scan = _m.Scanner().Scan([Rule("%TEMP%")]);

        Assert.Equal(1, scan.TotalFiles);
        Assert.True(File.Exists(file));
    }

    [Fact]
    public void Only_selected_rules_are_cleaned()
    {
        var file = _m.File(Path.Combine(_m.Temp, "a.tmp"));
        var scan = _m.Scanner().Scan([Rule("%TEMP%")]);

        var report = _m.Runner().Clean(scan, new HashSet<string>());

        Assert.Equal(0, report.Deleted);
        Assert.True(File.Exists(file));
    }

    [Theory]
    [InlineData("%USERPROFILE%")]
    [InlineData("%LOCALAPPDATA%")]
    [InlineData("%APPDATA%")]
    [InlineData("%PROGRAMDATA%")]
    [InlineData("%WINDIR%")]
    [InlineData("%WINDIR%\\System32")]
    [InlineData("%USERPROFILE%\\Documents")]
    [InlineData("%USERPROFILE%\\Downloads")]
    [InlineData("%USERPROFILE%\\OneDrive")]
    [InlineData("%USERPROFILE%\\AppData")]
    public void Protected_roots_are_refused(string path)
    {
        Directory.CreateDirectory(_m.Tokens.Expand(path)!);
        var doc = _m.File(Path.Combine(_m.Documents, "taxes.pdf"));
        _m.File(Path.Combine(_m.Windir, "System32", "kernel32.dll"));

        var (scan, clean) = _m.Run(Rule(path));

        Assert.Equal(0, scan.TotalFiles);
        Assert.Equal(0, clean.Deleted);
        Assert.True(File.Exists(doc));
    }

    [Fact]
    public void Allowed_folder_inside_Windows_can_be_cleaned_but_nothing_else_in_Windows()
    {
        var temp = _m.File(Path.Combine(_m.Windir, "Temp", "old.log"));
        var sys = _m.File(Path.Combine(_m.Windir, "System32", "keep.dll"));

        _m.Run(Rule("%WINDIR%\\Temp"));

        Assert.False(File.Exists(temp));
        Assert.True(File.Exists(sys));
    }

    [Fact]
    public void Unknown_tokens_make_the_rule_invalid()
    {
        var scan = _m.Scanner().Scan([Rule("%SYSTEMROOT%\\Temp")]);

        Assert.Equal(RuleStatus.Invalid, scan.Results[0].Status);
    }

    [Fact]
    public void Files_newer_than_min_age_are_kept()
    {
        var old = _m.File(Path.Combine(_m.Temp, "old.tmp"), ageHours: 48);
        var fresh = _m.File(Path.Combine(_m.Temp, "fresh.tmp"), ageHours: 1);

        _m.Run(Rule("%TEMP%", minAge: 24));

        Assert.False(File.Exists(old));
        Assert.True(File.Exists(fresh));
    }

    [Fact]
    public void File_modified_after_scan_is_skipped()
    {
        var file = _m.File(Path.Combine(_m.Temp, "a.tmp"));
        var scan = _m.Scanner().Scan([Rule("%TEMP%")]);

        File.SetLastWriteTimeUtc(file, _m.Clock.GetUtcNow().UtcDateTime.AddHours(-2));
        var report = _m.Runner().Clean(scan, new HashSet<string> { "test" });

        Assert.Equal(1, report.Skipped);
        Assert.True(File.Exists(file));
    }

    [Fact]
    public void Locked_files_are_skipped_and_counted()
    {
        var locked = _m.File(Path.Combine(_m.Temp, "locked.tmp"));
        var free = _m.File(Path.Combine(_m.Temp, "free.tmp"));
        var scan = _m.Scanner().Scan([Rule("%TEMP%")]);

        CleanReport report;
        using (new FileStream(locked, FileMode.Open, FileAccess.Read, FileShare.None))
            report = _m.Runner().Clean(scan, new HashSet<string> { "test" });

        Assert.Equal(1, report.Deleted);
        Assert.Equal(1, report.Skipped);
        Assert.True(File.Exists(locked));
        Assert.False(File.Exists(free));
    }

    [Fact]
    public void Junctions_are_never_followed()
    {
        var precious = _m.File(Path.Combine(_m.Documents, "precious.docx"));
        CreateJunction(Path.Combine(_m.Temp, "link"), _m.Documents);
        var tempFile = _m.File(Path.Combine(_m.Temp, "a.tmp"));

        _m.Run(Rule("%TEMP%"));

        Assert.True(File.Exists(precious));
        Assert.False(File.Exists(tempFile));
        Assert.True(Directory.Exists(Path.Combine(_m.Temp, "link"))); // the link itself is left alone
    }

    [Fact]
    public void Junction_swapped_in_after_scan_is_not_followed()
    {
        // Attack: scan sees a real folder, then it is replaced with a junction to Documents
        // containing a file with the same name and timestamp.
        var sub = Path.Combine(_m.Temp, "sub");
        _m.File(Path.Combine(sub, "same.docx"));
        var scan = _m.Scanner().Scan([Rule("%TEMP%")]);

        Directory.Delete(sub, recursive: true);
        var precious = _m.File(Path.Combine(_m.Documents, "same.docx"));
        File.SetLastWriteTimeUtc(precious, scan.Results[0].Files[0].LastWriteUtc);
        CreateJunction(sub, _m.Documents);

        var report = _m.Runner().Clean(scan, new HashSet<string> { "test" });

        Assert.True(File.Exists(precious));
        Assert.Equal(0, report.Deleted);
    }

    [Fact]
    public void Patterns_only_match_file_names()
    {
        var match = _m.File(Path.Combine(_m.LocalAppData, "CrashDumps", "app.dmp"));
        var other = _m.File(Path.Combine(_m.LocalAppData, "CrashDumps", "notes.txt"));

        _m.Run(Rule("%LOCALAPPDATA%\\CrashDumps", recursive: false, patterns: "*.dmp"));

        Assert.False(File.Exists(match));
        Assert.True(File.Exists(other));
    }

    [Fact]
    public void Non_recursive_rules_leave_subfolders_alone()
    {
        var top = _m.File(Path.Combine(_m.LocalAppData, "X", "a.tmp"));
        var nested = _m.File(Path.Combine(_m.LocalAppData, "X", "sub", "b.tmp"));

        _m.Run(Rule("%LOCALAPPDATA%\\X", recursive: false));

        Assert.False(File.Exists(top));
        Assert.True(File.Exists(nested));
    }

    [Fact]
    public void Empty_folders_are_removed_but_the_root_is_kept()
    {
        _m.File(Path.Combine(_m.Temp, "a", "b", "c.tmp"));

        _m.Run(Rule("%TEMP%"));

        Assert.True(Directory.Exists(_m.Temp));
        Assert.False(Directory.Exists(Path.Combine(_m.Temp, "a")));
    }

    [Theory]
    [InlineData("""[{"id":"a","name":"a","targets":[{"path":"C:\\Temp"}]}]""")]
    [InlineData("""[{"id":"a","name":"a","targets":[{"path":"%TEMP%\\..\\.."}]}]""")]
    [InlineData("""[{"id":"a","name":"a","targets":[{"path":"%TEMP%","patterns":["..\\*"]}]}]""")]
    [InlineData("""[{"id":"a","name":"a","targets":[]}]""")]
    [InlineData("""[{"id":"a","name":"a","targets":[{"path":"%TEMP%","skipFolders":["a\\b"]}]}]""")]
    [InlineData("""[{"id":"a","name":"a","targets":[{"path":"%TEMP%","skipFolders":[".."]}]}]""")]
    [InlineData("""[{"id":"a","name":"a","targets":[{"path":"%TEMP%","skipFolders":["*"]}]}]""")]
    [InlineData("""[{"id":"a","name":"a","targets":[{"path":"%TEMP%"}]},{"id":"a","name":"b","targets":[{"path":"%TEMP%"}]}]""")]
    public void Unsafe_rule_files_are_rejected(string json)
    {
        Assert.Throws<InvalidDataException>(() => RuleLoader.Validate(RuleLoader.Parse(json)));
    }

    [Fact]
    public void Admin_rules_are_not_scanned_without_elevation()
    {
        _m.File(Path.Combine(_m.Windir, "Temp", "old.log"));
        var rule = Rule("%WINDIR%\\Temp") with { RequiresAdmin = true };

        var scan = _m.Scanner(elevated: false).Scan([rule]);

        Assert.Equal(RuleStatus.NeedsAdmin, scan.Results[0].Status);
        Assert.Empty(scan.Results[0].Files);
    }

    [Fact]
    public void Recycle_bin_is_not_emptied_if_items_were_added_after_the_scan()
    {
        var rule = new CleanRule { Id = "bin", Name = "bin", Kind = RuleKind.RecycleBin };
        _m.RecycleBin.Bytes = 1000;
        _m.RecycleBin.AddAfterNextQuery = 500; // user bins a document while the results are on screen

        var scan = _m.Scanner().Scan([rule]);
        var report = _m.Runner().Clean(scan, new HashSet<string> { "bin" });

        Assert.False(_m.RecycleBin.Emptied);
        Assert.Equal(1, report.Skipped);
    }

    [Fact]
    public void Recycle_bin_is_not_emptied_if_contents_changed_but_total_shrank()
    {
        // User restores a big file (-900 bytes) and bins a small document (+1 item) after the scan.
        var rule = new CleanRule { Id = "bin", Name = "bin", Kind = RuleKind.RecycleBin };
        _m.RecycleBin.Bytes = 1000;
        _m.RecycleBin.AddAfterNextQuery = -900;
        _m.RecycleBin.AddItemsAfterNextQuery = 1;

        var scan = _m.Scanner().Scan([rule]);
        var report = _m.Runner().Clean(scan, new HashSet<string> { "bin" });

        Assert.False(_m.RecycleBin.Emptied);
        Assert.Equal(1, report.Skipped);
    }

    [Fact]
    public void Read_only_files_are_skipped_not_forced()
    {
        var file = _m.File(Path.Combine(_m.Temp, "ro.tmp"));
        File.SetAttributes(file, FileAttributes.ReadOnly);
        try
        {
            var (_, clean) = _m.Run(Rule("%TEMP%"));

            Assert.True(File.Exists(file));
            Assert.Equal(1, clean.Skipped);
        }
        finally
        {
            File.SetAttributes(file, FileAttributes.Normal);
        }
    }

    [Fact]
    public void Junction_root_is_refused_at_clean_time()
    {
        // The rule root itself is swapped for a junction to Documents after the scan.
        var root = Path.Combine(_m.LocalAppData, "SomeCache");
        _m.File(Path.Combine(root, "same.docx"));
        var scan = _m.Scanner().Scan([Rule("%LOCALAPPDATA%\\SomeCache")]);

        Directory.Delete(root, recursive: true);
        var precious = _m.File(Path.Combine(_m.Documents, "same.docx"));
        File.SetLastWriteTimeUtc(precious, scan.Results[0].Files[0].LastWriteUtc);
        CreateJunction(root, _m.Documents);

        var report = _m.Runner().Clean(scan, new HashSet<string> { "test" });

        Assert.True(File.Exists(precious));
        Assert.Equal(0, report.Deleted);
    }

    private static void CreateJunction(string link, string target)
    {
        using var p = Process.Start(new ProcessStartInfo("cmd.exe", $"/c mklink /J \"{link}\" \"{target}\"")
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
        })!;
        p.WaitForExit();
        Assert.True(Directory.Exists(link), "could not create junction");
    }
}
