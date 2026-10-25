using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Cleaner.Core;

/// <summary>
/// Deletes through an open handle so a folder swapped for a junction cannot redirect the delete:
/// the handle pins the object actually opened, its real path is checked, and only then is it
/// marked for deletion. Checking a path and then deleting by name would leave a race.
/// </summary>
internal static partial class SafeDelete
{
    private const uint DELETE = 0x00010000;
    private const uint FILE_READ_ATTRIBUTES = 0x80;
    private const uint FILE_SHARE_ALL = 0x1 | 0x2 | 0x4;
    private const uint OPEN_EXISTING = 3;
    private const uint FILE_FLAG_OPEN_REPARSE_POINT = 0x00200000;
    private const uint FILE_FLAG_BACKUP_SEMANTICS = 0x02000000;
    private const int FileBasicInfo = 0;
    private const int FileDispositionInfo = 4;

    [StructLayout(LayoutKind.Sequential)]
    private struct FILE_BASIC_INFO
    {
        public long CreationTime;
        public long LastAccessTime;
        public long LastWriteTime;
        public long ChangeTime;
        public uint FileAttributes;
    }

    [LibraryImport("kernel32.dll", EntryPoint = "CreateFileW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr security, uint disposition, uint flags, IntPtr template);

    [LibraryImport("kernel32.dll", EntryPoint = "GetFinalPathNameByHandleW", SetLastError = true)]
    private static unsafe partial uint GetFinalPathNameByHandle(SafeFileHandle handle, char* buffer, uint size, uint flags);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static unsafe partial bool GetFileInformationByHandleEx(SafeFileHandle handle, int infoClass, void* info, uint size);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static unsafe partial bool SetFileInformationByHandle(SafeFileHandle handle, int infoClass, void* info, uint size);

    /// <summary>The real, fully resolved path of an existing file or folder, or null.</summary>
    public static string? FinalPath(string path)
    {
        using var handle = Open(path, FILE_READ_ATTRIBUTES, followReparse: true);
        return handle.IsInvalid ? null : FinalPath(handle);
    }

    /// <param name="accept">Called with the object's real path, attributes and last write (UTC); delete only if it returns true.</param>
    /// <returns>True if the object was deleted.</returns>
    public static unsafe bool TryDelete(string path, Func<string, FileAttributes, DateTime, bool> accept)
    {
        // OPEN_REPARSE_POINT: if the path itself is a link, open the link, never its target.
        using var handle = Open(path, DELETE | FILE_READ_ATTRIBUTES, followReparse: false);
        if (handle.IsInvalid)
            return false;

        FILE_BASIC_INFO basic;
        if (!GetFileInformationByHandleEx(handle, FileBasicInfo, &basic, (uint)sizeof(FILE_BASIC_INFO)))
            return false;

        var attributes = (FileAttributes)basic.FileAttributes;
        var finalPath = FinalPath(handle);
        if (finalPath is null || attributes.HasFlag(FileAttributes.ReparsePoint))
            return false;

        if (!accept(finalPath, attributes, DateTime.FromFileTimeUtc(basic.LastWriteTime)))
            return false;

        // Marks the opened object for deletion; it goes when the handle closes.
        // Fails for read-only files and non-empty folders, which is what we want.
        byte deleteFile = 1;
        return SetFileInformationByHandle(handle, FileDispositionInfo, &deleteFile, sizeof(byte));
    }

    private static SafeFileHandle Open(string path, uint access, bool followReparse)
    {
        var flags = FILE_FLAG_BACKUP_SEMANTICS | (followReparse ? 0 : FILE_FLAG_OPEN_REPARSE_POINT);
        return CreateFile(path, access, FILE_SHARE_ALL, IntPtr.Zero, OPEN_EXISTING, flags, IntPtr.Zero);
    }

    private static unsafe string? FinalPath(SafeFileHandle handle)
    {
        var buffer = new char[1024];
        while (true)
        {
            uint length;
            fixed (char* p = buffer)
                length = GetFinalPathNameByHandle(handle, p, (uint)buffer.Length, 0);

            if (length == 0)
                return null;
            if (length < buffer.Length)
                return StripPrefix(new string(buffer, 0, (int)length));
            buffer = new char[length + 1];
        }
    }

    private static string? StripPrefix(string path)
    {
        if (path.StartsWith(@"\\?\UNC\", StringComparison.Ordinal))
            return @"\\" + path[8..];
        if (path.StartsWith(@"\\?\", StringComparison.Ordinal))
            return path[4..];
        return path;
    }
}

/// <summary>Process-wide hardening applied at startup by the app and the CLI.</summary>
public static partial class ProcessHardening
{
    private const int ProcessRedirectionTrustPolicy = 16;

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static unsafe partial bool SetProcessMitigationPolicy(int policy, void* buffer, nuint length);

    /// <summary>
    /// Refuses to follow junctions created by non-admin users (Windows 11 22H2+). Best effort:
    /// older Windows ignores it, and the handle-based delete still protects there.
    /// </summary>
    public static unsafe bool Apply()
    {
        uint enforce = 1; // EnforceRedirectionTrust
        try { return SetProcessMitigationPolicy(ProcessRedirectionTrustPolicy, &enforce, sizeof(uint)); }
        catch (Win32Exception) { return false; }
        catch (EntryPointNotFoundException) { return false; }
    }
}
