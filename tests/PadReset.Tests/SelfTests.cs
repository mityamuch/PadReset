using System.Text;
using System.Text.Json;
using System.Runtime.InteropServices;

using PadReset.Core;
using PadReset.Windows;

namespace PadReset.Tests;

internal static class SelfTests
{
    private static int count;
    private static void Check(bool condition, string name)
    {
        if (!condition) throw new Exception("FAIL: " + name);
        Console.WriteLine("PASS: " + name); count++;
    }
    private static void Reject(Action action, string name)
    {
        try { action(); } catch { Check(true, name); return; }
        throw new Exception("FAIL: " + name);
    }
    public static int Run()
    {
        string temp = Path.Combine(Path.GetTempPath(), "PadReset-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try
        {
            var db = new ProfileDatabase();
            Check(db.Models.Count == 1080, "All model records parsed");
            var profile = db.Find("L3250 Series")!;
            Check(profile.CanReset && profile.Counters.Length == 3 && profile.Reset.Length == 15, "L3250 profile loaded with auxiliary writes");
            Check(profile.Close == new CloseOperation(0x100, 0xFE, 0x100), "Close block parsed");
            Check(db.Find("L3250") == profile && db.Find("Imaginary L3250 plus") == null, "Exact model matching, suffix normalization only");
            Check(PrinterSession.ResolveProfile(db, "MFG:EPSON;MDL:L3150 Series;").Model == "L3150 Series", "Session resolves detected model without UI");
            Reject(() => PrinterSession.ResolveProfile(db, "MFG:NOTEPSON;MDL:L3150 Series;"), "Session rejects wrong manufacturer");
            Reject(() => PrinterSession.ResolveProfile(db, "MFG:EPSON;"), "Session requires detected model");
            Reject(() => PrinterSession.ResolveProfile(db, "MFG:EPSON;MDL:Unknown;"), "Session rejects unknown model");
            var simulatedSession = PrinterSession.Simulate(db, null);
            Check(simulatedSession.IsSimulation && simulatedSession.ReadCounters(CancellationToken.None).Length == 3, "Simulation session coordinates reads without USB");
            simulatedSession.Dispose();
            Reject(() => simulatedSession.ReadCounters(CancellationToken.None), "Disposed session cannot read");
            Check(db.Find("L805 Series")?.CanReset == true && db.Find("L120 Series")?.CanReset == true && db.Find("L800")?.CanReset == true, "L805, L120 and L800 profiles supported");
            Check(db.Models.Any(p => p.CanRead && p.Counters.Any(c => c.ExtraAddresses.Length == 0)), "Legacy inline counter addresses parsed");
            Check(profile.Counters[0].BaseAddresses.SequenceEqual(new[] { 0x30, 0x31 }) && profile.Counters[0].ExtraAddresses.SequenceEqual(new[] { 0x2F }), "Filtered registers preserved separately");
            byte[] expectedRead = [0x7C, 0x7C, 7, 0, 0x4A, 0x36, 0x41, 0xBE, 0xA0, 0x30, 0];
            Check(EpsonEeprom.EncodeFactory(profile, 0x41, 0x30).SequenceEqual(expectedRead), "Golden EEPROM read frame");
            var encodedWrite = EpsonEeprom.EncodeFactory(profile, 0x42, 0x100, 0xA4);
            Check(encodedWrite.Take(12).SequenceEqual(new byte[] { 0x7C, 0x7C, 16, 0, 0x4A, 0x36, 0x42, 0xBD, 0x21, 0, 1, 0xA4 }) && encodedWrite[12..].SequenceEqual(profile.Keyword), "Golden EEPROM write frame and keyword");
            Check(EpsonEeprom.ParseReadReply(Encoding.ASCII.GetBytes("@BDC PS\r\nEE:00307F;"), 0x30) == 0x7F, "Read response address and value");
            Reject(() => EpsonEeprom.ParseReadReply(Encoding.ASCII.GetBytes("@BDC PS\r\nEE:00317F;"), 0x30), "Wrong-address response rejected");
            Reject(() => EpsonEeprom.ParseReadReply(Encoding.ASCII.GetBytes("@BDC PS\r\nEE:NA;"), 0x30), "Rejected read cannot become zero");
            EpsonEeprom.ValidateWriteReply(Encoding.ASCII.GetBytes("||:42:OK;\x0C"), 0x30);
            Check(true, "L3150 direct write acknowledgment with form-feed accepted");
            EpsonEeprom.ValidateWriteReply(Encoding.ASCII.GetBytes("||:42:OK;"), 0x30);
            Check(true, "Direct write acknowledgment without terminator accepted");
            EpsonEeprom.ValidateWriteReply(Encoding.ASCII.GetBytes("@BDC PS\r\nWR:OK;"), 0x30);
            Check(true, "Legacy BDC write acknowledgment accepted");
            EpsonEeprom.ValidateWriteReply(Encoding.ASCII.GetBytes("@BDC PS2\r\n||:42:OK;\x0C\r\n"), 0x30);
            Check(true, "BDC-wrapped action acknowledgment accepted");
            foreach (string invalid in new[] { "||:42:NA;\x0C", "||:41:OK;\x0C", "||:42:OK", "||:42:OK;junk", "garbage:OK;", "||:42:OK;\x0C||:42:NA;", "" })
                Reject(() => EpsonEeprom.ValidateWriteReply(Encoding.ASCII.GetBytes(invalid), 0x30), "Invalid write acknowledgment rejected: " + Convert.ToHexString(Encoding.ASCII.GetBytes(invalid)));
            Check(D4Link.EncodePacket(2, 2, 1, 0, [1, 2]).SequenceEqual(new byte[] { 2, 2, 0, 8, 1, 0, 1, 2 }), "Golden D4 packet encoding");
            var fake = new ScriptedTransport();
            fake.AddPacket(0, [0xC5, 0]);
            fake.AddPacket(0, [0x80, 0, 0x10]);
            fake.AddPacket(0, [0x89, 0, 2]);
            fake.AddPacket(0, [0x81, 0, 2, 2, 2, 0, 2, 0, 0, 0, 0, 1]);
            fake.AddPacket(0, [0x83, 0, 2, 2]);
            var reply = Encoding.ASCII.GetBytes("@BDC PS\r\nEE:003000;");
            fake.AddPacket(2, reply, 1);
            var d4 = new D4Link(fake); d4.Open(CancellationToken.None);
            Check(d4.Exchange(expectedRead, CancellationToken.None).SequenceEqual(reply), "D4 handshake and service exchange");
            Check(fake.Writes[3][6] == 1 && fake.Writes[3].Length == 17 && fake.Writes[5][0] == 2, "OpenChannel request and data channel routing");
            fake.AddPacket(2, Encoding.ASCII.GetBytes("||:42:OK;\x0C"), 1);
            new EpsonEeprom(d4, profile).WriteByte(0x30, 0, CancellationToken.None);
            Check(fake.Writes[^1][6..].SequenceEqual(EpsonEeprom.EncodeFactory(profile, 0x42, 0x30, 0)), "Direct write acknowledgment through actual D4 and EEPROM stack");
            var oneByteProfile = profile with { Reset = [new EepromWrite(0x30, 0)], Close = null };
            fake.AddPacket(2, Encoding.ASCII.GetBytes("@BDC PS\r\nEE:003006;\x0C"), 1); // Backup.
            fake.AddPacket(2, Encoding.ASCII.GetBytes("||:42:OK;\x0C"), 1);
            fake.AddPacket(2, Encoding.ASCII.GetBytes("@BDC PS\r\nEE:003006;\x0C"), 1); // No actual change.
            var resetLog = new List<string>();
            Reject(() => new ResetService(oneByteProfile, new EpsonEeprom(d4, oneByteProfile), resetLog.Add)
                .Reset(temp, "TEST", CancellationToken.None), "Direct OK cannot pass reset when EEPROM byte did not change");
            Check(resetLog.Any(s => s.Contains("Попыток записи: 1, проверено чтением: 0")), "Unverified write is counted as attempted, not completed");
            fake.AddPacket(0, [0x82, 0, 2, 2]); fake.AddPacket(0, [0x88, 0]); d4.Dispose();
            var bad = new ScriptedTransport(); bad.AddRaw([0, 0, 0, 5, 1, 0]);
            Reject(() => new D4Link(bad).Open(CancellationToken.None), "Malformed packet length rejected");
            var memory = new SimulatedEeprom(profile);
            var service = new ResetService(profile, memory);
            Check(service.ReadSerial(CancellationToken.None) == "SIMULATOR1", "Serial decoding");
            Check(service.ReadCounters(CancellationToken.None).All(c => c.Percent > 95 && c.Percent < 97), "Counter little-endian decoding");
            var reset = service.Reset(temp, "SIMULATOR1", CancellationToken.None);
            Check(reset.Written == profile.Reset.Length + 1 && reset.Counters.All(c => c.Value == 0), "Full reset with final reread");
            Check(memory.Writes.Any(w => w.Value == 0x5E) && memory.ReadByte(0x100, CancellationToken.None) == 0xA4, "Auxiliary nonzero writes and close AND mask");
            using var backup = JsonDocument.Parse(File.ReadAllText(reset.BackupPath));
            Check(backup.RootElement.GetProperty("original").EnumerateArray().Any(p => p.GetProperty("address").GetInt32() == 0x100 && p.GetProperty("value").GetInt32() == 0xA5), "Backup includes original close register");
            var failure = new SimulatedEeprom(profile) { FailAddress = 0x34 };
            Reject(() => new ResetService(profile, failure).Reset(temp, "TEST", CancellationToken.None), "USB failure stops reset");
            Check(failure.Writes.Count == 3 && failure.Writes.All(w => w.Address != 0xFF), "No writes after failure and no blind rollback");
            var unwritable = Path.Combine(temp, "not-a-directory"); File.WriteAllText(unwritable, "test");
            var noWrite = new SimulatedEeprom(profile);
            Reject(() => new ResetService(profile, noWrite).Reset(unwritable, "TEST", CancellationToken.None), "Backup failure blocks all writes");
            Check(noWrite.Writes.Count == 0, "No EEPROM writes before durable backup");
            using var canceled = new CancellationTokenSource(); canceled.Cancel();
            Reject(() => new ResetService(profile, noWrite).Reset(temp, "TEST", canceled.Token), "Cancellation before reset");
            var mismatch = new WrongReadback(new SimulatedEeprom(profile));
            Reject(() => new ResetService(profile, mismatch).Reset(temp, "TEST", CancellationToken.None), "Write readback mismatch fails reset");
            Check(mismatch.Writes == 1, "No next write after verification failure");
            int statusCalls = 0;
            bool PaddedStatus(IntPtr buffer, uint size, out uint needed, out int error)
            {
                statusCalls++; needed = 8; error = size < needed ? 122 : 0;
                if (size < needed) return false;
                Marshal.WriteInt32(buffer, 0); return true;
            }
            Check(PrintQueue.ReadStatus(PaddedStatus, CancellationToken.None) == 0 && statusCalls == 2, "Spooler status uses required buffer size, not fixed four bytes");
            int growingCalls = 0;
            bool GrowingStatus(IntPtr buffer, uint size, out uint needed, out int error)
            {
                growingCalls++; needed = growingCalls == 1 ? 8u : 16u; error = size < needed ? 122 : 0;
                if (size < needed) return false;
                Marshal.WriteInt32(buffer, 0x400); return true;
            }
            Check(PrintQueue.ReadStatus(GrowingStatus, CancellationToken.None) == 0x400 && growingCalls == 3, "Spooler size growth is retried");
            bool DeniedStatus(IntPtr buffer, uint size, out uint needed, out int error) { needed = 0; error = 5; return false; }
            Reject(() => PrintQueue.ReadStatus(DeniedStatus, CancellationToken.None), "Spooler failure does not become idle");
            bool InvalidStatus(IntPtr buffer, uint size, out uint needed, out int error) { needed = 2; error = 122; return false; }
            Reject(() => PrintQueue.ReadStatus(InvalidStatus, CancellationToken.None), "Truncated status buffer is rejected");
            foreach (uint status in new uint[] { 0x100, 0x200, 0x400 })
                Reject(() => PrintQueue.CheckStatus(status, "Test queue"), $"Busy status 0x{status:X} still blocks reset");
            PrintQueue.CheckStatus(0, "Test queue");
            Check(true, "Idle status allows reset preflight");
            Reject(() => PrintQueue.ReadStatus(PaddedStatus, canceled.Token), "Cancellation blocks spooler status read");
            Console.WriteLine($"{count} checks passed. Readable profiles: {db.Models.Count(p => p.CanRead)}; reset profiles: {db.Models.Count(p => p.CanReset)}.");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex.ToString()); return 1; }
    }
    private sealed class WrongReadback(SimulatedEeprom inner) : IEeprom
    {
        public int Writes { get; private set; }
        public byte ReadByte(int address, CancellationToken token) => Writes > 0 ? (byte)0xFF : inner.ReadByte(address, token);
        public void WriteByte(int address, byte value, CancellationToken token) { Writes++; inner.WriteByte(address, value, token); }
    }
    private sealed class ScriptedTransport : IByteTransport
    {
        private readonly Queue<byte> input = new();
        public List<byte[]> Writes { get; } = [];
        public void AddRaw(byte[] bytes) { foreach (var b in bytes) input.Enqueue(b); }
        public void AddPacket(byte socket, byte[] bytes, byte credit = 1) => AddRaw(D4Link.EncodePacket(socket, socket, credit, 0, bytes));
        public void Write(byte[] data, CancellationToken token) { token.ThrowIfCancellationRequested(); Writes.Add(data); }
        public byte[] ReadExact(int count, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (input.Count < count) throw new IOException("Truncated test stream");
            return Enumerable.Range(0, count).Select(_ => input.Dequeue()).ToArray();
        }
        public void Dispose() { }
    }
}
