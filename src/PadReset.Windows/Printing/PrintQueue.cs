using System.ComponentModel;
using System.Drawing.Printing;
using System.Runtime.InteropServices;

namespace PadReset.Windows;

public sealed class PrintQueueCheckException(string message, Exception? inner = null) : IOException(message, inner);

public static class PrintQueue
{
    internal delegate bool PrinterInfoReader(IntPtr buffer, uint size, out uint needed, out int error);
    [DllImport("winspool.drv", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool OpenPrinterW(string name, out IntPtr handle, IntPtr defaults);
    [DllImport("winspool.drv", SetLastError = true)] private static extern bool EnumJobsW(IntPtr handle, uint first, uint count, uint level, IntPtr buffer, uint size, out uint needed, out uint returned);
    [DllImport("winspool.drv", SetLastError = true)] private static extern bool GetPrinterW(IntPtr handle, uint level, IntPtr buffer, uint size, out uint needed);
    [DllImport("winspool.drv")] private static extern bool ClosePrinter(IntPtr handle);
    private static Win32Exception NativeError(int error, string context) => new(error,
        $"{context}\nWindows {error}: {new Win32Exception(error).Message}");

    internal static uint ReadStatus(PrinterInfoReader query, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        bool ok = query(IntPtr.Zero, 0, out uint needed, out int error);
        if (!ok && error != 122) throw NativeError(error, "Не удалось определить размер статуса очереди.");
        for (int attempt = 0; attempt < 3; attempt++)
        {
            if (needed < 4 || needed > 1024 * 1024) throw new IOException("Windows вернула некорректный размер статуса очереди.");
            token.ThrowIfCancellationRequested();
            uint allocated = needed;
            var memory = Marshal.AllocHGlobal(checked((int)allocated));
            try
            {
                if (query(memory, allocated, out needed, out error))
                {
                    if (needed < 4 || needed > allocated) throw new IOException("Windows вернула неполный статус очереди.");
                    return unchecked((uint)Marshal.ReadInt32(memory));
                }
                // The spooler may require more space than sizeof(PRINTER_INFO_6),
                // or increase the required size between the two calls.
                if (error != 122 || needed <= allocated) throw NativeError(error, "Не удалось прочитать статус очереди.");
            }
            finally { Marshal.FreeHGlobal(memory); }
        }
        throw new IOException("Размер статуса очереди постоянно меняется. Повторите проверку.");
    }

    internal static void CheckStatus(uint status, string name)
    {
        if ((status & (0x200 | 0x400 | 0x100)) != 0)
            throw new IOException("Очередь занята печатью: " + name);
    }

    public static void RequireIdle(CancellationToken token, Action<string>? log = null)
    {
        try { CheckQueues(token, log); }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) when (ex is Win32Exception or IOException)
        { throw new PrintQueueCheckException(ex.Message, ex); }
    }

    private static void CheckQueues(CancellationToken token, Action<string>? log)
    {
        // Conservatively check every local spooler queue. Never cancel jobs or restart the spooler.
        foreach (string name in PrinterSettings.InstalledPrinters)
        {
            token.ThrowIfCancellationRequested();
            if (!OpenPrinterW(name, out var handle, IntPtr.Zero)) throw NativeError(Marshal.GetLastWin32Error(), "Не удалось проверить очередь: " + name);
            try
            {
                bool ok = EnumJobsW(handle, 0, 1, 1, IntPtr.Zero, 0, out uint needed, out uint returned);
                int error = Marshal.GetLastWin32Error();
                if (needed > 0 || returned > 0) throw new IOException("Перед сбросом дождитесь завершения печати. В очереди есть задания: " + name);
                if (!ok) throw NativeError(error, "Не удалось проверить очередь: " + name);
                bool Query(IntPtr buffer, uint size, out uint required, out int lastError)
                {
                    bool success = GetPrinterW(handle, 6, buffer, size, out required);
                    lastError = success ? 0 : Marshal.GetLastWin32Error();
                    return success;
                }
                uint status;
                try { status = ReadStatus(Query, token); }
                catch (Exception ex) when (ex is Win32Exception or IOException)
                { throw new IOException("Не удалось прочитать статус очереди: " + name + "\n" + ex.Message, ex); }
                log?.Invoke($"Очередь «{name}»: заданий нет, статус 0x{status:X8}.");
                CheckStatus(status, name);
            }
            finally { ClosePrinter(handle); }
        }
    }
}
