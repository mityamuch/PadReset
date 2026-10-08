namespace PadReset.Windows;

public sealed record UsbDevice(string Path, string Name) { public override string ToString() => Name; }
