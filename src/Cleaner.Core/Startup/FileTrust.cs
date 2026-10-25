using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Cleaner.Core.Startup;

public enum SignatureStatus
{
    Unknown,
    Missing,
    Unsigned,
    Valid,
    Invalid,
}

public sealed record FileDetails(string? Publisher, SignatureStatus Signature);

public interface IShortcutResolver
{
    string? Resolve(string lnkPath);
}

public interface IFileTrust
{
    FileDetails Inspect(string? path);
}

/// <summary>Reads the publisher and checks the Authenticode signature. Offline: no revocation lookups.</summary>
public sealed partial class WindowsFileTrust : IFileTrust
{
    public FileDetails Inspect(string? path)
    {
        if (path is null)
            return new FileDetails(null, SignatureStatus.Unknown);
        if (!File.Exists(path))
            return new FileDetails(null, SignatureStatus.Missing);

        string? publisher = null;
        try { publisher = FileVersionInfo.GetVersionInfo(path).CompanyName?.Trim(); }
        catch (FileNotFoundException) { }

        return new FileDetails(string.IsNullOrEmpty(publisher) ? null : publisher, Verify(path));
    }

    private static readonly Guid GenericVerifyV2 = new("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");

    private const uint WTD_UI_NONE = 2;
    private const uint WTD_REVOKE_NONE = 0;
    private const uint WTD_CHOICE_FILE = 1;
    private const uint WTD_STATEACTION_VERIFY = 1;
    private const uint WTD_STATEACTION_CLOSE = 2;
    private const uint WTD_CACHE_ONLY_URL_RETRIEVAL = 0x1000;
    private const int TRUST_E_NOSIGNATURE = unchecked((int)0x800B0100);
    private const int TRUST_E_SUBJECT_FORM_UNKNOWN = unchecked((int)0x800B0003);
    private const int TRUST_E_PROVIDER_UNKNOWN = unchecked((int)0x800B0001);

    [StructLayout(LayoutKind.Sequential)]
    private struct WINTRUST_FILE_INFO
    {
        public uint cbStruct;
        public IntPtr pcwszFilePath;
        public IntPtr hFile;
        public IntPtr pgKnownSubject;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WINTRUST_DATA
    {
        public uint cbStruct;
        public IntPtr pPolicyCallbackData;
        public IntPtr pSIPClientData;
        public uint dwUIChoice;
        public uint fdwRevocationChecks;
        public uint dwUnionChoice;
        public IntPtr pFile;
        public uint dwStateAction;
        public IntPtr hWVTStateData;
        public IntPtr pwszURLReference;
        public uint dwProvFlags;
        public uint dwUIContext;
        public IntPtr pSignatureSettings;
    }

    [LibraryImport("wintrust.dll")]
    private static unsafe partial int WinVerifyTrust(IntPtr hwnd, Guid* action, WINTRUST_DATA* data);

    [StructLayout(LayoutKind.Sequential)]
    private struct WINTRUST_CATALOG_INFO
    {
        public uint cbStruct;
        public uint dwCatalogVersion;
        public IntPtr pcwszCatalogFilePath;
        public IntPtr pcwszMemberTag;
        public IntPtr pcwszMemberFilePath;
        public IntPtr hMemberFile;
        public IntPtr pbCalculatedFileHash;
        public uint cbCalculatedFileHash;
        public IntPtr pcCatalogContext;
        public IntPtr hCatAdmin;
    }

    [StructLayout(LayoutKind.Sequential)]
    private unsafe struct CATALOG_INFO
    {
        public uint cbStruct;
        public fixed char wszCatalogFile[260];
    }

    private const uint WTD_CHOICE_CATALOG = 2;

    [LibraryImport("wintrust.dll", StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CryptCATAdminAcquireContext2(out IntPtr catAdmin, IntPtr subsystem, string hashAlgorithm, IntPtr strongHashPolicy, uint flags);

    [LibraryImport("wintrust.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static unsafe partial bool CryptCATAdminCalcHashFromFileHandle2(IntPtr catAdmin, SafeHandle file, uint* hashSize, byte* hash, uint flags);

    [LibraryImport("wintrust.dll")]
    private static unsafe partial IntPtr CryptCATAdminEnumCatalogFromHash(IntPtr catAdmin, byte* hash, uint hashSize, uint flags, IntPtr* previous);

    [LibraryImport("wintrust.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static unsafe partial bool CryptCATCatalogInfoFromContext(IntPtr catInfo, CATALOG_INFO* info, uint flags);

    [LibraryImport("wintrust.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CryptCATAdminReleaseCatalogContext(IntPtr catAdmin, IntPtr catInfo, uint flags);

    [LibraryImport("wintrust.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CryptCATAdminReleaseContext(IntPtr catAdmin, uint flags);

    private static readonly string[] CatalogHashes = ["SHA256", "SHA1"];

    private static SignatureStatus Verify(string path)
    {
        var embedded = VerifyEmbedded(path);
        if (embedded != SignatureStatus.Unsigned)
            return embedded;

        // Many Windows and driver files are signed through a system catalog, not in the file itself.
        foreach (var algorithm in CatalogHashes)
        {
            if (VerifyCatalog(path, algorithm) is { } catalog)
                return catalog;
        }
        return SignatureStatus.Unsigned;
    }

    private static unsafe SignatureStatus VerifyEmbedded(string path)
    {
        var pathPtr = Marshal.StringToHGlobalUni(path);
        try
        {
            var file = new WINTRUST_FILE_INFO { cbStruct = (uint)sizeof(WINTRUST_FILE_INFO), pcwszFilePath = pathPtr };
            var data = NewData(WTD_CHOICE_FILE, (IntPtr)(&file));
            return Run(&data);
        }
        finally
        {
            Marshal.FreeHGlobal(pathPtr);
        }
    }

    /// <summary>Null if no catalog lists this file.</summary>
    private static unsafe SignatureStatus? VerifyCatalog(string path, string algorithm)
    {
        if (!CryptCATAdminAcquireContext2(out var catAdmin, IntPtr.Zero, algorithm, IntPtr.Zero, 0))
            return null;

        var catInfo = IntPtr.Zero;
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var hash = stackalloc byte[64];
            uint hashSize = 64;
            if (!CryptCATAdminCalcHashFromFileHandle2(catAdmin, stream.SafeFileHandle, &hashSize, hash, 0))
                return null;

            catInfo = CryptCATAdminEnumCatalogFromHash(catAdmin, hash, hashSize, 0, null);
            if (catInfo == IntPtr.Zero)
                return null;

            var info = new CATALOG_INFO { cbStruct = (uint)sizeof(CATALOG_INFO) };
            if (!CryptCATCatalogInfoFromContext(catInfo, &info, 0))
                return null;

            var tagPtr = Marshal.StringToHGlobalUni(Convert.ToHexString(new ReadOnlySpan<byte>(hash, (int)hashSize)));
            var pathPtr = Marshal.StringToHGlobalUni(path);
            try
            {
                var catalog = new WINTRUST_CATALOG_INFO
                {
                    cbStruct = (uint)sizeof(WINTRUST_CATALOG_INFO),
                    pcwszCatalogFilePath = (IntPtr)info.wszCatalogFile,
                    pcwszMemberTag = tagPtr,
                    pcwszMemberFilePath = pathPtr,
                    hMemberFile = stream.SafeFileHandle.DangerousGetHandle(),
                    pbCalculatedFileHash = (IntPtr)hash,
                    cbCalculatedFileHash = hashSize,
                    hCatAdmin = catAdmin,
                };
                var data = NewData(WTD_CHOICE_CATALOG, (IntPtr)(&catalog));
                return Run(&data);
            }
            finally
            {
                Marshal.FreeHGlobal(tagPtr);
                Marshal.FreeHGlobal(pathPtr);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
        finally
        {
            if (catInfo != IntPtr.Zero)
                CryptCATAdminReleaseCatalogContext(catAdmin, catInfo, 0);
            CryptCATAdminReleaseContext(catAdmin, 0);
        }
    }

    private static unsafe WINTRUST_DATA NewData(uint choice, IntPtr union) => new()
    {
        cbStruct = (uint)sizeof(WINTRUST_DATA),
        dwUIChoice = WTD_UI_NONE,
        fdwRevocationChecks = WTD_REVOKE_NONE,
        dwUnionChoice = choice,
        pFile = union,
        dwStateAction = WTD_STATEACTION_VERIFY,
        dwProvFlags = WTD_CACHE_ONLY_URL_RETRIEVAL,
    };

    private static unsafe SignatureStatus Run(WINTRUST_DATA* data)
    {
        var action = GenericVerifyV2;
        var result = WinVerifyTrust(IntPtr.Zero, &action, data);

        data->dwStateAction = WTD_STATEACTION_CLOSE;
        WinVerifyTrust(IntPtr.Zero, &action, data);

        return result switch
        {
            0 => SignatureStatus.Valid,
            TRUST_E_NOSIGNATURE or TRUST_E_SUBJECT_FORM_UNKNOWN or TRUST_E_PROVIDER_UNKNOWN => SignatureStatus.Unsigned,
            _ => SignatureStatus.Invalid,
        };
    }
}

/// <summary>Resolves .lnk targets via the Windows Script Host shell object.</summary>
public sealed class WindowsShortcutResolver : IShortcutResolver
{
    public string? Resolve(string lnkPath)
    {
        try
        {
            var type = Type.GetTypeFromProgID("WScript.Shell");
            if (type is null)
                return null;

            dynamic shell = Activator.CreateInstance(type)!;
            dynamic? shortcut = null;
            try
            {
                shortcut = shell.CreateShortcut(lnkPath);
                string target = shortcut.TargetPath;
                return string.IsNullOrWhiteSpace(target) ? null : target;
            }
            finally
            {
                if (shortcut is not null)
                    Marshal.FinalReleaseComObject(shortcut);
                Marshal.FinalReleaseComObject(shell);
            }
        }
        catch (Exception)
        {
            return null; // one broken shortcut must not hide the rest of the list
        }
    }
}
