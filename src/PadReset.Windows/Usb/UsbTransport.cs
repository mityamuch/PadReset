using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;
using PadReset.Core;

namespace PadReset.Windows;

public sealed class UsbTransport : IByteTransport
{
    private readonly SafeFileHandle handle;
    private readonly Queue<byte> buffer = new();
    public static IReadOnlyList<UsbDevice> Enumerate()
    {
        var guid = new Guid("28d78fad-5a12-11d1-ae5b-0000f803a8c2");
        var set = Native.SetupDiGetClassDevsW(ref guid, null, IntPtr.Zero, 0x12);
        if (set == new IntPtr(-1)) throw new Win32Exception();
        var devices = new List<UsbDevice>();
        try
        {
            for (uint index = 0; ; index++)
            {
                var data = new Native.InterfaceData { Size = Marshal.SizeOf<Native.InterfaceData>() };
                if (!Native.SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref guid, index, ref data))
                {
                    if (Marshal.GetLastWin32Error() == 259) break;
                    throw new Win32Exception();
                }
                Native.SetupDiGetDeviceInterfaceDetailW(set, ref data, IntPtr.Zero, 0, out int size, IntPtr.Zero);
                if (size < 6) throw new Win32Exception();
                var detail = Marshal.AllocHGlobal(size);
                try
                {
                    Marshal.WriteInt32(detail, IntPtr.Size == 8 ? 8 : 6);
                    if (!Native.SetupDiGetDeviceInterfaceDetailW(set, ref data, detail, size, out _, IntPtr.Zero)) throw new Win32Exception();
                    var path = Marshal.PtrToStringUni(detail + 4)!;
                    if (!path.Contains("epson", StringComparison.OrdinalIgnoreCase) && !path.Contains("vid_04b8", StringComparison.OrdinalIgnoreCase)) continue;
                    var pieces = path.Split('#');
                    var name = pieces.Length > 2 ? pieces[1].Replace('_', ' ') + " · " + pieces[2] : path;
                    devices.Add(new(path, name));
                }
                finally { Marshal.FreeHGlobal(detail); }
            }
        }
        finally { Native.SetupDiDestroyDeviceInfoList(set); }
        return devices.DistinctBy(d => d.Path).ToArray();
    }
    public UsbTransport(string path)
    {
        handle = Native.CreateFileW(path, 0xC0000000, 3, IntPtr.Zero, 3, 0xC0000080, IntPtr.Zero);
        if (handle.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error(), "Не удалось открыть принтер. Закройте Epson Status Monitor и проверьте USB-драйвер.");
    }
    // All native buffers remain alive until cancellation has completed in the driver.
    private byte[] Io(byte[]? input, int outputLength, uint? ioctl, CancellationToken token, int timeoutMs = 8000)
    {
        token.ThrowIfCancellationRequested();
        using var ev = new EventWaitHandle(false, EventResetMode.ManualReset);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(timeoutMs);
        int length = input?.Length ?? outputLength;
        var memory = Marshal.AllocHGlobal(Math.Max(1, length));
        var overlapped = Marshal.AllocHGlobal(IntPtr.Size * 3 + 8);
        bool pending = false;
        try
        {
            for (int i = 0; i < IntPtr.Size * 3 + 8; i++) Marshal.WriteByte(overlapped, i, 0);
            Marshal.WriteIntPtr(overlapped, IntPtr.Size * 2 + 8, ev.SafeWaitHandle.DangerousGetHandle());
            if (input != null) Marshal.Copy(input, 0, memory, input.Length);
            bool ok; uint transferred;
            if (ioctl != null) ok = Native.DeviceIoControl(handle, ioctl.Value, IntPtr.Zero, 0, memory, (uint)outputLength, out transferred, overlapped);
            else if (input != null) ok = Native.WriteFile(handle, memory, (uint)length, out transferred, overlapped);
            else ok = Native.ReadFile(handle, memory, (uint)length, out transferred, overlapped);
            if (!ok)
            {
                int error = Marshal.GetLastWin32Error();
                if (error != 997) throw new Win32Exception(error);
                pending = true;
                int signaled = WaitHandle.WaitAny([ev, deadline.Token.WaitHandle]);
                if (signaled == 1)
                {
                    Native.CancelIoEx(handle, overlapped);
                    Native.GetOverlappedResult(handle, overlapped, out _, true);
                    pending = false;
                    token.ThrowIfCancellationRequested();
                    throw new TimeoutException("Принтер не ответил за 8 секунд. Переподключите USB и проверьте драйвер Epson.");
                }
                if (!Native.GetOverlappedResult(handle, overlapped, out transferred, false))
                { pending = false; throw new Win32Exception(); }
                pending = false;
            }
            if (input != null)
            {
                if (transferred != input.Length) throw new IOException("Принтер принял команду не полностью.");
                return [];
            }
            var result = new byte[transferred];
            Marshal.Copy(memory, result, 0, result.Length);
            return result;
        }
        finally
        {
            if (pending) { Native.CancelIoEx(handle, overlapped); Native.GetOverlappedResult(handle, overlapped, out _, true); }
            Marshal.FreeHGlobal(overlapped); Marshal.FreeHGlobal(memory);
        }
    }
    public string Identify(CancellationToken token)
    {
        var data = Io(null, 4096, 0x220034, token);
        if (data.Length < 3) throw new IOException("Принтер не вернул идентификатор IEEE 1284.");
        return Encoding.ASCII.GetString(data, 2, data.Length - 2).Trim('\0', '\r', '\n');
    }
    public void Write(byte[] data, CancellationToken token) => Io(data, 0, null, token);
    public byte[] ReadExact(int count, CancellationToken token)
    {
        var limit = DateTime.UtcNow.AddSeconds(10);
        while (buffer.Count < count)
        {
            token.ThrowIfCancellationRequested();
            if (DateTime.UtcNow >= limit) throw new TimeoutException("Неполный ответ USB.");
            var chunk = Io(null, 65536, null, token);
            foreach (byte b in chunk) buffer.Enqueue(b);
            if (chunk.Length == 0) Thread.Sleep(10);
        }
        return Enumerable.Range(0, count).Select(_ => buffer.Dequeue()).ToArray();
    }
    public void Dispose() => handle.Dispose();
}
