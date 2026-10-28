using DesktopDeck.Widgets;

public static class BluetoothTests
{
    private static BtDevice Dev(string name, bool connected = false, int? battery = null, string address = "", BtKind kind = BtKind.Generic, string id = "", string container = "", bool le = false) =>
        new(id.Length > 0 ? id : "id-" + name, name, connected, address, kind, battery, le, container);

    public static void ClassOfDeviceMapsToKinds()
    {
        Expect.Equal(BtKind.Headphones, BluetoothLogic.KindFromClass(4, 1), "wearable headset");
        Expect.Equal(BtKind.Headphones, BluetoothLogic.KindFromClass(4, 6), "headphones");
        Expect.Equal(BtKind.Speaker, BluetoothLogic.KindFromClass(4, 5), "loudspeaker");
        Expect.Equal(BtKind.Tv, BluetoothLogic.KindFromClass(4, 15), "video display");
        Expect.Equal(BtKind.Phone, BluetoothLogic.KindFromClass(2, 3), "smartphone");
        Expect.Equal(BtKind.Computer, BluetoothLogic.KindFromClass(1, 3), "laptop");
        Expect.Equal(BtKind.Watch, BluetoothLogic.KindFromClass(7, 1), "wristwatch");
        Expect.Equal(BtKind.Generic, BluetoothLogic.KindFromClass(31, 0), "uncategorized");
        Expect.Equal(BtKind.Generic, BluetoothLogic.KindFromClass(3, 0), "network");
    }

    public static void PeripheralMinorSeparatesKeyboardMouseAndGamepad()
    {
        Expect.Equal(BtKind.Keyboard, BluetoothLogic.KindFromClass(5, 0x10), "keyboard");
        Expect.Equal(BtKind.Mouse, BluetoothLogic.KindFromClass(5, 0x20), "pointing device");
        Expect.Equal(BtKind.Keyboard, BluetoothLogic.KindFromClass(5, 0x30), "combo keyboard and pointer");
        Expect.Equal(BtKind.Controller, BluetoothLogic.KindFromClass(5, 2), "gamepad");
        Expect.Equal(BtKind.Controller, BluetoothLogic.KindFromClass(5, 1), "joystick");
        Expect.Equal(BtKind.Generic, BluetoothLogic.KindFromClass(5, 0), "uncategorized peripheral");
    }

    public static void RawClassOfDeviceIsDecoded()
    {
        // Values seen on a real PC: a headset, a smartphone and a plain computer-like device.
        Expect.Equal(BtKind.Headphones, BluetoothLogic.KindFromRawClass(0x240404), "headset raw");
        Expect.Equal(BtKind.Phone, BluetoothLogic.KindFromRawClass(0x5A020C), "phone raw");
        Expect.Equal(BtKind.Keyboard, BluetoothLogic.KindFromRawClass(0x002540), "keyboard raw");
        Expect.Equal(BtKind.Mouse, BluetoothLogic.KindFromRawClass(0x002580), "mouse raw");
        Expect.Equal(BtKind.Generic, BluetoothLogic.KindFromRawClass(0x1F00), "uncategorized raw");
    }

    public static void AppearanceMapsToKinds()
    {
        Expect.Equal(BtKind.Keyboard, BluetoothLogic.KindFromAppearance(0x3C1), "HID keyboard");
        Expect.Equal(BtKind.Mouse, BluetoothLogic.KindFromAppearance(0x3C2), "HID mouse");
        Expect.Equal(BtKind.Controller, BluetoothLogic.KindFromAppearance(0x3C4), "HID gamepad");
        Expect.Equal(BtKind.Phone, BluetoothLogic.KindFromAppearance(0x41), "phone");
        Expect.Equal(BtKind.Watch, BluetoothLogic.KindFromAppearance(0xC2), "smartwatch");
        Expect.Equal(BtKind.Headphones, BluetoothLogic.KindFromAppearance(0x943), "wearable audio headphones");
        Expect.Equal(BtKind.Generic, BluetoothLogic.KindFromAppearance(0), "unknown");
        Expect.Equal(BtKind.Generic, BluetoothLogic.KindFromAppearance(-5), "negative");
    }

    public static void ResolvePrefersAppearanceThenClassThenName()
    {
        Expect.Equal(BtKind.Mouse, BluetoothLogic.Resolve(null, null, 0x3C2, "Anything"), "appearance first");
        Expect.Equal(BtKind.Headphones, BluetoothLogic.Resolve(4, 1, null, "Anything"), "class second");
        Expect.Equal(BtKind.Speaker, BluetoothLogic.Resolve(31, 0, 0, "Kitchen Speaker"), "name last");
        Expect.Equal(BtKind.Generic, BluetoothLogic.Resolve(null, null, null, "X1"), "nothing known");
        Expect.Equal(BtKind.Phone, BluetoothLogic.Resolve(31, 0, 0, "Pixel 8"), "phone by name");
        Expect.Equal(BtKind.Headphones, BluetoothLogic.Resolve(null, null, null, "WH-1000XM5"), "headphones by name");
    }

    public static void EveryKindHasAGlyphAndLabel()
    {
        foreach (BtKind kind in Enum.GetValues<BtKind>())
        {
            Expect.True(BluetoothLogic.Glyph(kind).Length == 1, "glyph for " + kind);
            Expect.True(BluetoothLogic.KindLabel(kind).Length > 0, "label for " + kind);
        }
        Expect.True(BluetoothLogic.Glyph(BtKind.Mouse) != BluetoothLogic.Glyph(BtKind.Keyboard), "distinct glyphs");
    }

    public static void BatteryParsingAcceptsNumbersAndRejectsJunk()
    {
        Expect.Equal(40, BluetoothLogic.ParseBattery((byte)40), "byte");
        Expect.Equal(100, BluetoothLogic.ParseBattery(100), "int");
        Expect.Equal(0, BluetoothLogic.ParseBattery((ushort)0), "zero is valid");
        Expect.Equal(77, BluetoothLogic.ParseBattery("77%"), "text");
        Expect.True(BluetoothLogic.ParseBattery(null) is null, "null");
        Expect.True(BluetoothLogic.ParseBattery(255) is null, "255 is not a level");
        Expect.True(BluetoothLogic.ParseBattery(-1) is null, "negative");
        Expect.True(BluetoothLogic.ParseBattery("lots") is null, "words");
        Expect.True(BluetoothLogic.ParseBattery(3.5) is null, "doubles are ignored");
    }

    public static void BatteryTextThresholdAndGlyph()
    {
        Expect.Equal("85%", BluetoothLogic.BatteryText(85), "text");
        Expect.Equal("", BluetoothLogic.BatteryText(null), "unknown");
        Expect.Equal("100%", BluetoothLogic.BatteryText(150), "clamped");
        Expect.True(BluetoothLogic.IsLow(20, 20), "at the threshold is low");
        Expect.True(!BluetoothLogic.IsLow(21, 20), "above is fine");
        Expect.True(!BluetoothLogic.IsLow(null, 20), "unknown is not low");
        Expect.Equal("", BluetoothLogic.BatteryGlyph(0), "empty glyph");
        Expect.Equal("", BluetoothLogic.BatteryGlyph(100), "full glyph");
        Expect.Equal("", BluetoothLogic.BatteryGlyph(50), "half glyph");
        Expect.Equal(10, BluetoothLogic.LowestBattery([100, 10, null, 90]) ?? -1, "lowest of several");
        Expect.True(BluetoothLogic.LowestBattery([null, null]) is null, "none known");
    }

    public static void AddressesNormalizeAndFormat()
    {
        Expect.Equal("E8EB1B84AA72", BluetoothLogic.NormalizeAddress("e8:eb:1b:84:aa:72"), "normalize");
        Expect.Equal("E8EB1B84AA72", BluetoothLogic.NormalizeAddress("E8-EB-1B-84-AA-72"), "dashes");
        Expect.Equal("", BluetoothLogic.NormalizeAddress("not an address"), "junk");
        Expect.Equal("", BluetoothLogic.NormalizeAddress("e8:eb:1b"), "too short");
        Expect.Equal("E8:EB:1B:84:AA:72", BluetoothLogic.FormatAddress("e8eb1b84aa72"), "format");
        Expect.Equal("E8:EB:1B:84:AA:72", BluetoothLogic.AddressFromId("Bluetooth#BluetoothLE00:1a:7d:da:71:13-e8:eb:1b:84:aa:72"), "address from id");
        Expect.Equal("", BluetoothLogic.AddressFromId("short"), "no address in id");
    }

    public static void MergeCombinesClassicAndLeEntriesOfOneDevice()
    {
        string container = "11111111-2222-3333-4444-555555555555";
        var merged = BluetoothLogic.Merge(
        [
            Dev("Buds", connected: false, kind: BtKind.Generic, id: "le", container: container, le: true, battery: 60),
            Dev("Buds", connected: true, kind: BtKind.Headphones, id: "classic", container: container),
            Dev("Mouse", id: "m", container: "99999999-2222-3333-4444-555555555555"),
        ]);
        Expect.Equal(2, merged.Count, "two physical devices");
        var buds = merged.Single(d => d.Name == "Buds");
        Expect.True(buds.Connected, "connected if either is");
        Expect.Equal("classic", buds.Id, "the connected entry wins");
        Expect.Equal(BtKind.Headphones, buds.Kind, "kind");
        Expect.Equal(60, buds.Battery ?? -1, "battery filled in from the other entry");
    }

    public static void MergeFallsBackToAddressAndKeepsUnrelatedDevices()
    {
        var merged = BluetoothLogic.Merge(
        [
            Dev("A", address: "aa:bb:cc:dd:ee:ff", id: "1"),
            Dev("A again", address: "AA-BB-CC-DD-EE-FF", id: "2"),
            Dev("B", id: "3"),
            Dev("C", id: "4", container: "00000000-0000-0000-0000-000000000000"),
            Dev("D", id: "5", container: "00000000-0000-0000-0000-000000000000"),
        ]);
        Expect.Equal(4, merged.Count, "same address merges; empty container ids and ids do not");
    }

    public static void HiddenListMatchesNamesAndAddresses()
    {
        var speaker = Dev("Old Speaker", address: "AA:BB:CC:DD:EE:FF");
        Expect.True(BluetoothLogic.IsHidden(speaker, ["old speaker"]), "name, any case");
        Expect.True(BluetoothLogic.IsHidden(speaker, ["aa-bb-cc-dd-ee-ff"]), "bare address");
        Expect.True(BluetoothLogic.IsHidden(speaker, ["Whatever [aabbccddeeff]"]), "bracketed address wins over the name");
        Expect.True(!BluetoothLogic.IsHidden(speaker, ["Old Speaker [11:22:33:44:55:66]"]), "same name, other address: a different device");
        Expect.True(!BluetoothLogic.IsHidden(speaker, ["Old", "", "  "]), "partial names and blanks don't match");
        Expect.True(BluetoothLogic.IsHidden(Dev("Old Speaker"), ["Old Speaker [AA:BB:CC:DD:EE:FF]"]), "no address known: falls back to the name");
        Expect.True(!BluetoothLogic.IsHidden(speaker, []), "empty list");
    }

    public static void HidingAndUnhidingEditsTheListText()
    {
        var speaker = Dev("Old Speaker", address: "aa:bb:cc:dd:ee:ff");
        string hidden = BluetoothLogic.AddHidden("Keyboard", speaker);
        Expect.Equal("Keyboard\nOld Speaker [AA:BB:CC:DD:EE:FF]", hidden, "added with its address");
        Expect.Equal(hidden, BluetoothLogic.AddHidden(hidden, speaker), "adding twice changes nothing");
        Expect.Equal("Keyboard", BluetoothLogic.RemoveHidden(hidden, speaker), "unhide removes only that device");
        Expect.Equal("Old Speaker", BluetoothLogic.AddHidden("", Dev("Old Speaker")), "no address: name only");
    }

    public static void SortingModes()
    {
        var list = new List<BtDevice>
        {
            Dev("Zed", connected: true, battery: 80), Dev("alpha", battery: 10), Dev("Mid", connected: true, battery: 30), Dev("Beta"),
        };
        Expect.Equal("Mid,Zed,alpha,Beta", string.Join(",", BluetoothLogic.Sort(list, "connected").Select(d => d.Name)), "connected first, then name");
        Expect.Equal("alpha,Beta,Mid,Zed", string.Join(",", BluetoothLogic.Sort(list, "name").Select(d => d.Name)), "by name, case-insensitive");
        Expect.Equal("alpha,Mid,Zed,Beta", string.Join(",", BluetoothLogic.Sort(list, "battery").Select(d => d.Name)), "lowest battery first, unknown last");
        Expect.Equal("Mid,Zed,alpha,Beta", string.Join(",", BluetoothLogic.Sort(list, "nonsense").Select(d => d.Name)), "unknown mode behaves like connected-first");
        Expect.Equal(4, list.Count, "the input list is untouched");
    }

    public static void ArrangeFiltersHidesAndCaps()
    {
        var list = new List<BtDevice> { Dev("A", connected: true), Dev("B"), Dev("C", connected: true), Dev("Hidden one", connected: true), Dev("D") };
        var all = BluetoothLogic.Arrange(list, false, "name", ["hidden one"], false, 3);
        Expect.Equal("A,B,C", string.Join(",", all.Shown.Select(d => d.Name)), "hidden removed, capped");
        Expect.Equal(1, all.Hidden, "one hidden");
        Expect.Equal(1, all.More, "one more beyond the cap");
        Expect.Equal(2, all.Connected, "connected among those visible");
        var onlyConnected = BluetoothLogic.Arrange(list, true, "name", ["hidden one"], false, 10);
        Expect.Equal("A,C", string.Join(",", onlyConnected.Shown.Select(d => d.Name)), "only connected");
        Expect.Equal(1, onlyConnected.Hidden, "hidden counted among connected");
        var revealed = BluetoothLogic.Arrange(list, false, "name", ["hidden one"], true, 10);
        Expect.Equal(5, revealed.Shown.Count, "revealed");
        Expect.Equal(1, BluetoothLogic.Arrange(list, false, "name", [], false, 0).Shown.Count, "max is at least one");
    }

    public static void SummaryAndStatusLines()
    {
        Expect.Equal("2 connected", BluetoothLogic.Summary(2, 5), "connected count");
        Expect.Equal("5 paired", BluetoothLogic.Summary(0, 5), "paired count");
        Expect.Equal("", BluetoothLogic.Summary(0, 0), "nothing");
        Expect.Equal("Connected", BluetoothLogic.StatusLine(Dev("A", true, address: "AA:BB:CC:DD:EE:FF"), false), "connected");
        Expect.Equal("Paired · AA:BB:CC:DD:EE:FF", BluetoothLogic.StatusLine(Dev("A", false, address: "aabbccddeeff"), true), "paired with address");
        Expect.Equal("Paired", BluetoothLogic.StatusLine(Dev("A"), true), "no address to show");
        Expect.Equal(BluetoothLogic.UnknownName, BluetoothLogic.DisplayName("  "), "blank name");
    }

    public static void RadioNotesCoverEveryProblemState()
    {
        foreach (var radio in new[] { BtRadio.Off, BtRadio.Disabled, BtRadio.NoAdapter, BtRadio.Denied, BtRadio.Unknown })
            Expect.True(BluetoothLogic.RadioNote(radio).Title.Length > 0, "note for " + radio);
        Expect.Equal("", BluetoothLogic.RadioNote(BtRadio.On).Title, "no note when on");
        Expect.True(BluetoothLogic.CanSwitch(BtRadio.On) && BluetoothLogic.CanSwitch(BtRadio.Off), "can switch on and off");
        Expect.True(!BluetoothLogic.CanSwitch(BtRadio.NoAdapter) && !BluetoothLogic.CanSwitch(BtRadio.Denied) && !BluetoothLogic.CanSwitch(BtRadio.Disabled), "can't switch when unavailable");
    }

    public static void LowBatteryNotificationsHappenOncePerDevicePerDay()
    {
        var today = new DateOnly(2026, 10, 5);
        var devices = new List<BtDevice>
        {
            Dev("Low", true, 12, "AA:BB:CC:DD:EE:01"), Dev("Fine", true, 80, "AA:BB:CC:DD:EE:02"), Dev("Away", false, 5, "AA:BB:CC:DD:EE:03"),
            Dev("Hidden", true, 3, "AA:BB:CC:DD:EE:04"), Dev("Unknown", true, null, "AA:BB:CC:DD:EE:05"),
        };
        var notified = BluetoothLogic.ParseNotified("");
        var due = BluetoothLogic.DueForLowBattery(devices, 20, ["Hidden"], notified, today);
        Expect.Equal("Low", string.Join(",", due.Select(d => d.Name)), "only connected, low, visible devices");

        notified[BluetoothLogic.NotifyKey(due[0])] = today;
        string saved = BluetoothLogic.FormatNotified(notified, today);
        var again = BluetoothLogic.ParseNotified(saved);
        Expect.Equal(0, BluetoothLogic.DueForLowBattery(devices, 20, ["Hidden"], again, today).Count, "not again today");
        Expect.Equal(1, BluetoothLogic.DueForLowBattery(devices, 20, ["Hidden"], again, today.AddDays(1)).Count, "again tomorrow");
        Expect.Equal("", BluetoothLogic.FormatNotified(again, today.AddDays(1)), "old entries are dropped when saving");
        Expect.Equal(0, BluetoothLogic.ParseNotified("garbage\nno-date|nope\n|2026-01-01").Count, "bad lines ignored");
    }

    public static void NotifyKeyPrefersTheAddress()
    {
        Expect.Equal("AABBCCDDEEFF", BluetoothLogic.NotifyKey(Dev("A", address: "aa:bb:cc:dd:ee:ff")), "address");
        Expect.Equal("id-A", BluetoothLogic.NotifyKey(Dev("A")), "id when there is no address");
    }

    public static void SampleJsonParsesAndSurvivesGarbage()
    {
        var sample = BluetoothLogic.ParseSample(BluetoothLogic.DefaultSampleJson);
        Expect.True(sample is not null, "default sample parses");
        Expect.Equal(BtRadio.On, sample!.Radio, "radio on");
        Expect.True(sample.Devices.Count >= 6, "several devices");
        Expect.True(sample.Devices.Any(d => d.Connected) && sample.Devices.Any(d => !d.Connected), "both states");
        Expect.True(sample.Devices.Any(d => d.Battery is { } b && b < 20), "one low battery");
        Expect.True(sample.Devices.Select(d => d.Kind).Distinct().Count() >= 5, "varied kinds");
        Expect.True(sample.Devices.Select(d => d.Id).Distinct().Count() == sample.Devices.Count, "unique ids");

        Expect.Equal(BtRadio.Off, BluetoothLogic.ParseSample("""{"radio":"off"}""")!.Radio, "off");
        Expect.Equal(BtRadio.NoAdapter, BluetoothLogic.ParseSample("""{"radio":"none","devices":[]}""")!.Radio, "no adapter");
        Expect.Equal(BtRadio.Denied, BluetoothLogic.ParseSample("""{"radio":"denied"}""")!.Radio, "denied");
        var odd = BluetoothLogic.ParseSample("""{"devices":[{"name":"Odd","battery":500,"kind":"toaster"}, 5, {"name":"Pixel 7"}]}""")!;
        Expect.Equal(BtRadio.On, odd.Radio, "radio defaults to on");
        Expect.Equal(2, odd.Devices.Count, "non-objects skipped");
        Expect.True(odd.Devices[0].Battery is null, "out-of-range battery dropped");
        Expect.Equal(BtKind.Generic, odd.Devices[0].Kind, "unknown kind is generic");
        Expect.Equal(BtKind.Phone, odd.Devices[1].Kind, "kind guessed from the name");

        Expect.True(BluetoothLogic.ParseSample("not json") is null, "garbage");
        Expect.True(BluetoothLogic.ParseSample("") is null, "empty");
        Expect.True(BluetoothLogic.ParseSample("[1,2]") is null, "array");
        Expect.True(BluetoothLogic.ParseSample(null) is null, "null");
    }
}
