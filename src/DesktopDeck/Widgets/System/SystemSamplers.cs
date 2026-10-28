using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;

namespace DesktopDeck.Widgets;

/// <summary>
/// Read-only system sampling for the System widgets (P/Invoke and BCL). Samplers are shared between copies of the
/// same widget (deck layers, floating window) through <see cref="WidgetContext.Cache"/>, keyed by item id and
/// interval, so each reading happens once however many copies are on screen.
/// </summary>
internal static class SystemSamplers
{
    /// <summary>The shared sampler for <paramref name="key"/>, created on first use.</summary>
    public static T Shared<T>(string key, Func<T> create) where T : class
        => WidgetContext.Cache.GetOrAdd("system-widgets/" + key, _ => create()) as T ?? create();

    // ---------- Native ----------

    internal static class Native
    {
        [StructLayout(LayoutKind.Sequential)]
        internal struct MemoryStatusEx
        {
            public uint Length, MemoryLoad;
            public ulong TotalPhys, AvailPhys, TotalPageFile, AvailPageFile, TotalVirtual, AvailVirtual, AvailExtendedVirtual;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct PowerStatus
        {
            public byte ACLineStatus, BatteryFlag, BatteryLifePercent, SystemStatusFlag;
            public int BatteryLifeTime, BatteryFullLifeTime;
        }

        [DllImport("kernel32.dll", SetLastError = true)][return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetSystemTimes(out long idle, out long kernel, out long user);
        [DllImport("kernel32.dll", SetLastError = true)][return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx status);
        [DllImport("kernel32.dll", SetLastError = true)][return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetSystemPowerStatus(out PowerStatus status);
        [DllImport("powrprof.dll")]
        internal static extern uint PowerGetActiveScheme(nint rootPowerKey, out nint activeScheme);
        [DllImport("powrprof.dll")]
        internal static extern uint PowerReadFriendlyName(nint rootPowerKey, nint schemeGuid, nint subGroup, nint setting, nint buffer, ref uint bufferSize);
        [DllImport("kernel32.dll")]
        internal static extern nint LocalFree(nint memory);
        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern nint OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, int processId);
        [DllImport("kernel32.dll")][return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool CloseHandle(nint handle);
        [DllImport("kernel32.dll", SetLastError = true)][return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetProcessTimes(nint process, out long creation, out long exit, out long kernel, out long user);
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)][return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool QueryFullProcessImageName(nint process, int flags, StringBuilder name, ref int size);
        [DllImport("kernel32.dll", SetLastError = true)][return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetLogicalProcessorInformationEx(int relationship, nint buffer, ref uint length);

        internal const uint QueryLimitedInformation = 0x1000;

        internal static MemoryStatusEx Memory()
        {
            var status = new MemoryStatusEx { Length = (uint)Marshal.SizeOf<MemoryStatusEx>() };
            return GlobalMemoryStatusEx(ref status) ? status : default;
        }

        /// <summary>The active power plan's name ("Balanced"), or "" if Windows won't say.</summary>
        internal static string PowerPlan()
        {
            try
            {
                if (PowerGetActiveScheme(0, out nint scheme) != 0 || scheme == 0) return "";
                try
                {
                    uint size = 0;
                    if (PowerReadFriendlyName(0, scheme, 0, 0, 0, ref size) != 0 || size == 0 || size > 4096) return "";
                    nint buffer = Marshal.AllocHGlobal((int)size);
                    try { return PowerReadFriendlyName(0, scheme, 0, 0, buffer, ref size) == 0 ? Marshal.PtrToStringUni(buffer) ?? "" : ""; }
                    finally { Marshal.FreeHGlobal(buffer); }
                }
                finally { LocalFree(scheme); }
            }
            catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException) { return ""; }
        }

        /// <summary>Physical core count (0 if unknown).</summary>
        internal static int PhysicalCores()
        {
            try
            {
                uint length = 0;
                GetLogicalProcessorInformationEx(0, 0, ref length);
                if (length == 0 || length > 1 << 20) return 0;
                nint buffer = Marshal.AllocHGlobal((int)length);
                try
                {
                    if (!GetLogicalProcessorInformationEx(0, buffer, ref length)) return 0;
                    int cores = 0;
                    for (uint offset = 0; offset + 8 <= length;)
                    {
                        int relationship = Marshal.ReadInt32(buffer, (int)offset);
                        int size = Marshal.ReadInt32(buffer, (int)offset + 4);
                        if (size <= 0) break;
                        if (relationship == 0) cores++;
                        offset += (uint)size;
                    }
                    return cores;
                }
                finally { Marshal.FreeHGlobal(buffer); }
            }
            catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException) { return 0; }
        }
    }

    // ---------- CPU and memory ----------

    /// <summary>CPU busy percent and memory use, sampled at most once per interval for every copy of a widget.</summary>
    internal sealed class CpuMemory
    {
        private long idle, kernel, user;
        private DateTimeOffset last;
        public CpuMemory(double interval)
        {
            Interval = interval;
            Native.GetSystemTimes(out idle, out kernel, out user);
            last = DateTimeOffset.Now;
            ReadMemory();
        }

        public double Interval { get; }
        /// <summary>Goes up with every new reading; widget copies redraw when it changes.</summary>
        public int Version { get; private set; }
        public bool HasCpu { get; private set; }
        public double Cpu { get; private set; }
        public List<double> CpuHistory { get; } = new();
        public List<double> RamHistory { get; } = new();
        public List<double> CommitHistory { get; } = new();
        public double RamPercent { get; private set; }
        public double RamUsed { get; private set; }
        public double RamTotal { get; private set; }
        public double CommitPercent { get; private set; }
        public double CommitUsed { get; private set; }
        public double CommitTotal { get; private set; }

        private void ReadMemory()
        {
            var m = Native.Memory();
            RamTotal = m.TotalPhys;
            RamUsed = m.TotalPhys - m.AvailPhys;
            RamPercent = m.TotalPhys == 0 ? 0 : RamUsed / RamTotal * 100;
            CommitTotal = m.TotalPageFile;
            CommitUsed = m.TotalPageFile - m.AvailPageFile;
            CommitPercent = m.TotalPageFile == 0 ? 0 : CommitUsed / CommitTotal * 100;
        }

        /// <summary>Takes a reading if one is due (the first CPU value comes after one second). True when it did.</summary>
        public bool Update(DateTimeOffset now, int historyLength)
        {
            double due = HasCpu ? Interval : 1;
            if ((now - last).TotalSeconds < due - 0.3) return false;
            if (!Native.GetSystemTimes(out long i, out long k, out long u)) return false;
            Cpu = SystemStatsLogic.CpuPercent(idle, kernel, user, i, k, u);
            (idle, kernel, user, last) = (i, k, u, now);
            HasCpu = true;
            ReadMemory();
            SystemStatsLogic.Push(CpuHistory, Cpu, historyLength);
            SystemStatsLogic.Push(RamHistory, RamPercent, historyLength);
            SystemStatsLogic.Push(CommitHistory, CommitPercent, historyLength);
            Version++;
            return true;
        }
    }

    // ---------- Network ----------

    /// <summary>Download and upload speed summed over the chosen adapters (deltas per adapter, so adapters coming and going don't spike).</summary>
    internal sealed class Network
    {
        private readonly string filter;
        private readonly bool includeVirtual;
        private NetworkInterface[] adapters = [];
        private DateTimeOffset adaptersAt, last;
        private readonly Dictionary<string, (long Rx, long Tx)> counters = new();

        public Network(string filter, bool includeVirtual, double interval)
        {
            this.filter = filter;
            this.includeVirtual = includeVirtual;
            Interval = interval;
            RefreshAdapters(DateTimeOffset.Now);
            Read(out _, out _);
            last = DateTimeOffset.Now;
        }

        public double Interval { get; }
        public int Version { get; private set; }
        public bool HasRates { get; private set; }
        public double Down { get; private set; }
        public double Up { get; private set; }
        public List<double> DownHistory { get; } = new();
        public List<double> UpHistory { get; } = new();
        public long TotalDown { get; private set; }
        public long TotalUp { get; private set; }
        public string Adapter { get; private set; } = "";
        public string AdapterDetail { get; private set; } = "";
        public string Ip { get; private set; } = "";
        public bool Wireless { get; private set; }
        public int AdapterCount => adapters.Length;

        /// <summary>Names of the adapters that are up (for the right-click picker).</summary>
        public static List<string> UpAdapterNames(bool includeVirtual)
        {
            try
            {
                return NetworkInterface.GetAllNetworkInterfaces()
                    .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType is not (NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)
                        && (includeVirtual || !NetworkSpeedLogic.IsVirtual(n.Name, n.Description)))
                    .Select(n => n.Name).Distinct().OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase).ToList();
            }
            catch (NetworkInformationException) { return []; }
        }

        private void RefreshAdapters(DateTimeOffset now)
        {
            adaptersAt = now;
            try
            {
                adapters = NetworkInterface.GetAllNetworkInterfaces()
                    .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType is not (NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)
                        && (includeVirtual || !NetworkSpeedLogic.IsVirtual(n.Name, n.Description))
                        && NetworkSpeedLogic.Matches(n.Name, n.Description, filter))
                    .ToArray();
            }
            catch (NetworkInformationException) { adapters = []; }
            // The adapter to name: the one with a gateway (the route to the internet), else the first.
            NetworkInterface? main = null;
            string ip = "";
            foreach (var adapter in adapters)
            {
                try
                {
                    var props = adapter.GetIPProperties();
                    string? v4 = props.UnicastAddresses.FirstOrDefault(a => a.Address.AddressFamily == AddressFamily.InterNetwork)?.Address.ToString();
                    bool gateway = props.GatewayAddresses.Any(g => g.Address.AddressFamily == AddressFamily.InterNetwork && !g.Address.Equals(System.Net.IPAddress.Any));
                    if (main is null || (gateway && ip.Length == 0)) { main = adapter; ip = gateway ? v4 ?? "" : ""; }
                    if (gateway && v4 is not null) { main = adapter; ip = v4; break; }
                }
                catch (NetworkInformationException) { }
            }
            Adapter = main?.Name ?? (adapters.Length == 0 ? "No connection" : "");
            AdapterDetail = main?.Description ?? "";
            Ip = ip.Length > 0 ? ip : main is null ? "" : FirstIp(main);
            Wireless = main?.NetworkInterfaceType == NetworkInterfaceType.Wireless80211;
        }

        private static string FirstIp(NetworkInterface adapter)
        {
            try { return adapter.GetIPProperties().UnicastAddresses.FirstOrDefault(a => a.Address.AddressFamily == AddressFamily.InterNetwork)?.Address.ToString() ?? ""; }
            catch (NetworkInformationException) { return ""; }
        }

        /// <summary>Reads every adapter's counters; returns the bytes moved since the last read (adapters seen both times only).</summary>
        private void Read(out long rx, out long tx)
        {
            rx = tx = 0;
            var seen = new HashSet<string>();
            foreach (var adapter in adapters)
            {
                try
                {
                    var stats = adapter.GetIPStatistics();
                    long r = stats.BytesReceived, t = stats.BytesSent;
                    seen.Add(adapter.Id);
                    if (counters.TryGetValue(adapter.Id, out var before) && r >= before.Rx && t >= before.Tx) { rx += r - before.Rx; tx += t - before.Tx; }
                    counters[adapter.Id] = (r, t);
                }
                catch (Exception ex) when (ex is NetworkInformationException or PlatformNotSupportedException) { }
            }
            foreach (var gone in counters.Keys.Where(k => !seen.Contains(k)).ToList()) counters.Remove(gone);
        }

        public bool Update(DateTimeOffset now, int historyLength)
        {
            double seconds = (now - last).TotalSeconds;
            if (seconds < (HasRates ? Interval : 1) - 0.3) return false;
            if ((now - adaptersAt).TotalSeconds >= 20) RefreshAdapters(now);
            Read(out long rx, out long tx);
            last = now;
            Down = rx / seconds;
            Up = tx / seconds;
            TotalDown += rx;
            TotalUp += tx;
            HasRates = true;
            SystemStatsLogic.Push(DownHistory, Down, historyLength);
            SystemStatsLogic.Push(UpHistory, Up, historyLength);
            Version++;
            return true;
        }
    }

    // ---------- Drives ----------

    /// <summary>The latest drive list (read off the UI thread: network drives can be slow).</summary>
    internal sealed class Drives
    {
        public List<DiskSpaceLogic.Drive>? List { get; set; }
        public DateTimeOffset Read { get; set; }
        public int Version { get; set; }

        public static List<DiskSpaceLogic.Drive> ReadAll(bool removable, bool network)
        {
            var result = new List<DiskSpaceLogic.Drive>();
            foreach (var drive in DriveInfo.GetDrives())
            {
                string kind = drive.DriveType switch { DriveType.Fixed => "fixed", DriveType.Removable => "removable", DriveType.Network => "network", _ => "" };
                if (kind.Length == 0 || (kind == "removable" && !removable) || (kind == "network" && !network)) continue;
                try
                {
                    if (!drive.IsReady) continue;
                    result.Add(new DiskSpaceLogic.Drive(DiskSpaceLogic.NormalizeName(drive.Name), drive.VolumeLabel ?? "", kind, drive.TotalSize, drive.AvailableFreeSpace));
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException) { }
            }
            return result;
        }
    }

    // ---------- Battery ----------

    internal sealed class Power
    {
        public BatteryLogic.Reading? Reading { get; set; }
        public DateTimeOffset Read { get; set; }
        public int Version { get; set; }
        public int LastPercent { get; set; } = 101;

        /// <summary>One reading: GetSystemPowerStatus, the power plan, and (when asked) the battery report for charge rate and health.</summary>
        public static BatteryLogic.Reading ReadNow(bool report)
        {
            if (!Native.GetSystemPowerStatus(out var s)) return new BatteryLogic.Reading(false, false, false, -1, -1, false, "Power status unavailable");
            bool hasBattery = s.BatteryFlag != 128 && s.BatteryFlag != 255 && s.BatteryLifePercent != 255;
            bool plugged = s.ACLineStatus == 1;
            bool charging = hasBattery && (s.BatteryFlag & 8) != 0;
            int percent = s.BatteryLifePercent == 255 ? -1 : Math.Min(100, (int)s.BatteryLifePercent);
            double? toFull = null, health = null, watts = null;
            if (report && hasBattery)
            {
                try
                {
                    var r = global::Windows.Devices.Power.Battery.AggregateBattery.GetReport();
                    int? rate = r.ChargeRateInMilliwatts, remaining = r.RemainingCapacityInMilliwattHours, full = r.FullChargeCapacityInMilliwattHours, design = r.DesignCapacityInMilliwattHours;
                    if (charging) toFull = BatteryLogic.TimeToFull(remaining, full, rate);
                    health = BatteryLogic.Health(full, design);
                    if (rate is { } mw && mw != 0) watts = mw / 1000.0;
                }
                catch (Exception ex) when (ex is COMException or InvalidCastException or UnauthorizedAccessException or TypeLoadException or PlatformNotSupportedException) { }
            }
            return new BatteryLogic.Reading(hasBattery, plugged, charging, percent, s.BatteryLifeTime, s.SystemStatusFlag == 1, Native.PowerPlan(), toFull, health, watts);
        }
    }

    // ---------- Processes ----------

    /// <summary>Process memory and CPU (between two samples), with exe paths and descriptions cached per process.</summary>
    internal sealed class Processes
    {
        private Dictionary<int, (long Created, long Cpu)> cpuBefore = new();
        private DateTime readAt;
        private readonly Dictionary<(int, long), string?> paths = new();
        private readonly Dictionary<string, string?> descriptions = new(StringComparer.OrdinalIgnoreCase);

        public List<TopAppsLogic.App>? Apps { get; set; }
        public Dictionary<string, string?> Descriptions => descriptions;
        public DateTimeOffset Read { get; set; }
        public int Version { get; set; }
        public double RamUsed { get; set; }
        public double RamTotal { get; set; }

        /// <summary>A fresh reading; CPU is -1 until there is an earlier reading to compare with. Runs off the UI thread.</summary>
        public List<TopAppsLogic.Proc> Sample(bool privateMemory, string cpuScale, bool friendly)
        {
            var now = DateTime.UtcNow;
            double wall = readAt == default ? 0 : (now - readAt).TotalSeconds;
            var cpuNow = new Dictionary<int, (long Created, long Cpu)>();
            var result = new List<TopAppsLogic.Proc>();
            var livePaths = new HashSet<(int, long)>();
            foreach (var process in Process.GetProcesses())
            {
                using (process)
                {
                    try
                    {
                        int pid = process.Id;
                        long memory = privateMemory ? process.PrivateMemorySize64 : process.WorkingSet64;
                        double cpu = -1;
                        string? path = null;
                        nint handle = pid == 0 ? 0 : Native.OpenProcess(Native.QueryLimitedInformation, false, pid);
                        if (handle != 0)
                        {
                            try
                            {
                                if (Native.GetProcessTimes(handle, out long created, out _, out long kernel, out long user))
                                {
                                    cpuNow[pid] = (created, kernel + user);
                                    if (wall > 0 && cpuBefore.TryGetValue(pid, out var before) && before.Created == created)
                                        cpu = TopAppsLogic.CpuPercent(before.Cpu, kernel + user, wall, Environment.ProcessorCount, cpuScale);
                                    var key = (pid, created);
                                    livePaths.Add(key);
                                    if (!paths.TryGetValue(key, out path))
                                    {
                                        var builder = new StringBuilder(1024);
                                        int size = builder.Capacity;
                                        path = Native.QueryFullProcessImageName(handle, 0, builder, ref size) ? builder.ToString(0, size) : null;
                                        paths[key] = path;
                                    }
                                }
                            }
                            finally { Native.CloseHandle(handle); }
                        }
                        string name = process.ProcessName;
                        if (friendly && path is not null && !descriptions.ContainsKey(name))
                        {
                            try { descriptions[name] = FileVersionInfo.GetVersionInfo(path).FileDescription; }
                            catch (Exception ex) when (ex is FileNotFoundException or IOException or UnauthorizedAccessException) { descriptions[name] = null; }
                        }
                        result.Add(new TopAppsLogic.Proc(name, pid, memory, cpu, path));
                    }
                    catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException) { }
                }
            }
            foreach (var stale in paths.Keys.Where(k => !livePaths.Contains(k)).ToList()) paths.Remove(stale);
            cpuBefore = cpuNow;
            readAt = now;
            var m = Native.Memory();
            RamTotal = m.TotalPhys;
            RamUsed = m.TotalPhys - m.AvailPhys;
            return result;
        }
    }

    // ---------- PC info ----------

    /// <summary>Facts that don't change while the app runs (re-read every few minutes for screens).</summary>
    internal sealed record PcFacts(string Computer, string User, string Windows, string Build, string Cpu, int Cores, int Threads, double Ram, int Screens, string Architecture, DateTimeOffset Read)
    {
        public static PcFacts ReadNow()
        {
            string product = "", display = "", cpu = "";
            int build = Environment.OSVersion.Version.Build, ubr = 0;
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
                product = key?.GetValue("ProductName") as string ?? "";
                display = key?.GetValue("DisplayVersion") as string ?? key?.GetValue("ReleaseId") as string ?? "";
                if (int.TryParse(key?.GetValue("CurrentBuild") as string, out int b)) build = b;
                if (key?.GetValue("UBR") is int u) ubr = u;
            }
            catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException) { }
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
                cpu = key?.GetValue("ProcessorNameString") as string ?? "";
            }
            catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException) { }
            int screens = 1;
            try { screens = Math.Max(1, System.Windows.Forms.Screen.AllScreens.Length); }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) { }
            return new PcFacts(Environment.MachineName, Environment.UserName, PcInfoLogic.WindowsVersion(product, display, build), PcInfoLogic.Build(build, ubr),
                cpu, Native.PhysicalCores(), Environment.ProcessorCount, Native.Memory().TotalPhys, screens,
                RuntimeInformation.OSArchitecture.ToString().ToLowerInvariant() switch { "x64" => "64-bit (x64)", "arm64" => "ARM64", "x86" => "32-bit (x86)", var a => a },
                DateTimeOffset.Now);
        }
    }
}

