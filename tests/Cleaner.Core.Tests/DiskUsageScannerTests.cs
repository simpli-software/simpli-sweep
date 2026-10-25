using System.Diagnostics;
using Cleaner.Core.DiskUsage;

namespace Cleaner.Core.Tests;

public sealed class DiskUsageScannerTests : IDisposable
{
    private readonly FakeMachine _m = new();

    public void Dispose() => _m.Dispose();

    private string Root => Path.Combine(_m.Root, "Drive");

    [Fact]
    public void Folder_sizes_add_up_and_are_sorted_largest_first()
    {
        _m.File(Path.Combine(Root, "small", "a.bin"), bytes: 100);
        _m.File(Path.Combine(Root, "big", "b.bin"), bytes: 5000);
        _m.File(Path.Combine(Root, "big", "nested", "c.bin"), bytes: 3000);
        _m.File(Path.Combine(Root, "top.bin"), bytes: 10);

        var report = new DiskUsageScanner().Scan(Root);

        Assert.Equal(8110, report.Root.Size);
        Assert.Equal(4, report.Root.FileCount);
        Assert.Equal(10, report.Root.OwnFilesSize);
        Assert.Equal(["big", "small"], report.Root.Children.Select(c => c.Name));
        var big = report.Root.Children[0];
        Assert.Equal(8000, big.Size);
        Assert.Equal(5000, big.OwnFilesSize);
        Assert.Equal(Path.Combine(Root, "big", "nested"), big.Children[0].FullPath);
    }

    [Fact]
    public void Largest_files_are_capped_and_ordered()
    {
        for (var i = 1; i <= 10; i++)
            _m.File(Path.Combine(Root, $"d{i % 3}", $"f{i}.bin"), bytes: i * 100);

        var report = new DiskUsageScanner(largestFileCount: 3).Scan(Root);

        Assert.Equal([1000L, 900L, 800L], report.LargestFiles.Select(f => f.Size));
        Assert.EndsWith("f10.bin", report.LargestFiles[0].Path);
    }

    [Fact]
    public void Junctions_are_not_followed_or_double_counted()
    {
        _m.File(Path.Combine(Root, "real", "data.bin"), bytes: 1000);
        CreateJunction(Path.Combine(Root, "link"), Path.Combine(Root, "real"));

        var report = new DiskUsageScanner().Scan(Root);

        Assert.Equal(1000, report.Root.Size);
        Assert.DoesNotContain(report.Root.Children, c => c.Name == "link");
    }

    [Fact]
    public void Cloud_only_files_are_not_counted()
    {
        _m.File(Path.Combine(Root, "local.bin"), bytes: 1000);
        var cloud = _m.File(Path.Combine(Root, "cloud.bin"), bytes: 9000);
        File.SetAttributes(cloud, FileAttributes.Offline);

        var report = new DiskUsageScanner().Scan(Root);

        Assert.Equal(1000, report.Root.Size);
        Assert.Single(report.LargestFiles);
    }

    [Fact]
    public void Scanning_never_changes_anything()
    {
        var file = _m.File(Path.Combine(Root, "x", "keep.bin"), bytes: 500);
        var before = File.GetLastWriteTimeUtc(file);

        new DiskUsageScanner().Scan(Root);

        Assert.True(File.Exists(file));
        Assert.Equal(before, File.GetLastWriteTimeUtc(file));
    }

    [Fact]
    public void Progress_reaches_the_final_count()
    {
        for (var i = 0; i < 12; i++)
            _m.File(Path.Combine(Root, $"d{i % 4}", $"f{i}.bin"), bytes: 10);
        var reports = new List<DiskScanProgress>();

        new DiskUsageScanner().Scan(Root, new SyncProgress(reports.Add));

        Assert.Equal(12, reports[^1].Files);
        Assert.Equal(120, reports[^1].Bytes);
    }

    [Fact]
    public void Very_deep_folder_trees_do_not_crash()
    {
        // Deep trees are walked with an explicit stack below the first few levels, so depth can't
        // overflow the call stack (a recursive walk crashed at ~6,600 levels). 600 levels keeps
        // this test fast: Windows re-walks every level of a path on each open.
        const int depth = 600;
        var paths = new List<string>(depth);
        var path = Path.Combine(Root, "deep");
        for (var i = 0; i < depth; i++)
        {
            path = Path.Combine(path, "d");
            paths.Add(path);
        }
        Directory.CreateDirectory(path);
        File.WriteAllBytes(Path.Combine(path, "leaf.bin"), new byte[123]);

        try
        {
            var report = new DiskUsageScanner().Scan(Root);

            Assert.Equal(123, report.Root.Size);
            Assert.Equal(123, report.Root.Children.Single().Size);
            Assert.EndsWith("leaf.bin", report.LargestFiles.Single().Path);
        }
        finally
        {
            // Remove bottom-up: a recursive delete would hit the same depth problem.
            File.Delete(Path.Combine(path, "leaf.bin"));
            for (var i = paths.Count - 1; i >= 0; i--)
                Directory.Delete(paths[i]);
        }
    }

    [Fact]
    public void Cancellation_stops_the_scan()
    {
        _m.File(Path.Combine(Root, "a", "f.bin"));
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.ThrowsAny<OperationCanceledException>(() => new DiskUsageScanner().Scan(Root, ct: cts.Token));
    }

    private sealed class SyncProgress(Action<DiskScanProgress> report) : IProgress<DiskScanProgress>
    {
        private readonly Lock _gate = new();

        public void Report(DiskScanProgress value)
        {
            lock (_gate)
                report(value);
        }
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
