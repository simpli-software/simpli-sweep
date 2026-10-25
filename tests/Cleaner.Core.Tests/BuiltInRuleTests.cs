using Cleaner.Core;

namespace Cleaner.Core.Tests;

/// <summary>Each built-in rule removes exactly what it claims, and nothing next to it.</summary>
public sealed class BuiltInRuleTests : IDisposable
{
    private static readonly IReadOnlyList<CleanRule> Rules = RuleLoader.LoadBuiltIn();
    private readonly FakeMachine _m = new();

    public void Dispose() => _m.Dispose();

    private static CleanRule R(string id) => Rules.Single(r => r.Id == id);

    [Fact]
    public void Built_in_rules_load_and_validate()
    {
        Assert.True(Rules.Count >= 10);
        RuleLoader.Validate(Rules);
    }

    [Fact]
    public void Every_built_in_rule_has_a_test()
    {
        var tested = typeof(BuiltInRuleTests).GetMethods()
            .Select(m => m.Name)
            .Where(n => n.StartsWith("Rule_"))
            .Select(n => n["Rule_".Length..].Replace('_', '-'))
            .ToHashSet();

        var untested = Rules.Select(r => r.Id).Where(id => !tested.Contains(id)).ToList();
        Assert.True(untested.Count == 0, "Rules without a test: " + string.Join(", ", untested));
    }

    [Fact]
    public void Every_built_in_rule_resolves_to_an_allowed_root()
    {
        foreach (var rule in Rules.Where(r => r.Kind == RuleKind.Files))
            foreach (var target in rule.Targets)
            {
                var expanded = _m.Tokens.Expand(target.Path);
                Assert.NotNull(expanded);
                Assert.True(_m.Guard.IsAllowedRoot(expanded!.Replace("*", "Default")), $"{rule.Id}: {target.Path}");
            }
    }

    [Fact]
    public void Rule_user_temp()
    {
        var old = _m.File(Path.Combine(_m.Temp, "x", "old.tmp"), ageHours: 30);
        var fresh = _m.File(Path.Combine(_m.Temp, "new.tmp"), ageHours: 2);

        _m.Run(R("user-temp"));

        Assert.False(File.Exists(old));
        Assert.True(File.Exists(fresh));
        Assert.True(Directory.Exists(_m.Temp));
    }

    [Fact]
    public void Rule_windows_temp()
    {
        var old = _m.File(Path.Combine(_m.Windir, "Temp", "setup.log"));
        var sys = _m.File(Path.Combine(_m.Windir, "System32", "drivers", "etc", "hosts"));

        _m.Run(R("windows-temp"));

        Assert.False(File.Exists(old));
        Assert.True(File.Exists(sys));
    }

    [Fact]
    public void Rule_recycle_bin()
    {
        _m.RecycleBin.Bytes = 5000;

        var (scan, clean) = _m.Run(R("recycle-bin"));

        Assert.Equal(5000, scan.TotalBytes);
        Assert.True(_m.RecycleBin.Emptied);
        Assert.Equal(5000, clean.BytesFreed);
    }

    [Fact]
    public void Rule_thumbnail_cache()
    {
        var dir = Path.Combine(_m.LocalAppData, "Microsoft", "Windows", "Explorer");
        var thumb = _m.File(Path.Combine(dir, "thumbcache_256.db"));
        var icon = _m.File(Path.Combine(dir, "iconcache_32.db"));
        var other = _m.File(Path.Combine(dir, "ExplorerStartupLog.etl"));

        _m.Run(R("thumbnail-cache"));

        Assert.False(File.Exists(thumb));
        Assert.False(File.Exists(icon));
        Assert.True(File.Exists(other));
    }

    [Fact]
    public void Rule_windows_update_cache()
    {
        var dl = Path.Combine(_m.Windir, "SoftwareDistribution");
        var old = _m.File(Path.Combine(dl, "Download", "abc", "update.cab"), ageHours: 100);
        var recent = _m.File(Path.Combine(dl, "Download", "def", "update.cab"), ageHours: 10);
        var datastore = _m.File(Path.Combine(dl, "DataStore", "DataStore.edb"), ageHours: 100);

        _m.Run(R("windows-update-cache"));

        Assert.False(File.Exists(old));
        Assert.True(File.Exists(recent));
        Assert.True(File.Exists(datastore));
    }

    [Fact]
    public void Rule_delivery_optimization()
    {
        var root = Path.Combine(_m.Windir, @"ServiceProfiles\NetworkService\AppData\Local\Microsoft\Windows\DeliveryOptimization");
        var cached = _m.File(Path.Combine(root, "Cache", "ab", "piece"));
        var state = _m.File(Path.Combine(root, "State", "dosvcState.dat"));

        _m.Run(R("delivery-optimization"));

        Assert.False(File.Exists(cached));
        Assert.True(File.Exists(state));
    }

    [Fact]
    public void Rule_user_crash_dumps()
    {
        var dump = _m.File(Path.Combine(_m.LocalAppData, "CrashDumps", "app.exe.123.dmp"));
        var other = _m.File(Path.Combine(_m.LocalAppData, "CrashDumps", "readme.txt"));

        _m.Run(R("user-crash-dumps"));

        Assert.False(File.Exists(dump));
        Assert.True(File.Exists(other));
    }

    [Fact]
    public void Rule_system_minidumps()
    {
        var dump = _m.File(Path.Combine(_m.Windir, "Minidump", "102426-01.dmp"));
        var memory = _m.File(Path.Combine(_m.Windir, "MEMORY.DMP"));

        _m.Run(R("system-minidumps"));

        Assert.False(File.Exists(dump));
        Assert.True(File.Exists(memory)); // outside the rule's folder
    }

    [Fact]
    public void Rule_error_reports()
    {
        var wer = Path.Combine(_m.LocalAppData, "Microsoft", "Windows", "WER");
        var archived = _m.File(Path.Combine(wer, "ReportArchive", "r1", "Report.wer"));
        var queued = _m.File(Path.Combine(wer, "ReportQueue", "r2", "Report.wer"));
        var temp = _m.File(Path.Combine(wer, "Temp", "keep.txt"));

        _m.Run(R("error-reports"));

        Assert.False(File.Exists(archived));
        Assert.False(File.Exists(queued));
        Assert.True(File.Exists(temp));
    }

    [Fact]
    public void Rule_directx_shader_cache()
    {
        Assert.False(R("directx-shader-cache").EnabledByDefault);
        var shader = _m.File(Path.Combine(_m.LocalAppData, "D3DSCache", "abc", "data.idx"));

        _m.Run(R("directx-shader-cache"));

        Assert.False(File.Exists(shader));
    }

    [Theory]
    [InlineData(@"Microsoft\Edge\User Data")]
    [InlineData(@"Google\Chrome\User Data")]
    [InlineData(@"BraveSoftware\Brave-Browser\User Data")]
    public void Chromium_rules_clear_cache_for_every_profile_but_keep_cookies_and_history(string userData)
    {
        var rule = Rules.Single(r => r.Targets.Any(t => t.Path.Contains(userData)));
        var root = Path.Combine(_m.LocalAppData, userData);

        var caches = new[] { "Default", "Profile 1" }.SelectMany(p => new[]
        {
            _m.File(Path.Combine(root, p, "Cache", "Cache_Data", "f_000001")),
            _m.File(Path.Combine(root, p, "Code Cache", "js", "abc")),
            _m.File(Path.Combine(root, p, "GPUCache", "data_0")),
        }).ToList();

        var keep = new[]
        {
            _m.File(Path.Combine(root, "Default", "Network", "Cookies")),
            _m.File(Path.Combine(root, "Default", "History")),
            _m.File(Path.Combine(root, "Default", "Login Data")),
            _m.File(Path.Combine(root, "Default", "Bookmarks")),
            _m.File(Path.Combine(root, "Local State")),
        };

        _m.Run(rule);

        Assert.All(caches, f => Assert.False(File.Exists(f)));
        Assert.All(keep, f => Assert.True(File.Exists(f)));
    }

    [Fact] public void Rule_edge_cache() => Chromium_rules_clear_cache_for_every_profile_but_keep_cookies_and_history(@"Microsoft\Edge\User Data");
    [Fact] public void Rule_chrome_cache() => Chromium_rules_clear_cache_for_every_profile_but_keep_cookies_and_history(@"Google\Chrome\User Data");
    [Fact] public void Rule_brave_cache() => Chromium_rules_clear_cache_for_every_profile_but_keep_cookies_and_history(@"BraveSoftware\Brave-Browser\User Data");

    [Fact]
    public void Rule_firefox_cache()
    {
        var profile = Path.Combine(_m.LocalAppData, "Mozilla", "Firefox", "Profiles", "abcd.default-release");
        var cache = _m.File(Path.Combine(profile, "cache2", "entries", "0A1B"));
        var roamingCookies = _m.File(Path.Combine(_m.AppData, "Mozilla", "Firefox", "Profiles", "abcd.default-release", "cookies.sqlite"));
        var startup = _m.File(Path.Combine(profile, "startupCache", "scriptCache.bin"));

        _m.Run(R("firefox-cache"));

        Assert.False(File.Exists(cache));
        Assert.True(File.Exists(roamingCookies));
        Assert.True(File.Exists(startup));
    }
}
