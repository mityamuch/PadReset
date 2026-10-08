namespace PadReset.Core;

public interface IEeprom
{
    byte ReadByte(int address, CancellationToken token);
    void WriteByte(int address, byte value, CancellationToken token);
}
