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

    private static unsafe SignatureStatus Verify(string path)
    {
        var pathPtr = Marshal.StringToHGlobalUni(path);
        try
        {
            var file = new WINTRUST_FILE_INFO { cbStruct = (uint)sizeof(WINTRUST_FILE_INFO), pcwszFilePath = pathPtr };
            var data = new WINTRUST_DATA
            {
                cbStruct = (uint)sizeof(WINTRUST_DATA),
                dwUIChoice = WTD_UI_NONE,
                fdwRevocationChecks = WTD_REVOKE_NONE,
                dwUnionChoice = WTD_CHOICE_FILE,
                pFile = (IntPtr)(&file),
                dwStateAction = WTD_STATEACTION_VERIFY,
                dwProvFlags = WTD_CACHE_ONLY_URL_RETRIEVAL,
            };
            var action = GenericVerifyV2;

            var result = WinVerifyTrust(IntPtr.Zero, &action, &data);

            data.dwStateAction = WTD_STATEACTION_CLOSE;
            WinVerifyTrust(IntPtr.Zero, &action, &data);

            return result switch
            {
                0 => SignatureStatus.Valid,
                TRUST_E_NOSIGNATURE or TRUST_E_SUBJECT_FORM_UNKNOWN or TRUST_E_PROVIDER_UNKNOWN => SignatureStatus.Unsigned,
                _ => SignatureStatus.Invalid,
            };
        }
        finally
        {
            Marshal.FreeHGlobal(pathPtr);
        }
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
            try
            {
                dynamic shortcut = shell.CreateShortcut(lnkPath);
                string target = shortcut.TargetPath;
                Marshal.FinalReleaseComObject(shortcut);
                return string.IsNullOrWhiteSpace(target) ? null : target;
            }
            finally
            {
                Marshal.FinalReleaseComObject(shell);
            }
        }
        catch (COMException)
        {
            return null;
        }
    }
}
