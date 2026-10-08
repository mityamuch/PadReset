using System.Text;

namespace PadReset.Core;

public sealed class SimulatedEeprom : IEeprom
{
    private readonly Dictionary<int, byte> values = new();
    public List<EepromWrite> Writes { get; } = [];
    public int? FailAddress { get; set; }
    public SimulatedEeprom(ModelProfile profile)
    {
        foreach (var counter in profile.Counters)
        {
            long level = (long)(counter.Maximum * 0.96);
            for (int i = 0; i < counter.BaseAddresses.Length; i++) values[counter.BaseAddresses[i]] = (byte)((level >> (i * 8)) & 255);
            foreach (var a in counter.ExtraAddresses) values[a] = 0;
        }
        var serial = Encoding.ASCII.GetBytes("SIMULATOR1");
        for (int i = 0; i < profile.SerialAddresses.Length; i++) values[profile.SerialAddresses[i]] = serial[i % serial.Length];
        if (profile.Close is { } close) values[close.ReadAddress] = 0xA5;
    }
    public byte ReadByte(int address, CancellationToken token) { token.ThrowIfCancellationRequested(); return values.GetValueOrDefault(address); }
    public void WriteByte(int address, byte value, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (address == FailAddress) throw new IOException("Имитированное отключение USB.");
        Writes.Add(new(address, value)); values[address] = value;
    }
}
