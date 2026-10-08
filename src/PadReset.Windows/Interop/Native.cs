using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace PadReset.Windows;

internal static class Native
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct InterfaceData
    {
        public int Size; public Guid ClassGuid; public int Flags; public IntPtr Reserved;
    }
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)] internal static extern IntPtr SetupDiGetClassDevsW(ref Guid guid, string? enumerator, IntPtr hwnd, uint flags);
    [DllImport("setupapi.dll", SetLastError = true)] internal static extern bool SetupDiEnumDeviceInterfaces(IntPtr set, IntPtr info, ref Guid guid, uint index, ref InterfaceData data);
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)] internal static extern bool SetupDiGetDeviceInterfaceDetailW(IntPtr set, ref InterfaceData data, IntPtr detail, int length, out int required, IntPtr info);
    [DllImport("setupapi.dll")] internal static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] internal static extern SafeFileHandle CreateFileW(string path, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);
    [DllImport("kernel32.dll", SetLastError = true)] internal static extern bool ReadFile(SafeFileHandle file, IntPtr buffer, uint length, out uint read, IntPtr overlapped);
    [DllImport("kernel32.dll", SetLastError = true)] internal static extern bool WriteFile(SafeFileHandle file, IntPtr buffer, uint length, out uint written, IntPtr overlapped);
    [DllImport("kernel32.dll", SetLastError = true)] internal static extern bool DeviceIoControl(SafeFileHandle file, uint code, IntPtr input, uint inputLength, IntPtr output, uint outputLength, out uint returned, IntPtr overlapped);
    [DllImport("kernel32.dll", SetLastError = true)] internal static extern bool GetOverlappedResult(SafeFileHandle file, IntPtr overlapped, out uint count, bool wait);
    [DllImport("kernel32.dll", SetLastError = true)] internal static extern bool CancelIoEx(SafeFileHandle file, IntPtr overlapped);
}
