using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Devices.Enumeration;
using Windows.Devices.Radios;
using Windows.Storage.Streams;

namespace DesktopDeck.Widgets;

/// <summary>
/// The Bluetooth radio and the paired devices, shared by every Bluetooth widget. Windows device watchers (classic and LE)
/// keep the list current without polling; they run only while at least one widget is subscribed and stop shortly after the
/// last one goes (a short grace period, because redrawing a widget replaces its card and briefly drops the count to zero).
/// Battery levels are not pushed by Windows, so they are read from the device nodes after the list settles, when a device
/// connects or disconnects, and every couple of minutes. The only write is <see cref="SetRadioAsync"/>, which the widget
/// calls when the user clicks the switch.
/// </summary>
internal sealed class BluetoothService
{
    public static BluetoothService Instance { get; } = new();

    /// <summary>What the widgets draw. Immutable; replaced as a whole.</summary>
    public sealed record Snapshot(BtRadio Radio, IReadOnlyList<BtDevice> Devices, bool Ready, long Version);

    private const string BatteryKey = "{104EA319-6EE2-4701-BD47-8DDBF425BBE5} 2";
    private const string ConnectedKey = "System.Devices.Aep.IsConnected";
    private const string AddressKey = "System.Devices.Aep.DeviceAddress";
    private const string ContainerKey = "System.Devices.Aep.ContainerId";
    private const string MajorKey = "System.Devices.Aep.Bluetooth.Cod.Major";
    private const string MinorKey = "System.Devices.Aep.Bluetooth.Cod.Minor";
    private const string AppearanceKey = "System.Devices.Aep.Bluetooth.Le.Appearance";
    private const string NameKey = "System.ItemNameDisplay";

    private static readonly string[] RequestedProperties = [ConnectedKey, "System.Devices.Aep.Bluetooth.Le.IsConnectable", AddressKey, ContainerKey, MajorKey, MinorKey, AppearanceKey, BatteryKey];
    private static readonly string[] MinimalProperties = [ConnectedKey, AddressKey, ContainerKey];

    private static readonly TimeSpan StopDelay = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan PublishDelay = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan BatteryEvery = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan GattEvery = TimeSpan.FromMinutes(10);

    private readonly object gate = new();
    private readonly List<Subscription> subscribers = [];
    private readonly Dictionary<string, DeviceInformation> found = new(StringComparer.Ordinal);
    private readonly Dictionary<string, bool> wasConnected = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (int? Level, DateTime At)> gatt = new(StringComparer.Ordinal);
    private Dictionary<Guid, int> batteries = [];
    private DeviceWatcher? classic, le;
    private bool classicDone, leDone;
    private Radio? radio;
    private BtRadio radioState = BtRadio.Unknown;
    private bool running;
    private int generation;
    private Timer? stopTimer, publishTimer, batteryTimer, batteryDebounce, restartTimer;
    private int batteryBusy, batteryAgain;
    private volatile Snapshot current = new(BtRadio.Unknown, [], false, 0);

    public Snapshot Current => current;

    // ---------- Subscribing ----------

    /// <summary>
    /// Starts watching (if nobody else is) and calls <paramref name="changed"/> on a background thread whenever the visible
    /// result changes. It is never called just because you subscribed: read <see cref="Current"/> yourself.
    /// Dispose to unsubscribe.
    /// </summary>
    public IDisposable Subscribe(Action changed)
    {
        var subscription = new Subscription(this, changed);
        lock (gate)
        {
            subscribers.Add(subscription);
            stopTimer?.Dispose();
            stopTimer = null;
            if (!running) StartLocked();
        }
        return subscription;
    }

    private void Unsubscribe(Subscription subscription)
    {
        lock (gate)
        {
            subscribers.Remove(subscription);
            if (subscribers.Count > 0 || !running || stopTimer is not null) return;
            int stopGeneration = generation;
            stopTimer = new Timer(_ =>
            {
                lock (gate)
                {
                    if (subscribers.Count > 0 || !running || stopGeneration != generation) return;
                    StopLocked();
                }
            }, null, StopDelay, Timeout.InfiniteTimeSpan);
        }
    }

    private sealed class Subscription(BluetoothService owner, Action changed) : IDisposable
    {
        private int disposed;
        public void Raise() { if (Volatile.Read(ref disposed) == 0) changed(); }
        public void Dispose() { if (Interlocked.Exchange(ref disposed, 1) == 0) owner.Unsubscribe(this); }
    }

    /// <summary>Reads everything again (for the refresh button).</summary>
    public void Refresh()
    {
        lock (gate)
        {
            if (!running) return;
            StopLocked();
            StartLocked();
        }
    }

    // ---------- Start and stop ----------

    private void StartLocked()
    {
        running = true;
        int mine = ++generation;
        found.Clear();
        wasConnected.Clear();
        classicDone = leDone = false;
        batteries = [];
        _ = Task.Run(() => StartAsync(mine));
    }

    private void StopLocked()
    {
        running = false;
        generation++;
        stopTimer?.Dispose(); stopTimer = null;
        publishTimer?.Dispose(); publishTimer = null;
        batteryTimer?.Dispose(); batteryTimer = null;
        batteryDebounce?.Dispose(); batteryDebounce = null;
        restartTimer?.Dispose(); restartTimer = null;
        StopWatcher(ref classic);
        StopWatcher(ref le);
        if (radio is not null)
        {
            radio.StateChanged -= OnRadioChanged;
            radio = null;
        }
        found.Clear();
        radioState = BtRadio.Unknown;
        current = new Snapshot(BtRadio.Unknown, [], false, current.Version + 1);
    }

    private static void StopWatcher(ref DeviceWatcher? watcher)
    {
        var w = watcher;
        watcher = null;
        if (w is null) return;
        try
        {
            if (w.Status is DeviceWatcherStatus.Started or DeviceWatcherStatus.EnumerationCompleted) w.Stop();
        }
        catch (Exception ex) { App.Log(ex); }
    }

    private async Task StartAsync(int mine)
    {
        try
        {
            var access = await Radio.RequestAccessAsync();
            if (access != RadioAccessStatus.Allowed)
            {
                SetRadioState(mine, BtRadio.Denied, ready: true);
                return;
            }
            var radios = await Radio.GetRadiosAsync();
            var bluetooth = radios.FirstOrDefault(r => r.Kind == RadioKind.Bluetooth);
            if (bluetooth is null)
            {
                SetRadioState(mine, BtRadio.NoAdapter, ready: true);
                return;
            }
            lock (gate)
            {
                if (mine != generation || !running) return;
                radio = bluetooth;
                bluetooth.StateChanged += OnRadioChanged;
                radioState = Map(bluetooth.State);
                classic = CreateWatcher(BluetoothDevice.GetDeviceSelectorFromPairingState(true), mine, isLe: false);
                le = CreateWatcher(BluetoothLEDevice.GetDeviceSelectorFromPairingState(true), mine, isLe: true);
                batteryTimer = new Timer(_ => ScheduleBatteries(mine, TimeSpan.Zero), null, BatteryEvery, BatteryEvery);
                if (classic is null && le is null) { classicDone = leDone = true; }
            }
            MarkChanged(mine, immediate: true);
        }
        catch (Exception ex)
        {
            App.Log(ex);
            SetRadioState(mine, BtRadio.NoAdapter, ready: true);
        }
    }

    private static BtRadio Map(RadioState state) => state switch
    {
        RadioState.On => BtRadio.On,
        RadioState.Off => BtRadio.Off,
        RadioState.Disabled => BtRadio.Disabled,
        _ => BtRadio.Unknown,
    };

    private void SetRadioState(int mine, BtRadio state, bool ready)
    {
        lock (gate)
        {
            if (mine != generation || !running) return;
            radioState = state;
            if (ready) classicDone = leDone = true;
        }
        MarkChanged(mine, immediate: true);
    }

    private void OnRadioChanged(Radio sender, object args)
    {
        int mine;
        lock (gate)
        {
            if (!running) return;
            mine = generation;
            radioState = Map(sender.State);
        }
        MarkChanged(mine, immediate: true);
        ScheduleBatteries(mine, TimeSpan.FromSeconds(2));
    }

    // ---------- Switching the radio (only ever from the user's click) ----------

    /// <summary>Turns the Bluetooth radio on or off. Returns null on success, otherwise a short message for the user.</summary>
    public async Task<string?> SetRadioAsync(bool on)
    {
        Radio? target;
        lock (gate) target = radio;
        if (target is null) return "No Bluetooth adapter is available.";
        try
        {
            var status = await target.SetStateAsync(on ? RadioState.On : RadioState.Off);
            if (status != RadioAccessStatus.Allowed) return "Windows didn't allow changing Bluetooth. Use Bluetooth settings instead.";
            int mine;
            lock (gate)
            {
                if (!running) return null;
                mine = generation;
                radioState = Map(target.State);
            }
            MarkChanged(mine, immediate: true);
            return null;
        }
        catch (Exception ex)
        {
            App.Log(ex);
            return "Couldn't change Bluetooth: " + ex.Message;
        }
    }

    // ---------- Watchers ----------

    private DeviceWatcher? CreateWatcher(string selector, int mine, bool isLe)
    {
        try
        {
            DeviceWatcher watcher;
            try { watcher = DeviceInformation.CreateWatcher(selector, RequestedProperties, DeviceInformationKind.AssociationEndpoint); }
            catch (Exception) { watcher = DeviceInformation.CreateWatcher(selector, MinimalProperties, DeviceInformationKind.AssociationEndpoint); }
            watcher.Added += (_, info) => OnAdded(mine, info);
            watcher.Updated += (_, update) => OnUpdated(mine, update);
            watcher.Removed += (_, update) => OnRemoved(mine, update);
            watcher.EnumerationCompleted += (_, _) => OnEnumerated(mine, isLe);
            watcher.Stopped += (sender, _) => OnStopped(mine, sender, isLe);
            watcher.Start();
            return watcher;
        }
        catch (Exception ex)
        {
            App.Log(ex);
            return null;
        }
    }

    private void OnAdded(int mine, DeviceInformation info)
    {
        lock (gate)
        {
            if (mine != generation || !running) return;
            found[info.Id] = info;
            wasConnected[info.Id] = IsConnected(info);
        }
        MarkChanged(mine);
    }

    private void OnUpdated(int mine, DeviceInformationUpdate update)
    {
        bool connectionChanged = false;
        lock (gate)
        {
            if (mine != generation || !running || !found.TryGetValue(update.Id, out var info)) return;
            try { info.Update(update); }
            catch (Exception ex) { App.Log(ex); return; }
            bool now = IsConnected(info);
            connectionChanged = wasConnected.TryGetValue(update.Id, out bool before) && before != now;
            wasConnected[update.Id] = now;
        }
        MarkChanged(mine);
        if (connectionChanged) ScheduleBatteries(mine, TimeSpan.FromSeconds(2));
    }

    private void OnRemoved(int mine, DeviceInformationUpdate update)
    {
        lock (gate)
        {
            if (mine != generation || !running) return;
            found.Remove(update.Id);
            wasConnected.Remove(update.Id);
            gatt.Remove(update.Id);
        }
        MarkChanged(mine);
    }

    private void OnEnumerated(int mine, bool isLe)
    {
        lock (gate)
        {
            if (mine != generation || !running) return;
            if (isLe) leDone = true; else classicDone = true;
        }
        MarkChanged(mine, immediate: true);
        ScheduleBatteries(mine, TimeSpan.Zero);
    }

    private void OnStopped(int mine, DeviceWatcher sender, bool isLe)
    {
        // A watcher that Windows aborted (Bluetooth stack restarted, say) has to be started again from scratch.
        lock (gate)
        {
            if (mine != generation || !running || sender.Status != DeviceWatcherStatus.Aborted) return;
            restartTimer?.Dispose();
            restartTimer = new Timer(_ =>
            {
                lock (gate)
                {
                    if (mine != generation || !running) return;
                    StopLocked();
                    StartLocked();
                }
            }, null, TimeSpan.FromSeconds(3), Timeout.InfiniteTimeSpan);
        }
    }

    private static bool IsConnected(DeviceInformation info) => info.Properties.TryGetValue(ConnectedKey, out var v) && v is true;

    // ---------- Publishing ----------

    private void MarkChanged(int mine, bool immediate = false)
    {
        lock (gate)
        {
            if (mine != generation || !running) return;
            publishTimer ??= new Timer(_ => Publish(mine), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            publishTimer.Change(immediate ? TimeSpan.FromMilliseconds(30) : PublishDelay, Timeout.InfiniteTimeSpan);
        }
    }

    private void Publish(int mine)
    {
        List<Subscription> toTell;
        lock (gate)
        {
            if (mine != generation || !running) return;
            bool ready = classicDone && leDone && radioState != BtRadio.Unknown;
            var devices = BuildDevices();
            var old = current;
            if (old.Radio == radioState && old.Ready == ready && old.Devices.SequenceEqual(devices)) return;
            current = new Snapshot(radioState, devices, ready, old.Version + 1);
            toTell = [.. subscribers];
        }
        foreach (var subscriber in toTell)
        {
            try { subscriber.Raise(); }
            catch (Exception ex) { App.Log(ex); }
        }
    }

    private List<BtDevice> BuildDevices()
    {
        var list = new List<BtDevice>(found.Count);
        foreach (var (id, info) in found)
        {
            var p = info.Properties;
            string address = p.TryGetValue(AddressKey, out var a) && a is string text && text.Length > 0 ? BluetoothLogic.FormatAddress(text) : BluetoothLogic.AddressFromId(id);
            Guid container = p.TryGetValue(ContainerKey, out var c) && c is Guid g ? g : Guid.Empty;
            int? major = Number(p, MajorKey), minor = Number(p, MinorKey), appearance = Number(p, AppearanceKey);
            bool isLe = appearance is not null || id.Contains("BluetoothLE", StringComparison.OrdinalIgnoreCase);
            int? battery = container != Guid.Empty && batteries.TryGetValue(container, out int level) ? level : BluetoothLogic.ParseBattery(p.TryGetValue(BatteryKey, out var direct) ? direct : null);
            if (battery is null && gatt.TryGetValue(id, out var read)) battery = read.Level;
            string name = info.Name ?? "";
            if (name.Length == 0 && p.TryGetValue(NameKey, out var shown) && shown is string shownText) name = shownText;
            list.Add(new BtDevice(id, BluetoothLogic.DisplayName(name), IsConnected(info), address, BluetoothLogic.Resolve(major, minor, appearance, name), battery, isLe, container == Guid.Empty ? "" : container.ToString("D")));
        }
        return BluetoothLogic.Merge(list);
    }

    private static int? Number(IReadOnlyDictionary<string, object> properties, string key) =>
        properties.TryGetValue(key, out var value) && value is not null ? value switch
        {
            ushort u => u,
            int i => i,
            uint u => (int)u,
            short s => s,
            byte b => b,
            _ => null,
        } : null;

    // ---------- Battery levels ----------

    private void ScheduleBatteries(int mine, TimeSpan after)
    {
        lock (gate)
        {
            if (mine != generation || !running) return;
            batteryDebounce?.Dispose();
            batteryDebounce = new Timer(_ => _ = ReadBatteriesAsync(mine), null, after, Timeout.InfiniteTimeSpan);
        }
    }

    /// <summary>
    /// Windows keeps a battery level on the device nodes of a Bluetooth device (they share its container id) rather than on the
    /// paired entry, so one query of the present device nodes covers every device. Connected LE devices without one get a single
    /// read of the GATT Battery Service (cached value, short timeout, nothing is written or paired).
    /// </summary>
    private async Task ReadBatteriesAsync(int mine)
    {
        // A read that is already running may have started before the change that asked for this one: run once more when it ends.
        if (Interlocked.Exchange(ref batteryBusy, 1) == 1) { Volatile.Write(ref batteryAgain, 1); return; }
        try
        {
            lock (gate) { if (mine != generation || !running || radioState != BtRadio.On) return; }
            var nodes = await DeviceInformation.FindAllAsync("System.Devices.Present:=System.StructuredQueryType.Boolean#True", [BatteryKey, "System.Devices.ContainerId", "System.Devices.Connected"], DeviceInformationKind.Device).AsTask().WaitAsync(TimeSpan.FromSeconds(10));
            var byContainer = new Dictionary<Guid, List<(int Level, bool Connected)>>();
            foreach (var node in nodes)
            {
                if (BluetoothLogic.ParseBattery(node.Properties.TryGetValue(BatteryKey, out var raw) ? raw : null) is not { } level) continue;
                if (!(node.Properties.TryGetValue("System.Devices.ContainerId", out var c) && c is Guid container) || container == Guid.Empty) continue;
                bool connected = node.Properties.TryGetValue("System.Devices.Connected", out var cn) && cn is true;
                if (!byContainer.TryGetValue(container, out var list)) byContainer[container] = list = [];
                list.Add((level, connected));
            }
            var result = new Dictionary<Guid, int>();
            foreach (var (container, list) in byContainer)
            {
                // Prefer nodes that are connected now; one device can have several (left and right earbud): show the lowest.
                var preferred = list.Where(x => x.Connected).ToList();
                result[container] = BluetoothLogic.LowestBattery((preferred.Count > 0 ? preferred : list).Select(x => (int?)x.Level)) ?? 0;
            }
            List<DeviceInformation> needGatt;
            lock (gate)
            {
                if (mine != generation || !running) return;
                batteries = result;
                needGatt = found.Values.Where(info => IsConnected(info) && info.Properties.TryGetValue(AppearanceKey, out var appearance) && appearance is not null && NeedsGatt(info, result)).ToList();
            }
            MarkChanged(mine);
            foreach (var info in needGatt)
            {
                int? level = await ReadGattBatteryAsync(info.Id);
                lock (gate)
                {
                    if (mine != generation || !running) return;
                    gatt[info.Id] = (level, DateTime.UtcNow);
                }
                if (level is not null) MarkChanged(mine);
            }
        }
        catch (Exception ex) { App.Log(ex); }
        finally
        {
            bool again = Interlocked.Exchange(ref batteryAgain, 0) == 1;
            Interlocked.Exchange(ref batteryBusy, 0);
            if (again) ScheduleBatteries(mine, TimeSpan.FromMilliseconds(500));
        }
    }

    private bool NeedsGatt(DeviceInformation info, Dictionary<Guid, int> byContainer)
    {
        if (info.Properties.TryGetValue(ContainerKey, out var c) && c is Guid g && byContainer.ContainsKey(g)) return false;
        return !gatt.TryGetValue(info.Id, out var last) || DateTime.UtcNow - last.At > GattEvery;
    }

    private static async Task<int?> ReadGattBatteryAsync(string deviceId)
    {
        BluetoothLEDevice? device = null;
        GattDeviceService? service = null;
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            device = await BluetoothLEDevice.FromIdAsync(deviceId).AsTask(timeout.Token);
            if (device is null || device.ConnectionStatus != BluetoothConnectionStatus.Connected) return null;
            var services = await device.GetGattServicesForUuidAsync(GattServiceUuids.Battery, BluetoothCacheMode.Cached).AsTask(timeout.Token);
            service = services.Status == GattCommunicationStatus.Success ? services.Services.FirstOrDefault() : null;
            if (service is null) return null;
            var characteristics = await service.GetCharacteristicsForUuidAsync(GattCharacteristicUuids.BatteryLevel, BluetoothCacheMode.Cached).AsTask(timeout.Token);
            var characteristic = characteristics.Status == GattCommunicationStatus.Success ? characteristics.Characteristics.FirstOrDefault() : null;
            if (characteristic is null) return null;
            var value = await characteristic.ReadValueAsync(BluetoothCacheMode.Cached).AsTask(timeout.Token);
            if (value.Status != GattCommunicationStatus.Success || value.Value.Length < 1) return null;
            return BluetoothLogic.ParseBattery(DataReader.FromBuffer(value.Value).ReadByte());
        }
        catch (Exception) { return null; }
        finally
        {
            try { service?.Dispose(); device?.Dispose(); }
            catch (Exception) { }
        }
    }
}
