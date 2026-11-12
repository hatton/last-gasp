using System.ComponentModel;
using System.Runtime.InteropServices;

namespace LastGasp;

/// <summary>
/// Does what RAMMap's -Ew, -Es and -Em switches do. Every step needs admin rights, so the
/// taskbar app runs this in a second copy of itself (LastGasp.exe --free) started as administrator.
/// </summary>
internal static class FreeMemory
{
    private const int SystemMemoryListInformation = 80;
    private const int MemoryEmptyWorkingSets = 2;
    private const int MemoryFlushModifiedList = 3;

    /// <returns>0 on success, otherwise the Win32 or NTSTATUS error of the first failed step.</returns>
    public static int Run()
    {
        try
        {
            EnablePrivilege("SeProfileSingleProcessPrivilege");
            EnablePrivilege("SeIncreaseQuotaPrivilege");

            // RAMMap -Ew: empty the working sets of all processes.
            SetMemoryList(MemoryEmptyWorkingSets);

            // RAMMap -Es: empty the system working set. Passing -1 for both sizes flushes it.
            if (!SetSystemFileCacheSize(new IntPtr(-1), new IntPtr(-1), 0))
                throw new Win32Exception(Marshal.GetLastWin32Error());

            // RAMMap -Em: write the modified page list to disk so its pages become free.
            SetMemoryList(MemoryFlushModifiedList);
            return 0;
        }
        catch (Win32Exception ex)
        {
            return ex.NativeErrorCode;
        }
    }

    private static void SetMemoryList(int command)
    {
        int status = NtSetSystemInformation(SystemMemoryListInformation, ref command, sizeof(int));
        if (status != 0)
            throw new Win32Exception(RtlNtStatusToDosError(status));
    }

    private static void EnablePrivilege(string name)
    {
        const uint TOKEN_ADJUST_PRIVILEGES = 0x20;
        const uint TOKEN_QUERY = 0x8;
        const uint SE_PRIVILEGE_ENABLED = 0x2;

        if (!OpenProcessToken(GetCurrentProcess(), TOKEN_ADJUST_PRIVILEGES | TOKEN_QUERY, out var token))
            throw new Win32Exception(Marshal.GetLastWin32Error());
        try
        {
            if (!LookupPrivilegeValue(null, name, out var luid))
                throw new Win32Exception(Marshal.GetLastWin32Error());
            var privileges = new TokenPrivileges { PrivilegeCount = 1, Luid = luid, Attributes = SE_PRIVILEGE_ENABLED };
            if (!AdjustTokenPrivileges(token, false, ref privileges, 0, IntPtr.Zero, IntPtr.Zero))
                throw new Win32Exception(Marshal.GetLastWin32Error());
            // AdjustTokenPrivileges succeeds without enabling a privilege the token lacks.
            int error = Marshal.GetLastWin32Error();
            if (error != 0)
                throw new Win32Exception(error);
        }
        finally
        {
            CloseHandle(token);
        }
    }

    // Windows aligns the LUID to 4 bytes, not 8.
    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct TokenPrivileges
    {
        public uint PrivilegeCount;
        public long Luid;
        public uint Attributes;
    }

    [DllImport("ntdll.dll")]
    private static extern int NtSetSystemInformation(int infoClass, ref int info, int length);

    [DllImport("ntdll.dll")]
    private static extern int RtlNtStatusToDosError(int status);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetSystemFileCacheSize(IntPtr minimumFileCacheSize, IntPtr maximumFileCacheSize, int flags);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool LookupPrivilegeValue(string? systemName, string name, out long luid);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AdjustTokenPrivileges(IntPtr token, bool disableAll, ref TokenPrivileges newState,
        int bufferLength, IntPtr previousState, IntPtr returnLength);
}
