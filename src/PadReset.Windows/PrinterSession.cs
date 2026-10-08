using PadReset.Core;

namespace PadReset.Windows;

/// <summary>Owns the device connection and coordinates printer operations.</summary>
public sealed class PrinterSession : IDisposable
{
    private readonly UsbTransport? usb;
    private readonly D4Link? link;
    private readonly ResetService service;
    private readonly Action<string>? log;
    private bool disposed;

    public ModelProfile Profile { get; }
    public string Serial { get; }
    public bool IsSimulation { get; }

    private PrinterSession(ModelProfile profile, string serial, ResetService service,
        bool simulated, Action<string>? log, UsbTransport? usb = null, D4Link? link = null)
    {
        Profile = profile;
        Serial = serial;
        this.service = service;
        IsSimulation = simulated;
        this.log = log;
        this.usb = usb;
        this.link = link;
    }

    public static PrinterSession Open(ProfileDatabase database, UsbDevice device,
        Action<string>? log, CancellationToken token)
    {
        var usb = new UsbTransport(device.Path);
        D4Link? link = null;
        try
        {
            var identity = usb.Identify(token);
            var profile = ResolveProfile(database, identity);
            log?.Invoke("Идентификатор: " + identity);
            link = new D4Link(usb);
            link.Open(token);
            var service = new ResetService(profile, new EpsonEeprom(link, profile), log);
            string serial = service.ReadSerial(token);
            return new(profile, serial, service, false, log, usb, link);
        }
        catch
        {
            link?.Dispose();
            usb.Dispose();
            throw;
        }
    }

    internal static ModelProfile ResolveProfile(ProfileDatabase database, string identity)
    {
        var fields = identity.Split(';').Select(s => s.Split(':', 2)).Where(p => p.Length == 2)
            .GroupBy(p => p[0].Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Last()[1].Trim(), StringComparer.OrdinalIgnoreCase);
        if (!fields.TryGetValue("MFG", out var maker) || !maker.Equals("EPSON", StringComparison.OrdinalIgnoreCase))
            throw new IOException("Устройство не подтвердило производителя Epson.");
        string model = fields.GetValueOrDefault("MDL") ?? throw new IOException("Принтер не сообщил модель.");
        var profile = database.Find(model) ?? throw new IOException("Нет профиля модели: " + model);
        if (!profile.CanRead) throw new IOException(profile.UnsupportedReason ?? "Нет данных счётчиков.");
        return profile;
    }

    public static PrinterSession Simulate(ProfileDatabase database, Action<string>? log)
    {
        var profile = database.Find("L3250 Series") ?? throw new InvalidDataException("Нет профиля симулятора.");
        return new(profile, "SIMULATOR1", new ResetService(profile, new SimulatedEeprom(profile), log), true, log);
    }

    public CounterReading[] ReadCounters(CancellationToken token)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        return service.ReadCounters(token);
    }

    public ResetResult Reset(string dataDirectory, CancellationToken token)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (!IsSimulation) PrintQueue.RequireIdle(token, log);
        string directory = Path.Combine(dataDirectory, IsSimulation ? "simulation-backups" : "backups");
        return service.Reset(directory, Serial, token);
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        try { link?.Dispose(); }
        finally { usb?.Dispose(); }
    }
}
