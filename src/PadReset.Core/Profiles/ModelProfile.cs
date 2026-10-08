namespace PadReset.Core;

public sealed record CounterProfile(int[] BaseAddresses, int[] ExtraAddresses, long Maximum);
public sealed record EepromWrite(int Address, byte Value);
public sealed record CloseOperation(int ReadAddress, byte Mask, int WriteAddress);
public sealed record ModelProfile(string Model, string Title, byte[] Factory, byte[] Keyword,
    int ReadLength, int WriteLength, CounterProfile[] Counters, EepromWrite[] Reset,
    int[] SerialAddresses, CloseOperation? Close, string? UnsupportedReason)
{
    public bool CanRead => UnsupportedReason == null && Counters.Length > 0;
    public bool CanReset => CanRead && Reset.Length > 0;
    public override string ToString() => Title;
}
