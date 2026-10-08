using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace PadReset.Core;

public sealed class ProfileDatabase
{
    public IReadOnlyList<ModelProfile> Models { get; }
    public ProfileDatabase()
    {
        using var stream = typeof(ProfileDatabase).Assembly.GetManifestResourceStream("PadReset.models.xml")
            ?? throw new InvalidDataException("Не найдена встроенная база моделей.");
        var root = XDocument.Load(stream).Root!;
        var profiles = root.Element("devices")!.Elements().ToDictionary(e => e.Name.LocalName);
        Models = root.Element("records")!.Elements("printer").Select(p => Parse(p, profiles))
            .OrderBy(p => p.Title, StringComparer.OrdinalIgnoreCase).ToArray();
    }
    public ModelProfile? Find(string model)
    {
        // Only the harmless " Series" suffix is normalized; no guessing from product numbers.
        static string Normal(string s) => Regex.Replace(s.Trim(), @"\s+Series$", "", RegexOptions.IgnoreCase);
        return Models.FirstOrDefault(p => p.Model.Equals(model.Trim(), StringComparison.OrdinalIgnoreCase))
            ?? Models.FirstOrDefault(p => Normal(p.Model).Equals(Normal(model), StringComparison.OrdinalIgnoreCase));
    }
    public static int[] Numbers(string? text) => Regex.Matches(text ?? "", @"0x[0-9a-fA-F]+")
        .Select(m => int.Parse(m.Value[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture)).ToArray();
    private static string DirectText(XElement? e) => e == null ? "" : string.Concat(e.Nodes().OfType<XText>().Select(t => t.Value));
    private static ModelProfile Parse(XElement printer, Dictionary<string, XElement> profiles)
    {
        var parts = ((string?)printer.Attribute("specs") ?? "").Split(',').Select(s => profiles.GetValueOrDefault(s)).OfType<XElement>().ToArray();
        var service = parts.Select(p => p.Element("service")).LastOrDefault(p => p != null);
        byte[] Bytes(XElement? e) => Numbers(DirectText(e)).Select(n => checked((byte)n)).ToArray();
        var counters = new List<CounterProfile>();
        var writes = new List<EepromWrite>();
        int[] serial = [];
        CloseOperation? close = null;
        string? error = null;
        try
        {
            foreach (var part in parts)
            {
                foreach (var counter in part.Element("waste")?.Element("query")?.Elements("counter") ?? [])
                {
                    var entries = counter.Elements("entry").ToArray();
                    var primary = entries.Where(e => e.Element("filter") == null).SelectMany(e => Numbers(DirectText(e)))
                        .Concat(Numbers(DirectText(counter))).ToArray();
                    var extra = entries.Where(e => e.Element("filter") != null).SelectMany(e => Numbers(DirectText(e))).ToArray();
                    var maximum = long.Parse(counter.Element("max")!.Value, CultureInfo.InvariantCulture);
                    if (primary.Length is < 1 or > 4 || maximum <= 0) throw new InvalidDataException("Нестандартное описание счётчика.");
                    counters.Add(new(primary, extra, maximum));
                }
                if (part.Element("waste")?.Element("reset") is { } reset)
                {
                    var numbers = Numbers(DirectText(reset));
                    if (numbers.Length % 2 != 0) throw new InvalidDataException("Неполная последовательность сброса.");
                    for (int i = 0; i < numbers.Length; i += 2) writes.Add(new(numbers[i], checked((byte)numbers[i + 1])));
                    if (reset.Element("close") is { } end)
                    {
                        var read = Numbers(end.Element("query")?.Value);
                        var mask = Numbers(end.Element("var")?.Value);
                        var write = Numbers(end.Element("write")?.Value);
                        if (read.Length != 1 || mask.Length != 1 || write.Length != 1 || !end.Element("var")!.Value.Trim().EndsWith('&'))
                            throw new InvalidDataException("Неизвестная завершающая операция.");
                        close = new(read[0], checked((byte)mask[0]), write[0]);
                    }
                }
                if (part.Element("serial") is { } sn) serial = Numbers(DirectText(sn));
            }
            if (service == null) error = "Нет сервисного профиля.";
            var readLength = Numbers(service?.Element("readlen")?.Value).FirstOrDefault(2);
            var writeLength = Numbers(service?.Element("sendlen")?.Value).FirstOrDefault(2);
            var factory = Bytes(service?.Element("factory"));
            var keyword = Bytes(service?.Element("keyword"));
            if (factory.Length != 2 || readLength is not (1 or 2) || writeLength is not (1 or 2)) error = "Формат сервисного профиля пока не поддерживается.";
            if (writes.Count > 0 && keyword.Length != 8) error = "Нет полного ключа сервисной команды записи.";
            var maxAddress = (1 << (8 * Math.Min(readLength, writeLength))) - 1;
            var addresses = counters.SelectMany(c => c.BaseAddresses.Concat(c.ExtraAddresses)).Concat(serial).Concat(writes.Select(w => w.Address));
            if (close != null) addresses = addresses.Concat([close.ReadAddress, close.WriteAddress]);
            if (addresses.Any(a => a < 0 || a > maxAddress)) error = "Адреса выходят за пределы сервисного формата.";
            return new((string)printer.Attribute("model")!, (string)printer.Attribute("title")!, factory, keyword,
                readLength, writeLength, counters.ToArray(), writes.ToArray(), serial, close, error);
        }
        catch (Exception ex) when (ex is FormatException or OverflowException or InvalidDataException or NullReferenceException)
        {
            return new((string)printer.Attribute("model")!, (string)printer.Attribute("title")!, [], [], 2, 2, [], [], [], null, ex.Message);
        }
    }
}
