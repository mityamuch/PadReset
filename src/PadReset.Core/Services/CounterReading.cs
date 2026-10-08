namespace PadReset.Core;

public sealed record CounterReading(int Number, long Value, long Maximum, Dictionary<int, byte> Raw, bool HasExtra)
{
    public double Percent => Value * 100.0 / Maximum;
}
public sealed record ResetResult(string BackupPath, CounterReading[] Counters, int Written);
