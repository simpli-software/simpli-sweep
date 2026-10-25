using Cleaner.Core;
using Microsoft.Extensions.Time.Testing;

namespace Cleaner.Core.Tests;

/// <summary>
/// A throwaway folder tree laid out like a Windows machine, so rules run against real files
/// without ever touching the real profile.
/// </summary>
public sealed class FakeMachine : IDisposable
{
    public string Root { get; } = Path.Combine(Path.GetTempPath(), "cleaner-tests", Guid.NewGuid().ToString("N"));
    public FakeTimeProvider Clock { get; } = new(new DateTimeOffset(2026, 10, 24, 12, 0, 0, TimeSpan.Zero));
    public FakeRecycleBin RecycleBin { get; } = new();

    public string Windir => Path.Combine(Root, "Windows");
    public string Profile => Path.Combine(Root, "Users", "test");
    public string LocalAppData => Path.Combine(Profile, "AppData", "Local");
    public string AppData => Path.Combine(Profile, "AppData", "Roaming");
    public string Temp => Path.Combine(LocalAppData, "Temp");
    public string ProgramData => Path.Combine(Root, "ProgramData");
    public string Documents => Path.Combine(Profile, "Documents");

    public FakeMachine()
    {
        foreach (var dir in new[] { Windir, Temp, AppData, ProgramData, Documents })
            Directory.CreateDirectory(dir);

        Tokens = new PathTokens(new Dictionary<string, string>
        {
            ["TEMP"] = Temp,
            ["LOCALAPPDATA"] = LocalAppData,
            ["APPDATA"] = AppData,
            ["USERPROFILE"] = Profile,
            ["PROGRAMDATA"] = ProgramData,
            ["WINDIR"] = Windir,
        });

        Guard = new SafetyGuard(
            denyRoots: [Windir, Documents, Path.Combine(Profile, "Downloads"), Path.Combine(Profile, "OneDrive")],
            allowExceptions: [Path.Combine(Windir, "Temp"), Path.Combine(Windir, "Minidump"), Path.Combine(Windir, "SoftwareDistribution", "Download"),
                Path.Combine(Windir, @"ServiceProfiles\NetworkService\AppData\Local\Microsoft\Windows\DeliveryOptimization\Cache")],
            neverRoots: [Profile, Path.Combine(Profile, "AppData"), LocalAppData, AppData, ProgramData]);
    }

    public PathTokens Tokens { get; }
    public SafetyGuard Guard { get; }

    public Scanner Scanner(bool elevated = true) => new(Tokens, Guard, RecycleBin, Clock, elevated);
    public CleanRunner Runner() => new(Guard, RecycleBin, Clock);

    /// <summary>Creates a file with the given size and age (relative to the fake clock).</summary>
    public string File(string path, int bytes = 100, double ageHours = 48)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        System.IO.File.WriteAllBytes(path, new byte[bytes]);
        System.IO.File.SetLastWriteTimeUtc(path, Clock.GetUtcNow().UtcDateTime.AddHours(-ageHours));
        return path;
    }

    /// <summary>Scans then cleans the given rules, as the UI will.</summary>
    public (ScanReport Scan, CleanReport Clean) Run(params CleanRule[] rules)
    {
        var scan = Scanner().Scan(rules);
        var clean = Runner().Clean(scan, rules.Select(r => r.Id).ToHashSet());
        return (scan, clean);
    }

    public void Dispose()
    {
        try
        {
            // Unlink junctions first so the recursive delete never reaches through them.
            var links = Directory.EnumerateDirectories(Root, "*", new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = 0 })
                .Where(d => System.IO.File.GetAttributes(d).HasFlag(FileAttributes.ReparsePoint))
                .ToList();
            foreach (var link in links)
                Directory.Delete(link);

            Directory.Delete(Root, recursive: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }
}

public sealed class FakeRecycleBin : IRecycleBin
{
    public long Bytes { get; set; }
    public bool Emptied { get; private set; }

    /// <summary>Simulates the user binning something between the scan and the clean.</summary>
    public long AddAfterNextQuery { get; set; }
    public long Items { get; set; } = 1;
    public long AddItemsAfterNextQuery { get; set; }

    public (long Bytes, long Items) Query()
    {
        var result = (Bytes, Bytes > 0 ? Items : 0L);
        Bytes += AddAfterNextQuery;
        Items += AddItemsAfterNextQuery;
        AddAfterNextQuery = AddItemsAfterNextQuery = 0;
        return result;
    }

    public bool Empty()
    {
        Emptied = true;
        Bytes = 0;
        return true;
    }
}
