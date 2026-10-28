using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace DesktopDeck.Widgets;

/// <summary>What a paired device is, for its icon.</summary>
public enum BtKind { Generic, Headphones, Speaker, Mouse, Keyboard, Phone, Controller, Watch, Computer, Tv }

/// <summary>The state of the PC's Bluetooth radio.</summary>
public enum BtRadio { Unknown, On, Off, Disabled, NoAdapter, Denied }

/// <summary>One paired device as the widget shows it. Battery is 0-100 or null when Windows doesn't report it.</summary>
public sealed record BtDevice(string Id, string Name, bool Connected, string Address, BtKind Kind, int? Battery, bool IsLe = false, string ContainerId = "");

/// <summary>Made-up (or parsed) data for smoke renders.</summary>
public sealed record BtSample(BtRadio Radio, IReadOnlyList<BtDevice> Devices);

/// <summary>The devices to draw, after hiding, filtering, sorting and the maximum.</summary>
public sealed record BtArrangement(IReadOnlyList<BtDevice> Shown, int Hidden, int More, int Connected);

/// <summary>
/// Bluetooth widget logic with no Windows or WPF types: class of device and appearance to a device kind, battery
/// text and thresholds, merging, hiding, sorting, once-a-day notification bookkeeping and sample data. Unit tested.
/// </summary>
public static class BluetoothLogic
{
    public const string UnknownName = "Unknown device";

    // ---------- Device kind ----------

    /// <summary>
    /// The kind from a Bluetooth class-of-device major and minor number. The minor is the 6-bit field, already shifted
    /// down (what Windows reports as System.Devices.Aep.Bluetooth.Cod.Minor).
    /// </summary>
    public static BtKind KindFromClass(int major, int minor)
    {
        switch (major)
        {
            case 1: return BtKind.Computer;
            case 2: return BtKind.Phone;
            case 4:
                return minor switch
                {
                    1 or 2 or 6 => BtKind.Headphones,
                    5 or 7 or 8 or 10 => BtKind.Speaker,
                    9 or 14 or 15 or 16 => BtKind.Tv,
                    _ => BtKind.Generic,
                };
            case 5:
                int pointing = (minor >> 4) & 3;
                if (pointing == 2) return BtKind.Mouse;
                if (pointing is 1 or 3) return BtKind.Keyboard;
                return (minor & 0xF) is 1 or 2 ? BtKind.Controller : BtKind.Generic;
            case 7: return minor == 1 ? BtKind.Watch : BtKind.Generic;
            case 8: return minor is 4 or 5 ? BtKind.Controller : BtKind.Generic;
            default: return BtKind.Generic;
        }
    }

    /// <summary>The kind from a raw 24-bit class-of-device value (bits 8-12 major, bits 2-7 minor).</summary>
    public static BtKind KindFromRawClass(uint raw) => KindFromClass((int)((raw >> 8) & 0x1F), (int)((raw >> 2) & 0x3F));

    /// <summary>The kind from a GATT appearance value (category in the top 10 bits, sub-category in the low 6).</summary>
    public static BtKind KindFromAppearance(int appearance)
    {
        if (appearance <= 0) return BtKind.Generic;
        int category = appearance >> 6, sub = appearance & 0x3F;
        return category switch
        {
            1 => BtKind.Phone,
            2 => BtKind.Computer,
            3 => BtKind.Watch,
            5 => BtKind.Tv,
            10 => BtKind.Speaker,
            15 => sub switch { 1 => BtKind.Keyboard, 2 or 9 => BtKind.Mouse, 3 or 4 => BtKind.Controller, _ => BtKind.Generic },
            37 => BtKind.Headphones,
            _ => BtKind.Generic,
        };
    }

    private static readonly (string[] Words, BtKind Kind)[] NameHints =
    [
        (["headphone", "headset", "earbud", "earphone", "airpods", "buds", "wh-1000", "wf-1000", "pods", "arctis"], BtKind.Headphones),
        (["speaker", "soundbar", "boombox", "boom ", "echo ", "homepod", "jbl", "charge 5", "flip 6"], BtKind.Speaker),
        (["mouse", "mx master", "trackpad", "trackball"], BtKind.Mouse),
        (["keyboard", "keychron", "k380", "mx keys"], BtKind.Keyboard),
        (["controller", "gamepad", "xbox", "dualshock", "dualsense", "joy-con", "joycon", "wireless controller"], BtKind.Controller),
        (["watch", "galaxy fit", "mi band", "smart band", "forerunner", "fenix"], BtKind.Watch),
        (["iphone", "android", "pixel", "galaxy s", "galaxy a", "oneplus", "phone"], BtKind.Phone),
        (["laptop", "desktop", "notebook", "macbook", "thinkpad", "-pc", " pc"], BtKind.Computer),
        ([" tv ", "[tv]", "bravia", "monitor"], BtKind.Tv),
    ];

    /// <summary>A best guess from the device name, used only when the class and appearance say nothing.</summary>
    public static BtKind KindFromName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return BtKind.Generic;
        string lower = " " + name.Trim().ToLowerInvariant() + " ";
        foreach (var (words, kind) in NameHints)
            foreach (string word in words)
                if (lower.Contains(word, StringComparison.Ordinal)) return kind;
        return BtKind.Generic;
    }

    /// <summary>The best kind from whatever Windows reported: appearance first (LE), then class of device, then the name.</summary>
    public static BtKind Resolve(int? major, int? minor, int? appearance, string? name)
    {
        var kind = appearance is { } a ? KindFromAppearance(a) : BtKind.Generic;
        if (kind == BtKind.Generic && major is { } m) kind = KindFromClass(m, minor ?? 0);
        if (kind == BtKind.Generic) kind = KindFromName(name);
        return kind;
    }

    /// <summary>A Segoe Fluent Icons glyph for the kind.</summary>
    public static string Glyph(BtKind kind) => kind switch
    {
        BtKind.Headphones => "",
        BtKind.Speaker => "",
        BtKind.Mouse => "",
        BtKind.Keyboard => "",
        BtKind.Phone => "",
        BtKind.Controller => "",
        BtKind.Watch => "",
        BtKind.Computer => "",
        BtKind.Tv => "",
        _ => "",
    };

    public static string KindLabel(BtKind kind) => kind switch
    {
        BtKind.Headphones => "Headphones",
        BtKind.Speaker => "Speaker",
        BtKind.Mouse => "Mouse",
        BtKind.Keyboard => "Keyboard",
        BtKind.Phone => "Phone",
        BtKind.Controller => "Controller",
        BtKind.Watch => "Watch",
        BtKind.Computer => "Computer",
        BtKind.Tv => "Display",
        _ => "Bluetooth device",
    };

    // ---------- Names and addresses ----------

    public static string DisplayName(string? name) => string.IsNullOrWhiteSpace(name) ? UnknownName : name.Trim();

    /// <summary>Hex digits only, upper case: "e8:eb:1b:84:aa:72" becomes "E8EB1B84AA72". Empty when it isn't a 48-bit address.</summary>
    public static string NormalizeAddress(string? address)
    {
        if (string.IsNullOrWhiteSpace(address)) return "";
        var hex = new StringBuilder(12);
        foreach (char c in address)
        {
            if (Uri.IsHexDigit(c)) hex.Append(char.ToUpperInvariant(c));
            else if (c is not (':' or '-' or ' ' or '.')) return "";
        }
        return hex.Length == 12 ? hex.ToString() : "";
    }

    /// <summary>"AA:BB:CC:DD:EE:FF", or the text as given when it isn't an address.</summary>
    public static string FormatAddress(string? address)
    {
        string hex = NormalizeAddress(address);
        if (hex.Length != 12) return address?.Trim() ?? "";
        return string.Join(":", Enumerable.Range(0, 6).Select(i => hex.Substring(i * 2, 2)));
    }

    /// <summary>Finds an address at the end of a Windows device id ("...-e8:eb:1b:84:aa:72"), or "".</summary>
    public static string AddressFromId(string? id)
    {
        if (string.IsNullOrEmpty(id) || id.Length < 17) return "";
        return NormalizeAddress(id[^17..]).Length == 12 ? FormatAddress(id[^17..]) : "";
    }

    // ---------- Battery ----------

    /// <summary>A battery level from any numeric Windows property (byte, int, text...). Null when missing or not 0-100.</summary>
    public static int? ParseBattery(object? value)
    {
        long level;
        switch (value)
        {
            case null: return null;
            case byte b: level = b; break;
            case sbyte sb: level = sb; break;
            case short s: level = s; break;
            case ushort us: level = us; break;
            case int i: level = i; break;
            case uint ui: level = ui; break;
            case long l: level = l; break;
            case ulong ul when ul <= 1000: level = (long)ul; break;
            case string text when long.TryParse(text.Trim().TrimEnd('%'), NumberStyles.Integer, CultureInfo.InvariantCulture, out long parsed): level = parsed; break;
            default: return null;
        }
        return level is >= 0 and <= 100 ? (int)level : null;
    }

    public static string BatteryText(int? level) => level is { } l ? Math.Clamp(l, 0, 100).ToString(CultureInfo.InvariantCulture) + "%" : "";

    public static bool IsLow(int? level, int threshold) => level is { } l && l <= threshold;

    /// <summary>Segoe Fluent Icons battery glyph for the level (empty at 0, full at 100).</summary>
    public static string BatteryGlyph(int level)
    {
        int step = (int)Math.Round(Math.Clamp(level, 0, 100) / 10.0);
        return step >= 10 ? "" : ((char)(0xE850 + step)).ToString();
    }

    /// <summary>The lowest level of several readings (one device can expose several battery nodes, e.g. two earbuds).</summary>
    public static int? LowestBattery(IEnumerable<int?> levels)
    {
        int? lowest = null;
        foreach (var level in levels)
            if (level is { } l && (lowest is null || l < lowest)) lowest = l;
        return lowest;
    }

    // ---------- Merging, hiding, sorting ----------

    /// <summary>
    /// One entry per physical device: a headset paired both as classic and as LE shows up twice in Windows, with the same
    /// container id or address. Keeps the connected (then classic) entry and fills in the name, kind and battery from the others.
    /// </summary>
    public static List<BtDevice> Merge(IEnumerable<BtDevice> devices)
    {
        var order = new List<string>();
        var groups = new Dictionary<string, List<BtDevice>>(StringComparer.OrdinalIgnoreCase);
        foreach (var device in devices)
        {
            string address = NormalizeAddress(device.Address);
            string key = device.ContainerId.Length > 0 && !IsEmptyGuid(device.ContainerId) ? "c:" + device.ContainerId
                : address.Length > 0 ? "a:" + address : "i:" + device.Id;
            if (!groups.TryGetValue(key, out var list)) { groups[key] = list = []; order.Add(key); }
            list.Add(device);
        }
        var merged = new List<BtDevice>(order.Count);
        foreach (string key in order)
        {
            var group = groups[key].OrderByDescending(d => d.Connected).ThenBy(d => d.IsLe).ThenBy(d => d.Id, StringComparer.Ordinal).ToList();
            var primary = group[0];
            merged.Add(primary with
            {
                Connected = group.Any(d => d.Connected),
                Name = group.Select(d => d.Name).FirstOrDefault(n => !string.IsNullOrWhiteSpace(n)) ?? primary.Name,
                Address = group.Select(d => d.Address).FirstOrDefault(a => !string.IsNullOrWhiteSpace(a)) ?? primary.Address,
                Kind = group.Select(d => d.Kind).FirstOrDefault(k => k != BtKind.Generic),
                Battery = group.Select(d => d.Battery).FirstOrDefault(b => b is not null),
            });
        }
        return merged;
    }

    private static bool IsEmptyGuid(string text) => Guid.TryParse(text, out var g) && g == Guid.Empty;

    /// <summary>
    /// Whether a device is on the hidden list. A line is a name ("Old speaker") or a name with an address in brackets
    /// ("Old speaker [AA:BB:CC:DD:EE:FF]", which is what "Hide this device" writes) or a bare address. Names and addresses
    /// match without regard to case; a line with an address matches that address only.
    /// </summary>
    public static bool IsHidden(BtDevice device, IEnumerable<string> hiddenLines)
    {
        string address = NormalizeAddress(device.Address);
        string name = DisplayName(device.Name);
        foreach (string raw in hiddenLines)
        {
            string line = raw.Trim();
            if (line.Length == 0) continue;
            var (lineName, lineAddress) = SplitHiddenLine(line);
            // A line with an address decides by address; the name only counts when the device has no known address.
            if (lineAddress.Length > 0 && address.Length > 0)
            {
                if (string.Equals(lineAddress, address, StringComparison.Ordinal)) return true;
                continue;
            }
            if (lineName.Length > 0 && string.Equals(lineName, name, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    private static (string Name, string Address) SplitHiddenLine(string line)
    {
        int open = line.LastIndexOf('[');
        if (open >= 0 && line.EndsWith(']'))
        {
            string address = NormalizeAddress(line[(open + 1)..^1]);
            if (address.Length == 12) return (line[..open].Trim(), address);
        }
        string bare = NormalizeAddress(line);
        return bare.Length == 12 ? ("", bare) : (line, "");
    }

    /// <summary>The line "Hide this device" adds ("Name [AA:BB:..]", or just the name when the address is unknown).</summary>
    public static string HiddenLine(BtDevice device)
    {
        string address = FormatAddress(device.Address);
        return NormalizeAddress(address).Length == 12 ? $"{DisplayName(device.Name)} [{address}]" : DisplayName(device.Name);
    }

    /// <summary>The hidden-devices text with this device added (unchanged when it is already hidden).</summary>
    public static string AddHidden(string current, BtDevice device)
    {
        var lines = SplitLines(current);
        if (IsHidden(device, lines)) return string.Join("\n", lines);
        lines.Add(HiddenLine(device));
        return string.Join("\n", lines);
    }

    /// <summary>The hidden-devices text without any line that hides this device.</summary>
    public static string RemoveHidden(string current, BtDevice device) =>
        string.Join("\n", SplitLines(current).Where(line => !IsHidden(device, [line])));

    private static List<string> SplitLines(string text) =>
        (text ?? "").Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToList();

    public static readonly string[] SortModes = ["connected", "name", "battery"];

    /// <summary>Sorts a copy: "connected" (connected first, then by name), "name", or "battery" (lowest first, unknown last).</summary>
    public static List<BtDevice> Sort(IEnumerable<BtDevice> devices, string mode)
    {
        var list = devices.ToList();
        Comparison<BtDevice> byName = (a, b) => string.Compare(DisplayName(a.Name), DisplayName(b.Name), StringComparison.CurrentCultureIgnoreCase);
        Comparison<BtDevice> comparison = mode switch
        {
            "name" => byName,
            "battery" => (a, b) =>
            {
                int result = (a.Battery ?? int.MaxValue).CompareTo(b.Battery ?? int.MaxValue);
                return result != 0 ? result : byName(a, b);
            },
            _ => (a, b) =>
            {
                int result = b.Connected.CompareTo(a.Connected);
                return result != 0 ? result : byName(a, b);
            },
        };
        // List.Sort is unstable; order by the comparison, then the original position.
        return list.Select((d, i) => (d, i)).OrderBy(x => x, Comparer<(BtDevice d, int i)>.Create((x, y) => { int r = comparison(x.d, y.d); return r != 0 ? r : x.i.CompareTo(y.i); })).Select(x => x.d).ToList();
    }

    /// <summary>
    /// Hides the hidden list (unless <paramref name="revealHidden"/>), keeps only connected devices if asked, sorts, and cuts
    /// to <paramref name="max"/>. Hidden counts devices that would otherwise have been listed.
    /// </summary>
    public static BtArrangement Arrange(IEnumerable<BtDevice> devices, bool onlyConnected, string sortMode, IEnumerable<string> hiddenLines, bool revealHidden, int max)
    {
        var lines = hiddenLines.ToList();
        var candidates = devices.Where(d => !onlyConnected || d.Connected).ToList();
        int hidden = candidates.Count(d => IsHidden(d, lines));
        var visible = revealHidden ? candidates : candidates.Where(d => !IsHidden(d, lines)).ToList();
        var sorted = Sort(visible, sortMode);
        max = Math.Max(1, max);
        return new BtArrangement(sorted.Take(max).ToList(), hidden, Math.Max(0, sorted.Count - max), visible.Count(d => d.Connected));
    }

    /// <summary>"2 connected", "3 paired" or "" when there are none.</summary>
    public static string Summary(int connected, int paired) =>
        connected > 0 ? connected.ToString(CultureInfo.InvariantCulture) + " connected" : paired > 0 ? paired.ToString(CultureInfo.InvariantCulture) + " paired" : "";

    /// <summary>"Connected" or "Paired", with the address when asked.</summary>
    public static string StatusLine(BtDevice device, bool showAddress)
    {
        string status = device.Connected ? "Connected" : "Paired";
        string address = showAddress ? FormatAddress(device.Address) : "";
        return address.Length > 0 ? status + " · " + address : status;
    }

    // ---------- Radio ----------

    /// <summary>A headline and a hint for a radio that isn't on.</summary>
    public static (string Title, string Detail) RadioNote(BtRadio radio) => radio switch
    {
        BtRadio.Off => ("Bluetooth is off", "Turn it on with the switch to see your devices."),
        BtRadio.Disabled => ("Bluetooth is unavailable", "It may be off because of airplane mode or a hardware switch."),
        BtRadio.NoAdapter => ("No Bluetooth adapter found", "This PC has no Bluetooth, or its driver isn't working."),
        BtRadio.Denied => ("Windows blocked Bluetooth access", "Check Bluetooth and privacy settings, then refresh."),
        BtRadio.Unknown => ("Looking for Bluetooth…", ""),
        _ => ("", ""),
    };

    /// <summary>Whether the on/off switch can do anything.</summary>
    public static bool CanSwitch(BtRadio radio) => radio is BtRadio.On or BtRadio.Off;

    // ---------- Low-battery notifications: once per device per day ----------

    /// <summary>The key a device is remembered by: its address, or its id when it has none.</summary>
    public static string NotifyKey(BtDevice device) => NormalizeAddress(device.Address) is { Length: > 0 } a ? a : device.Id;

    /// <summary>Parses the saved "key|yyyy-MM-dd" lines.</summary>
    public static Dictionary<string, DateOnly> ParseNotified(string? text)
    {
        var map = new Dictionary<string, DateOnly>(StringComparer.OrdinalIgnoreCase);
        foreach (string line in (text ?? "").Split('\n'))
        {
            int bar = line.LastIndexOf('|');
            if (bar <= 0) continue;
            if (DateOnly.TryParseExact(line[(bar + 1)..].Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)) map[line[..bar]] = date;
        }
        return map;
    }

    /// <summary>Saved text for the map, keeping only today's entries.</summary>
    public static string FormatNotified(Dictionary<string, DateOnly> map, DateOnly today) =>
        string.Join("\n", map.Where(p => p.Value == today).OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => p.Key + "|" + p.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)));

    public static bool ShouldNotify(Dictionary<string, DateOnly> map, string key, DateOnly today) => !map.TryGetValue(key, out var last) || last != today;

    /// <summary>Connected, unhidden devices at or below the threshold that haven't been announced today.</summary>
    public static List<BtDevice> DueForLowBattery(IEnumerable<BtDevice> devices, int threshold, IEnumerable<string> hiddenLines, Dictionary<string, DateOnly> notified, DateOnly today)
    {
        var lines = hiddenLines.ToList();
        return devices.Where(d => d.Connected && IsLow(d.Battery, threshold) && !IsHidden(d, lines) && ShouldNotify(notified, NotifyKey(d), today)).ToList();
    }

    // ---------- Sample data ----------

    public const string DefaultSampleJson = """
        {"radio":"on","devices":[
          {"name":"Studio Headphones","connected":true,"kind":"headphones","battery":82,"address":"24:FC:32:DB:C9:3F"},
          {"name":"Desk Speaker","connected":true,"kind":"speaker","battery":14,"address":"10:2A:4B:77:01:5C"},
          {"name":"Travel Mouse","connected":true,"kind":"mouse","battery":57,"address":"C2:AF:D8:D0:3E:E7","le":true},
          {"name":"Pocket Phone","connected":false,"kind":"phone","address":"D0:04:B0:57:B3:B6"},
          {"name":"Mechanical Keyboard","connected":false,"kind":"keyboard","battery":100,"address":"F4:EE:25:4C:39:17","le":true},
          {"name":"Game Controller","connected":false,"kind":"controller","address":"A8:5E:45:11:22:33"},
          {"name":"Fitness Watch","connected":false,"kind":"watch","battery":41,"address":"E6:09:74:00:98:98","le":true}
        ]}
        """;

    /// <summary>
    /// Parses the sample JSON: {"radio":"on|off|disabled|none|denied","devices":[{"name","connected","kind","battery","address","le"}]}.
    /// Returns null when it isn't usable. Never throws.
    /// </summary>
    public static BtSample? ParseSample(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;
            var radio = BtRadio.On;
            if (root.TryGetProperty("radio", out var r) && r.ValueKind == JsonValueKind.String)
                radio = r.GetString()?.Trim().ToLowerInvariant() switch
                {
                    "off" => BtRadio.Off,
                    "disabled" => BtRadio.Disabled,
                    "none" or "noadapter" => BtRadio.NoAdapter,
                    "denied" => BtRadio.Denied,
                    "unknown" => BtRadio.Unknown,
                    _ => BtRadio.On,
                };
            var devices = new List<BtDevice>();
            if (root.TryGetProperty("devices", out var list) && list.ValueKind == JsonValueKind.Array)
            {
                int index = 0;
                foreach (var entry in list.EnumerateArray())
                {
                    index++;
                    if (entry.ValueKind != JsonValueKind.Object) continue;
                    string name = Str(entry, "name");
                    string address = Str(entry, "address");
                    var kind = Enum.TryParse(Str(entry, "kind"), true, out BtKind parsed) ? parsed : KindFromName(name);
                    int? battery = entry.TryGetProperty("battery", out var b) && b.ValueKind == JsonValueKind.Number && b.TryGetInt32(out int level) ? ParseBattery(level) : null;
                    bool connected = entry.TryGetProperty("connected", out var c) && c.ValueKind == JsonValueKind.True;
                    bool le = entry.TryGetProperty("le", out var l) && l.ValueKind == JsonValueKind.True;
                    devices.Add(new BtDevice("sample-" + index.ToString(CultureInfo.InvariantCulture), name, connected, address, kind, battery, le));
                }
            }
            return new BtSample(radio, devices);
        }
        catch (JsonException) { return null; }
    }

    private static string Str(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";
}
