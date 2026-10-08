using System.Buffers.Binary;
using System.Text;

namespace PadReset.Core;

public sealed class D4Link : IDisposable
{
    private readonly IByteTransport transport;
    private byte socket;
    private int txCredit, rxCredit, mtu = 512;
    private bool opened;
    private readonly Queue<byte[]> received = new();
    public D4Link(IByteTransport transport) => this.transport = transport;
    public static byte[] EncodePacket(byte psid, byte ssid, byte credit, byte control, byte[] payload)
    {
        if (payload.Length > ushort.MaxValue - 6) throw new ArgumentOutOfRangeException(nameof(payload));
        var packet = new byte[payload.Length + 6];
        packet[0] = psid; packet[1] = ssid;
        BinaryPrimitives.WriteUInt16BigEndian(packet.AsSpan(2), (ushort)packet.Length);
        packet[4] = credit; packet[5] = control;
        payload.CopyTo(packet, 6);
        return packet;
    }
    private sealed record Packet(byte Psid, byte Ssid, byte Credit, byte Control, byte[] Payload);
    private Packet Read(CancellationToken token)
    {
        var header = transport.ReadExact(6, token);
        int length = BinaryPrimitives.ReadUInt16BigEndian(header.AsSpan(2));
        if (length < 6 || length > 8192) throw new IOException("Некорректная длина пакета D4.");
        var packet = new Packet(header[0], header[1], header[4], header[5], transport.ReadExact(length - 6, token));
        if (opened && packet.Psid == socket && packet.Ssid == socket)
        {
            txCredit += packet.Credit;
            if (packet.Payload.Length > 0) { rxCredit--; received.Enqueue(packet.Payload); }
        }
        return packet;
    }
    private byte[] Control(byte command, byte[] payload, CancellationToken token)
    {
        transport.Write(EncodePacket(0, 0, 1, 0, [(byte)command, .. payload]), token);
        for (int i = 0; i < 32; i++)
        {
            var reply = Read(token);
            if (reply.Psid != 0 || reply.Ssid != 0) continue;
            if (reply.Payload.Length > 0 && reply.Payload[0] == 0x7F)
                throw new IOException("Принтер сообщил ошибку D4: " + Convert.ToHexString(reply.Payload));
            if (reply.Payload.Length < 2 || reply.Payload[0] != (command | 0x80))
                throw new IOException("Неожиданный ответ D4: " + Convert.ToHexString(reply.Payload));
            if (reply.Payload[1] != 0) throw new IOException($"D4-команда 0x{command:X2} отклонена: 0x{reply.Payload[1]:X2}.");
            return reply.Payload[2..];
        }
        throw new IOException("Не получено подтверждение D4.");
    }
    public void Open(CancellationToken token)
    {
        transport.Write([0, 0, 0, 0x1B, 1, .. Encoding.ASCII.GetBytes("@EJL 1284.4\n@EJL\n@EJL\n")], token);
        // Mode-switch acknowledgment itself is an eight-byte D4 packet.
        var acknowledgment = Read(token);
        if (acknowledgment.Psid != 0 || acknowledgment.Ssid != 0)
            throw new IOException("Принтер не подтвердил вход в D4.");
        var revision = Control(0, [0x10], token);
        if (revision.Length != 1 || revision[0] != 0x10) throw new IOException("Принтер не поддержал D4 версии 0x10.");
        var socketReply = Control(9, Encoding.ASCII.GetBytes("EPSON-CTRL"), token);
        if (socketReply.Length < 1 || socketReply[0] == 0) throw new IOException("Канал EPSON-CTRL недоступен.");
        socket = socketReply[0];
        var channel = Control(1, [socket, socket, 0xFF, 0xFF, 0xFF, 0xFF, 0, 0, 0, 0], token);
        if (channel.Length is not (8 or 10) || channel[0] != socket || channel[1] != socket)
            throw new IOException("Принтер вернул неизвестный формат открытия D4-канала.");
        mtu = BinaryPrimitives.ReadUInt16BigEndian(channel.AsSpan(2));
        if (mtu < 32) throw new IOException("Слишком малый размер D4-пакета.");
        txCredit = BinaryPrimitives.ReadUInt16BigEndian(channel.AsSpan(channel.Length - 2));
        opened = true;
        Control(3, [socket, socket, 0, 1], token);
        rxCredit = 1;
    }
    public byte[] Exchange(byte[] command, CancellationToken token)
    {
        if (!opened) throw new InvalidOperationException("Сервисный канал не открыт.");
        // EEPROM frames are small; explicitly refuse partial-message semantics not used here.
        if (command.Length > mtu - 6) throw new IOException("Команда превышает размер D4-пакета.");
        if (received.Count > 0) throw new IOException("Неожиданные данные в сервисном канале. Переподключите принтер.");
        for (int attempt = 0; txCredit == 0 && attempt < 12; attempt++)
        {
            var reply = Control(4, [socket, socket, 0, 0x80, 0xFF, 0xFF], token);
            if (reply.Length != 4 || reply[0] != socket || reply[1] != socket) throw new IOException("Некорректный ответ на запрос D4 credit.");
            txCredit += BinaryPrimitives.ReadUInt16BigEndian(reply.AsSpan(2));
            if (txCredit == 0) Thread.Sleep(30);
        }
        if (txCredit <= 0) throw new IOException("Принтер не разрешил отправку сервисной команды.");
        byte grant = (byte)Math.Max(0, 1 - rxCredit);
        transport.Write(EncodePacket(socket, socket, grant, 0, command), token);
        rxCredit += grant; txCredit--;
        for (int i = 0; i < 32; i++)
        {
            if (received.Count > 0) return received.Dequeue();
            var packet = Read(token);
            if (packet.Psid == 0 && packet.Payload.Length > 0)
                throw new IOException("Ошибка сервисного обмена D4: " + Convert.ToHexString(packet.Payload));
        }
        throw new IOException("Принтер не ответил на сервисную команду.");
    }
    public void Dispose()
    {
        if (!opened) return;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(1));
        try { Control(2, [socket, socket, 0], timeout.Token); Control(8, [], timeout.Token); }
        catch { /* USB removal and shutdown must still release the handle. */ }
        opened = false;
    }
}
