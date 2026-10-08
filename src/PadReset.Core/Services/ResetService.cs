using System.Text;
using System.Text.Json;

namespace PadReset.Core;

public sealed class ResetService(ModelProfile profile, IEeprom eeprom, Action<string>? log = null)
{
    private void Log(string text) => log?.Invoke(text);
    public CounterReading[] ReadCounters(CancellationToken token)
    {
        var values = new Dictionary<int, byte>();
        return profile.Counters.Select((c, i) =>
        {
            var raw = new Dictionary<int, byte>();
            foreach (var address in c.BaseAddresses.Concat(c.ExtraAddresses).Distinct())
            {
                if (!values.TryGetValue(address, out byte value)) values[address] = value = eeprom.ReadByte(address, token);
                raw[address] = value;
            }
            long level = 0;
            for (int j = 0; j < c.BaseAddresses.Length; j++) level |= (long)values[c.BaseAddresses[j]] << (8 * j);
            return new CounterReading(i + 1, level, c.Maximum, raw, c.ExtraAddresses.Length > 0);
        }).ToArray();
    }
    public string ReadSerial(CancellationToken token)
    {
        if (profile.SerialAddresses.Length == 0) return "Не указан";
        return Encoding.ASCII.GetString(profile.SerialAddresses.Select(a => eeprom.ReadByte(a, token)).ToArray()).Trim('\0', ' ', '\r', '\n');
    }
    public ResetResult Reset(string backupDirectory, string serial, CancellationToken token)
    {
        if (!profile.CanReset) throw new InvalidOperationException(profile.UnsupportedReason ?? "Нет последовательности сброса для этой модели.");
        var addresses = profile.Reset.Select(w => w.Address);
        if (profile.Close is { } close) addresses = addresses.Concat([close.ReadAddress, close.WriteAddress]);
        var before = new Dictionary<int, byte>();
        foreach (int address in addresses.Distinct()) { token.ThrowIfCancellationRequested(); before[address] = eeprom.ReadByte(address, token); }
        Directory.CreateDirectory(backupDirectory);
        string path = Path.Combine(backupDirectory, $"eeprom-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.json");
        var backupBytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
        {
            schema = 1,
            model = profile.Model,
            serial,
            timestampUtc = DateTime.UtcNow,
            original = before.Select(p => new { address = p.Key, value = p.Value }).ToArray(),
            planned = profile.Reset,
            close = profile.Close
        }, new JsonSerializerOptions { WriteIndented = true }));
        using (var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read, 4096, FileOptions.WriteThrough))
        { file.Write(backupBytes); file.Flush(flushToDisk: true); }
        Log("Исходные значения сохранены: " + path);
        int written = 0;
        int attempted = 0;
        try
        {
            foreach (var write in profile.Reset)
            {
                token.ThrowIfCancellationRequested();
                attempted++;
                eeprom.WriteByte(write.Address, write.Value, token);
                if (eeprom.ReadByte(write.Address, token) != write.Value) throw new IOException($"Проверка записи 0x{write.Address:X} не прошла.");
                written++;
                Log($"[{written}/{profile.Reset.Length}] 0x{write.Address:X4} = 0x{write.Value:X2} · проверено");
            }
            if (profile.Close is { } end)
            {
                byte value = (byte)(eeprom.ReadByte(end.ReadAddress, token) & end.Mask);
                attempted++;
                eeprom.WriteByte(end.WriteAddress, value, token);
                if (eeprom.ReadByte(end.WriteAddress, token) != value) throw new IOException("Не подтверждена завершающая операция.");
                written++;
                Log("Завершающая операция проверена.");
            }
            // Confirm the final map as well; an auxiliary write can change another register.
            foreach (var write in profile.Reset.DistinctBy(w => w.Address).Select(w => profile.Reset.Last(x => x.Address == w.Address)))
                if (eeprom.ReadByte(write.Address, token) != write.Value) throw new IOException($"Итоговая проверка 0x{write.Address:X} не прошла.");
            var counters = ReadCounters(token);
            Log("Все записи подтверждены. Выключите и включите принтер, затем перечитайте счётчики.");
            return new(path, counters, written);
        }
        catch (Exception ex)
        {
            // Never perform blind rollback after a lost USB channel: preserve recovery evidence.
            string uncertain = attempted > written ? " Последняя попытка могла изменить байт; результат не проверен." : "";
            Log($"Сброс прерван. Попыток записи: {attempted}, проверено чтением: {written}.{uncertain} Резервная копия: {path}");
            throw new IOException($"Сброс не завершён. Попыток записи: {attempted}, проверено чтением: {written}.{uncertain}\nИсходные байты: {path}\n{ex.Message}", ex);
        }
    }
}
