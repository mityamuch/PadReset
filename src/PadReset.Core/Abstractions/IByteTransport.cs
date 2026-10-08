namespace PadReset.Core;

public interface IByteTransport : IDisposable
{
    void Write(byte[] data, CancellationToken token);
    byte[] ReadExact(int count, CancellationToken token);
}
