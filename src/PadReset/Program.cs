using PadReset.Core;
using PadReset.UI;
using PadReset.Windows;

namespace PadReset;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Contains("--check-queues"))
        {
            try { PrintQueue.RequireIdle(CancellationToken.None, Console.WriteLine); Console.WriteLine("Print queues are idle."); return 0; }
            catch (System.ComponentModel.Win32Exception ex) { Console.Error.WriteLine($"Win32 {ex.NativeErrorCode}: {ex.Message}"); return 1; }
            catch (Exception ex) { Console.Error.WriteLine(ex.Message); return 1; }
        }
        if (args.Contains("--list-usb"))
        {
            try { foreach (var device in UsbTransport.Enumerate()) Console.WriteLine(device.Name + "\n" + device.Path); return 0; }
            catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        }
        ApplicationConfiguration.Initialize();
        try
        {
            var form = new MainForm(new ProfileDatabase(), args.Contains("--render-preview"));
            if (args.Contains("--render-preview"))
            {
                int index = Array.IndexOf(args, "--render-preview");
                string path = index + 1 < args.Length ? args[index + 1] : "ui-preview.png";
                form.ShowInTaskbar = false; form.Opacity = 0;
                form.Shown += (_, _) => form.BeginInvoke(() =>
                {
                    using var bitmap = new Bitmap(form.ClientSize.Width, form.ClientSize.Height);
                    form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.ClientSize));
                    bitmap.Save(Path.GetFullPath(path), System.Drawing.Imaging.ImageFormat.Png);
                    form.Close();
                });
            }
            Application.Run(form);
            return 0;
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Ошибка запуска PadReset", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }
    }
}
