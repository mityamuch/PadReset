using System.Text;
using System.Text.RegularExpressions;

namespace PadReset.Core;

public sealed class EpsonEeprom(D4Link link, ModelProfile profile) : IEeprom
{
    public static byte[] EncodeFactory(ModelProfile profile, byte action, int address, byte? value = null)
    {
        int addressLength = value.HasValue ? profile.WriteLength : profile.ReadLength;
        if (addressLength is not (1 or 2) || address < 0 || address >= 1 << (addressLength * 8)) throw new ArgumentOutOfRangeException(nameof(address));
        var payload = new List<byte>(profile.Factory);
        payload.Add(action); payload.Add((byte)(action ^ 0xFF));
        payload.Add((byte)((action >> 1) | ((action & 1) << 7)));
        payload.Add((byte)(address & 0xFF));
        if (addressLength == 2) payload.Add((byte)(address >> 8));
        if (value.HasValue) { payload.Add(value.Value); payload.AddRange(profile.Keyword); }
        return [(byte)'|', (byte)'|', (byte)payload.Count, (byte)(payload.Count >> 8), .. payload];
    }
    public static byte ParseReadReply(byte[] response, int address)
    {
        var text = Encoding.ASCII.GetString(response);
        var match = Regex.Match(text, @"^@BDC PS(?:2)?\r\n.*?\bEE:([0-9A-Fa-f]{4}|[0-9A-Fa-f]{6});", RegexOptions.Singleline);
        if (!match.Success) throw new IOException("Принтер не подтвердил чтение EEPROM: " + text);
        var bytes = Convert.FromHexString(match.Groups[1].Value);
        int replyAddress = bytes.Length == 3 ? (bytes[0] << 8) | bytes[1] : bytes[0];
        if (replyAddress != address) throw new IOException($"Ответ EEPROM для другого адреса: 0x{replyAddress:X}, ожидался 0x{address:X}.");
        return bytes[^1];
    }
    public byte ReadByte(int address, CancellationToken token) => ParseReadReply(link.Exchange(EncodeFactory(profile, 0x41, address), token), address);
    public static void ValidateWriteReply(byte[] response, int address)
    {
        var reply = Encoding.ASCII.GetString(response);
        // Epson also returns the CTRL action directly, without a BDC header.
        // The observed L3150 reply is "||:42:OK;\x0C"; 42 must match the write action.
        bool direct = Regex.IsMatch(reply, @"\A\|\|:42:OK;\x0C?(?:\r\n)?\z");
        bool framed = Regex.IsMatch(reply, @"\A@BDC PS(?:2)?\r\n(?:\|\|:42:OK;|WR:OK;)\x0C?(?:\r\n)?\z");
        if (!direct && !framed)
        {
            var visible = string.Concat(reply.Select(c => char.IsControl(c) ? $"\\x{(int)c:X2}" : c.ToString()));
            throw new IOException($"Запись 0x{address:X} не подтверждена: {visible}\nОтвет HEX: {Convert.ToHexString(response)}");
        }
    }
    public void WriteByte(int address, byte value, CancellationToken token)
    {
        ValidateWriteReply(link.Exchange(EncodeFactory(profile, 0x42, address, value), token), address);
    }
}
