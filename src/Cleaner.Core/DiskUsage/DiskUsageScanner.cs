namespace Cleaner.Core.DiskUsage;

/// <summary>One folder in the size tree. Paths are built on demand to keep memory low on big drives.</summary>
public sealed class FolderNode
{
    private readonly List<FolderNode> _children = [];

    internal FolderNode(string name, FolderNode? parent)
    {
        Name = name;
        Parent = parent;
    }

    public string Name { get; }
    public FolderNode? Parent { get; }

    /// <summary>Bytes on disk in this folder and everything below it (cloud-only files excluded).</summary>
    public long Size { get; internal set; }
    public long FileCount { get; internal set; }

    /// <summary>Bytes in files directly in this folder (not in subfolders).</summary>
    public long OwnFilesSize { get; internal set; }

    public IReadOnlyList<FolderNode> Children => _children;

    /// <summary>Built by walking up the parents (no recursion, so very deep trees are fine).</summary>
    public string FullPath
    {
        get
        {
            var names = new List<string>();
            for (var n = this; n is not null; n = n.Parent)
                names.Add(n.Name);
            names.Reverse();
            return Path.Combine([.. names]);
        }
    }

    internal FolderNode AddChild(string name)
    {
        var child = new FolderNode(name, this);
        _children.Add(child);
        return child;
    }

    /// <summary>Sorts every folder's children largest first, iteratively.</summary>
    internal void SortTree()
    {
        var pending = new Stack<FolderNode>();
        pending.Push(this);
        while (pending.Count > 0)
        {
            var node = pending.Pop();
            node._children.Sort((a, b) => b.Size.CompareTo(a.Size));
            foreach (var c in node._children)
                pending.Push(c);
        }
    }
}

public sealed record LargeFile(string Path, long Size, DateTime LastWriteUtc);

public sealed record DiskUsageReport(FolderNode Root, IReadOnlyList<LargeFile> LargestFiles, int InaccessibleFolders);

public sealed record DiskScanProgress(long Files, long Bytes);

/// <summary>
/// Read-only size scan of a folder or drive. Never follows junctions or symlinks (no double counting,
/// no loops) and doesn't count cloud-only placeholders, which use no local space.
/// </summary>
public sealed class DiskUsageScanner
{
    private const FileAttributes NotOnDisk = FileAttributes.Offline | (FileAttributes)0x00400000; // RECALL_ON_DATA_ACCESS

    private static readonly EnumerationOptions Shallow = new()
    {
        RecurseSubdirectories = false,
        IgnoreInaccessible = false,
        AttributesToSkip = FileAttributes.ReparsePoint,
    };

    private readonly int _largestFileCount;

    public DiskUsageScanner(int largestFileCount = 50)
    {
        _largestFileCount = largestFileCount;
    }

    public DiskUsageReport Scan(string rootPath, IProgress<DiskScanProgress>? progress = null, CancellationToken ct = default)
    {
        // TrimEndingDirectorySeparator keeps a drive root as "C:\".
        var root = new FolderNode(Path.TrimEndingDirectorySeparator(Path.GetFullPath(rootPath)), null);
        var state = new ScanState(_largestFileCount, progress);

        ScanTop(root, root.Name, state, depth: 0, ct);
        root.SortTree();
        state.Report();
        return new DiskUsageReport(root, state.Largest(), state.Inaccessible);
    }

    /// <summary>Folders this close to the root are scanned in parallel; deeper subtrees on one thread each.</summary>
    private const int ParallelDepth = 4;

    /// <summary>Recursion here is bounded by <see cref="ParallelDepth"/>; below it the walk is iterative.</summary>
    private static void ScanTop(FolderNode node, string path, ScanState state, int depth, CancellationToken ct)
    {
        if (depth >= ParallelDepth)
        {
            ScanSubtree(node, path, state, ct);
            return;
        }

        var children = ScanFolder(node, path, state, ct).Select(name => (node: node.AddChild(name), path: Path.Combine(path, name))).ToList();

        Parallel.ForEach(
            children,
            new ParallelOptions { CancellationToken = ct, MaxDegreeOfParallelism = Math.Max(2, Environment.ProcessorCount) },
            child => ScanTop(child.node, child.path, state, depth + 1, ct));

        // Children are complete here, so their totals are final.
        foreach (var (child, _) in children)
        {
            node.Size += child.Size;
            node.FileCount += child.FileCount;
        }
    }

    /// <summary>
    /// Scans a whole subtree with an explicit stack, so folder depth can't overflow the call stack,
    /// then adds sizes up from the deepest folders to the top.
    /// </summary>
    private static void ScanSubtree(FolderNode start, string startPath, ScanState state, CancellationToken ct)
    {
        var visited = new List<FolderNode>();
        var pending = new Stack<(FolderNode Node, string Path)>();
        pending.Push((start, startPath));

        while (pending.Count > 0)
        {
            var (node, path) = pending.Pop();
            visited.Add(node);
            foreach (var name in ScanFolder(node, path, state, ct))
                pending.Push((node.AddChild(name), Path.Combine(path, name)));
        }

        // Every folder appears after its parent, so walking backwards finishes children first.
        for (var i = visited.Count - 1; i > 0; i--)
        {
            var node = visited[i];
            node.Parent!.Size += node.Size;
            node.Parent.FileCount += node.FileCount;
        }
    }

    /// <summary>Adds this folder's own files to <paramref name="node"/> and returns its subfolder names.</summary>
    private static List<string> ScanFolder(FolderNode node, string path, ScanState state, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var subfolders = new List<string>();
        long bytes = 0, files = 0;

        try
        {
            foreach (var entry in new DirectoryInfo(path).EnumerateFileSystemInfos("*", Shallow))
            {
                if (entry is DirectoryInfo)
                {
                    subfolders.Add(entry.Name);
                    continue;
                }

                if ((entry.Attributes & NotOnDisk) != 0)
                    continue;

                var file = (FileInfo)entry;
                bytes += file.Length;
                files++;
                state.OfferFile(file);
            }
        }
        catch (Exception e) when (e is UnauthorizedAccessException or IOException)
        {
            state.MarkInaccessible();
        }

        node.OwnFilesSize = bytes;
        node.Size += bytes;
        node.FileCount += files;
        state.AddTotals(files, bytes);
        return subfolders;
    }

    private sealed class ScanState(int capacity, IProgress<DiskScanProgress>? progress)
    {
        private readonly Lock _gate = new();
        private readonly PriorityQueue<LargeFile, long> _largest = new();
        private long _files;
        private long _bytes;
        private int _inaccessible;
        private long _lastReport;

        public int Inaccessible => _inaccessible;

        public void MarkInaccessible() => Interlocked.Increment(ref _inaccessible);

        public void AddTotals(long files, long bytes)
        {
            var total = Interlocked.Add(ref _files, files);
            Interlocked.Add(ref _bytes, bytes);

            // Report about every 5,000 files so the UI stays responsive without being flooded.
            var bucket = total / 5000;
            if (progress is not null && bucket != Interlocked.Exchange(ref _lastReport, bucket))
                Report();
        }

        public void Report() =>
            progress?.Report(new DiskScanProgress(Interlocked.Read(ref _files), Interlocked.Read(ref _bytes)));

        // Size a file must beat to enter the full list; read without the lock so most files skip it.
        private long _threshold = -1;

        public void OfferFile(FileInfo file)
        {
            var size = file.Length;
            if (capacity <= 0 || size <= Volatile.Read(ref _threshold))
                return;

            lock (_gate)
            {
                if (_largest.Count < capacity)
                {
                    _largest.Enqueue(new LargeFile(file.FullName, size, file.LastWriteTimeUtc), size);
                }
                else if (_largest.TryPeek(out _, out var smallest) && size > smallest)
                {
                    _largest.DequeueEnqueue(new LargeFile(file.FullName, size, file.LastWriteTimeUtc), size);
                }

                if (_largest.Count == capacity && _largest.TryPeek(out _, out var min))
                    Volatile.Write(ref _threshold, min);
            }
        }

        public IReadOnlyList<LargeFile> Largest()
        {
            lock (_gate)
                return _largest.UnorderedItems.Select(i => i.Element).OrderByDescending(f => f.Size).ToList();
        }
    }
}
