using System.Runtime.InteropServices;

namespace Clawleash.Sandbox;

/// <summary>
/// Windows AppContainer API用のP/Invoke定義
/// </summary>
internal static class NativeMethods
{
    #region Constants

    public const uint EXTENDED_STARTUPINFO_PRESENT = 0x00080000;
    public const uint CREATE_NO_WINDOW = 0x08000000;
    public const uint CREATE_UNICODE_ENVIRONMENT = 0x00000400;

    // Error codes
    public const int ERROR_SUCCESS = 0;
    public const int ERROR_ALREADY_EXISTS = -2147024713; // 0x80071392

    // AppContainer Capabilities (Well-known SIDs)
    // https://docs.microsoft.com/en-us/windows/win32/secauthz/well-known-sids
    public const string INTERNET_CLIENT_SID = "S-1-15-3-1";
    public const string INTERNET_CLIENT_SERVER_SID = "S-1-15-3-2";
    public const string PRIVATE_NETWORK_CLIENT_SERVER_SID = "S-1-15-3-3";
    public const string PICTURES_LIBRARY_SID = "S-1-15-3-4";
    public const string VIDEOS_LIBRARY_SID = "S-1-15-3-5";
    public const string MUSIC_LIBRARY_SID = "S-1-15-3-6";
    public const string DOCUMENTS_LIBRARY_SID = "S-1-15-3-7";
    public const string ENTERPRISE_AUTHENTICATION_SID = "S-1-15-3-8";
    public const string SHARED_USER_CERTIFICATES_SID = "S-1-15-3-9";
    public const string REMOVABLE_STORAGE_SID = "S-1-15-3-10";
    public const string APPOINTMENTS_SID = "S-1-15-3-11";
    public const string CONTACTS_SID = "S-1-15-3-12";

    // Access permissions
    public const uint GENERIC_READ = 0x80000000;
    public const uint GENERIC_WRITE = 0x40000000;
    public const uint GENERIC_EXECUTE = 0x20000000;
    public const uint GENERIC_ALL = 0x10000000;

    // Inheritance flags
    public const uint SUB_CONTAINERS_AND_OBJECTS_INHERIT = 0x00000003;

    // Security information
    public const uint DACL_SECURITY_INFORMATION = 0x00000004;

    // Trustee types
    public const int NO_MULTIPLE_TRUSTEE = 0;
    public const int TRUSTEE_IS_SID = 0;
    public const int TRUSTEE_IS_WELL_KNOWN_GROUP = 5;

    // Access modes
    public const int GRANT_ACCESS = 1;

    // Object types
    public const int SE_FILE_OBJECT = 1;

    #endregion

    #region userenv.dll - AppContainer Functions

    [DllImport("userenv.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern int CreateAppContainerProfile(
        [MarshalAs(UnmanagedType.LPWStr)] string pszAppContainerName,
        [MarshalAs(UnmanagedType.LPWStr)] string pszDisplayName,
        [MarshalAs(UnmanagedType.LPWStr)] string pszDescription,
        IntPtr pCapabilities,
        uint dwCapabilityCount,
        out IntPtr ppSid);

    [DllImport("userenv.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern int DeleteAppContainerProfile(
        [MarshalAs(UnmanagedType.LPWStr)] string pszAppContainerName);

    [DllImport("userenv.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern int DeriveAppContainerSidFromAppContainerName(
        [MarshalAs(UnmanagedType.LPWStr)] string pszAppContainerName,
        out IntPtr ppSid);

    [DllImport("userenv.dll", SetLastError = true)]
    public static extern void FreeSid(IntPtr pSid);

    #endregion

    #region kernel32.dll - Process Functions

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern bool CreateProcess(
        string? lpApplicationName,
        string? lpCommandLine,
        IntPtr lpProcessAttributes,
        IntPtr lpThreadAttributes,
        bool bInheritHandles,
        uint dwCreationFlags,
        IntPtr lpEnvironment,
        string? lpCurrentDirectory,
        ref STARTUPINFOEX lpStartupInfo,
        out PROCESS_INFORMATION lpProcessInformation);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool InitializeProcThreadAttributeList(
        IntPtr lpAttributeList,
        int dwAttributeCount,
        int dwFlags,
        ref IntPtr lpSize);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool UpdateProcThreadAttribute(
        IntPtr lpAttributeList,
        int dwFlags,
        IntPtr Attribute,
        ref SECURITY_CAPABILITIES lpValue,
        IntPtr cbSize,
        IntPtr lpPreviousValue,
        IntPtr lpReturnSize);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool DeleteProcThreadAttributeList(IntPtr lpAttributeList);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool GetExitCodeProcess(IntPtr hProcess, out uint lpExitCode);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool CloseHandle(IntPtr hObject);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool ReadFile(
        IntPtr hFile,
        byte[] lpBuffer,
        uint nNumberOfBytesToRead,
        out uint lpNumberOfBytesRead,
        IntPtr lpOverlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool CreatePipe(
        out IntPtr hReadPipe,
        out IntPtr hWritePipe,
        ref SECURITY_ATTRIBUTES lpPipeAttributes,
        uint nSize);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool SetHandleInformation(
        IntPtr hObject,
        uint dwMask,
        uint dwFlags);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern uint WaitForSingleObject(
        IntPtr hHandle,
        uint dwMilliseconds);

    public const uint INFINITE = 0xFFFFFFFF;
    public const uint WAIT_OBJECT_0 = 0x00000000;
    public const uint WAIT_TIMEOUT = 0x00000102;
    public const uint WAIT_FAILED = 0xFFFFFFFF;

    public const uint HANDLE_FLAG_INHERIT = 0x00000001;

    #endregion

    #region advapi32.dll - ACL Functions

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern bool SetEntriesInAcl(
        int cCountOfExplicitEntries,
        EXPLICIT_ACCESS[] pListOfExplicitEntries,
        IntPtr OldAcl,
        out IntPtr NewAcl);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern bool SetNamedSecurityInfo(
        string pObjectName,
        int ObjectType,
        uint SecurityInfo,
        IntPtr psidOwner,
        IntPtr psidGroup,
        IntPtr pDacl,
        IntPtr psacl);

    [DllImport("advapi32.dll", SetLastError = true)]
    public static extern bool LocalFree(IntPtr hMem);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern bool ConvertStringSidToSid(
        string StringSid,
        out IntPtr pSid);

    #endregion

    #region Structures

    [StructLayout(LayoutKind.Sequential)]
    public struct SECURITY_ATTRIBUTES
    {
        public int nLength;
        public IntPtr lpSecurityDescriptor;
        public bool bInheritHandle;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct STARTUPINFOEX
    {
        public STARTUPINFO StartupInfo;
        public IntPtr lpAttributeList;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct STARTUPINFO
    {
        public int cb;
        public string? lpReserved;
        public string? lpDesktop;
        public string? lpTitle;
        public uint dwX;
        public uint dwY;
        public uint dwXSize;
        public uint dwYSize;
        public uint dwXCountChars;
        public uint dwYCountChars;
        public uint dwFillAttribute;
        public uint dwFlags;
        public ushort wShowWindow;
        public ushort cbReserved2;
        public IntPtr lpReserved2;
        public IntPtr hStdInput;
        public IntPtr hStdOutput;
        public IntPtr hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct PROCESS_INFORMATION
    {
        public IntPtr hProcess;
        public IntPtr hThread;
        public uint dwProcessId;
        public uint dwThreadId;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct SECURITY_CAPABILITIES
    {
        public IntPtr AppContainerSid;
        public IntPtr Capabilities;
        public uint CapabilityCount;
        public uint Reserved;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct EXPLICIT_ACCESS
    {
        public uint grfAccessPermissions;
        public int grfAccessMode;
        public uint grfInheritance;
        public TRUSTEE Trustee;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct TRUSTEE
    {
        public IntPtr pMultipleTrustee;
        public int MultipleTrusteeOperation;
        public int TrusteeForm;
        public int TrusteeType;
        public IntPtr ptstrName;
    }

    /// <summary>
    /// SID_AND_ATTRIBUTES structure for capabilities
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct SID_AND_ATTRIBUTES
    {
        public IntPtr Sid;
        public uint Attributes;
    }

    #endregion

    #region Helper Methods

    /// <summary>
    /// ProcThreadAttributeListのサイズを取得する
    /// </summary>
    public static IntPtr GetProcThreadAttributeListSize(int attributeCount)
    {
        IntPtr size = IntPtr.Zero;
        InitializeProcThreadAttributeList(IntPtr.Zero, attributeCount, 0, ref size);
        return size;
    }

    /// <summary>
    /// セキュアなパイプを作成する
    /// </summary>
    public static bool CreateSecurePipe(out IntPtr readHandle, out IntPtr writeHandle)
    {
        var sa = new SECURITY_ATTRIBUTES
        {
            nLength = Marshal.SizeOf<SECURITY_ATTRIBUTES>(),
            bInheritHandle = true,
            lpSecurityDescriptor = IntPtr.Zero
        };

        return CreatePipe(out readHandle, out writeHandle, ref sa, 0);
    }

    /// <summary>
    /// ケーパビリティ名からSID文字列を取得
    /// </summary>
    public static string? GetCapabilitySidString(AppContainerCapability capability)
    {
        return capability switch
        {
            AppContainerCapability.InternetClient => INTERNET_CLIENT_SID,
            AppContainerCapability.InternetClientServer => INTERNET_CLIENT_SERVER_SID,
            AppContainerCapability.PrivateNetworkClientServer => PRIVATE_NETWORK_CLIENT_SERVER_SID,
            AppContainerCapability.PicturesLibrary => PICTURES_LIBRARY_SID,
            AppContainerCapability.VideosLibrary => VIDEOS_LIBRARY_SID,
            AppContainerCapability.MusicLibrary => MUSIC_LIBRARY_SID,
            AppContainerCapability.DocumentsLibrary => DOCUMENTS_LIBRARY_SID,
            AppContainerCapability.EnterpriseAuthentication => ENTERPRISE_AUTHENTICATION_SID,
            AppContainerCapability.SharedUserCertificates => SHARED_USER_CERTIFICATES_SID,
            AppContainerCapability.RemovableStorage => REMOVABLE_STORAGE_SID,
            AppContainerCapability.Appointments => APPOINTMENTS_SID,
            AppContainerCapability.Contacts => CONTACTS_SID,
            _ => null
        };
    }

    /// <summary>
    /// ケーパビリティの配列からSID_AND_ATTRIBUTES配列を作成
    /// </summary>
    public static SID_AND_ATTRIBUTES[] CreateCapabilitySids(AppContainerCapability[] capabilities)
    {
        var sids = new List<SID_AND_ATTRIBUTES>();

        foreach (var capability in capabilities)
        {
            var sidString = GetCapabilitySidString(capability);
            if (sidString != null && ConvertStringSidToSid(sidString, out var sid) && sid != IntPtr.Zero)
            {
                sids.Add(new SID_AND_ATTRIBUTES
                {
                    Sid = sid,
                    Attributes = 0 // SE_GROUP_ENABLED
                });
            }
        }

        return sids.ToArray();
    }

    /// <summary>
    /// SID_AND_ATTRIBUTES配列のメモリを解放
    /// </summary>
    public static void FreeCapabilitySids(SID_AND_ATTRIBUTES[] sids)
    {
        foreach (var sid in sids)
        {
            if (sid.Sid != IntPtr.Zero)
            {
                FreeSid(sid.Sid);
            }
        }
    }

    #endregion
}

/// <summary>
/// AppContainerのケーパビリティ定義
/// </summary>
[Flags]
public enum AppContainerCapability
{
    None = 0,
    InternetClient = 1,
    InternetClientServer = 2,
    PrivateNetworkClientServer = 4,
    PicturesLibrary = 8,
    VideosLibrary = 16,
    MusicLibrary = 32,
    DocumentsLibrary = 64,
    EnterpriseAuthentication = 128,
    SharedUserCertificates = 256,
    RemovableStorage = 512,
    Appointments = 1024,
    Contacts = 2048
}
