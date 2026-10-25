using Cleaner.Core.Startup;

namespace Cleaner.Core.Tests;

/// <summary>Runs the real Windows signature checks (Windows only, offline).</summary>
public sealed class FileTrustTests : IDisposable
{
    private readonly FakeMachine _m = new();
    private readonly WindowsFileTrust _trust = new();

    public void Dispose() => _m.Dispose();

    [Fact]
    public void Catalog_signed_Windows_files_are_valid_not_unsigned()
    {
        // cmd.exe carries no embedded signature; Windows signs it through a system catalog.
        var cmd = Path.Combine(Environment.SystemDirectory, "cmd.exe");

        Assert.Equal(SignatureStatus.Valid, _trust.Inspect(cmd).Signature);
    }

    [Fact]
    public void Embedded_signatures_are_valid()
    {
        var coreLib = typeof(object).Assembly.Location; // Microsoft-signed runtime file

        var details = _trust.Inspect(coreLib);

        Assert.Equal(SignatureStatus.Valid, details.Signature);
        Assert.Equal("Microsoft Corporation", details.Publisher);
    }

    [Fact]
    public void Unsigned_files_are_reported_as_unsigned()
    {
        var file = _m.File(Path.Combine(_m.Temp, "plain.exe"), bytes: 4096);

        Assert.Equal(SignatureStatus.Unsigned, _trust.Inspect(file).Signature);
    }

    [Fact]
    public void Missing_and_unknown_paths_are_distinguished()
    {
        Assert.Equal(SignatureStatus.Missing, _trust.Inspect(Path.Combine(_m.Temp, "gone.exe")).Signature);
        Assert.Equal(SignatureStatus.Unknown, _trust.Inspect(null).Signature);
    }

    [Fact]
    public void Broken_shortcut_resolves_to_null_instead_of_throwing()
    {
        var lnk = _m.File(Path.Combine(_m.Temp, "broken.lnk"), bytes: 10);

        Assert.Null(new WindowsShortcutResolver().Resolve(lnk));
    }
}
