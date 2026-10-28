using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using DesktopDeck.Models;

namespace DesktopDeck.Services;

/// <summary>Owns jobs created by this instance. Shell-opened shortcuts are deliberately untracked.</summary>
public sealed class LauncherService : IDisposable
{
    private readonly object _gate = new();
    private readonly Dictionary<string, OwnedJob> _jobs = new(StringComparer.Ordinal);
    private readonly Dictionary<nuint, OwnedJob> _completionJobs = new();
    private readonly Dictionary<string, string> _statuses = new(StringComparer.Ordinal);
    private readonly SynchronizationContext? _context = SynchronizationContext.Current;
    private readonly nint _completionPort;
    private long _nextKey;
    private bool _disposed;

    public event EventHandler? StatusChanged;

    public LauncherService()
    {
        _completionPort = Native.CreateIoCompletionPort(new nint(-1), 0, 0, 1);
        if (_completionPort == 0) throw Error("Could not initialize process monitoring");
        new Thread(WatchCompletions) { IsBackground = true, Name = "DesktopDeck process monitor" }.Start();
    }

    public string GetStatus(string id)
    {
        lock (_gate) return _statuses.GetValueOrDefault(id, "Ready");
    }

    public void Launch(LauncherItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (string.IsNullOrWhiteSpace(item.Id)) throw new ArgumentException("The item needs an ID.");
        string target = Environment.ExpandEnvironmentVariables(item.Target.Trim());
        if (string.IsNullOrWhiteSpace(target)) throw new InvalidOperationException("Choose a program or script first.");
        if (!Path.IsPathFullyQualified(target)) throw new InvalidOperationException("Choose a full path to a program, script, or shortcut.");
        if (!File.Exists(target)) throw new FileNotFoundException("The launch target no longer exists.", target);
        string directory = string.IsNullOrWhiteSpace(item.WorkingDirectory) ? Path.GetDirectoryName(target)! : Environment.ExpandEnvironmentVariables(item.WorkingDirectory);
        if (!Directory.Exists(directory)) throw new DirectoryNotFoundException($"The working folder does not exist: {directory}");
        string extension = Path.GetExtension(target);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_jobs.ContainsKey(item.Id)) throw new InvalidOperationException("This item is already running.");
            if (extension.Equals(".exe", StringComparison.OrdinalIgnoreCase) || extension.Equals(".bat", StringComparison.OrdinalIgnoreCase) || extension.Equals(".cmd", StringComparison.OrdinalIgnoreCase))
                StartOwned(item, target, directory, !extension.Equals(".exe", StringComparison.OrdinalIgnoreCase));
            else
            {
                // ShellExecute can reuse an existing application. Its returned PID is not proof of ownership.
                using var opened = Process.Start(new ProcessStartInfo(target)
                {
                    UseShellExecute = true,
                    Arguments = item.Arguments,
                    WorkingDirectory = directory
                });
                _statuses[item.Id] = "Opened via Windows (not tracked)";
            }
        }
        Notify();
    }

    public void Stop(string id, bool force = false)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!_jobs.TryGetValue(id, out var owned)) throw new InvalidOperationException("This item has no tracked running process. Windows-opened shortcuts cannot be stopped safely here.");
            if (force)
            {
                if (!Native.TerminateJobObject(owned.Job, 1)) throw Error("Could not force-stop the owned process tree");
                owned.StopRequested = true;
                _statuses[id] = "Force stop requested";
            }
            else
            {
                int sent = 0;
                int lastError = 0;
                Native.EnumWindows((window, _) =>
                {
                    if (!Native.IsWindowVisible(window)) return true;
                    Native.GetWindowThreadProcessId(window, out uint pid);
                    nint process = Native.OpenProcess(0x1000, false, pid);
                    if (process == 0) return true;
                    try
                    {
                        if (Native.IsProcessInJob(process, owned.Job, out bool belongs) && belongs)
                        {
                            if (Native.PostMessage(window, 0x0010, 0, 0)) sent++;
                            else lastError = Marshal.GetLastWin32Error();
                        }
                    }
                    finally { Native.CloseHandle(process); }
                    return true;
                }, 0);
                if (sent == 0)
                {
                    if (lastError != 0) throw new Win32Exception(lastError, "The owned application did not accept a close request.");
                    throw new InvalidOperationException("This process has no window that can accept a close request. Use Force stop to terminate its owned process tree.");
                }
                owned.StopRequested = true;
                _statuses[id] = "Close requested (waiting)";
            }
        }
        Notify();
    }

    private void StartOwned(LauncherItem item, string target, string directory, bool batch)
    {
        nint job = Native.CreateJobObject(0, null);
        if (job == 0) throw Error("Could not create a process job");
        Native.ProcessInformation process = default;
        nuint key = (nuint)Interlocked.Increment(ref _nextKey);
        try
        {
            var association = new Native.CompletionAssociation { CompletionKey = (nint)key, CompletionPort = _completionPort };
            if (!Native.SetInformationJobObject(job, 7, ref association, (uint)Marshal.SizeOf<Native.CompletionAssociation>())) throw Error("Could not monitor the process job");
            string executable = batch ? Path.Combine(Environment.SystemDirectory, "cmd.exe") : target;
            string command = batch
                ? Quote(executable) + " /d /s /c \"" + Quote(target) + (string.IsNullOrWhiteSpace(item.Arguments) ? "" : " " + item.Arguments) + "\""
                : Quote(executable) + (string.IsNullOrWhiteSpace(item.Arguments) ? "" : " " + item.Arguments);
            var startup = new Native.StartupInfo { Size = (uint)Marshal.SizeOf<Native.StartupInfo>() };
            // Suspend first: even a very fast script cannot spawn an unowned child before assignment.
            uint flags = 0x00000004u | (batch ? 0x00000010u : 0u); // CREATE_SUSPENDED | CREATE_NEW_CONSOLE for scripts
            if (!Native.CreateProcess(executable, new StringBuilder(command), 0, 0, false, flags, 0, directory, ref startup, out process)) throw Error("Could not start the application");
            if (!Native.AssignProcessToJobObject(job, process.Process)) throw Error("Windows would not allow safe process-tree tracking");
            var owned = new OwnedJob(item.Id, key, job, process.Process);
            _jobs.Add(item.Id, owned);
            _completionJobs.Add(key, owned);
            if (Native.ResumeThread(process.Thread) == uint.MaxValue)
            {
                _jobs.Remove(item.Id);
                _completionJobs.Remove(key);
                throw Error("Could not resume the application");
            }
            _statuses[item.Id] = "Running";
            job = 0;
            process.Process = 0;
        }
        catch
        {
            // Only the newly created, suspended process can be present on this failure path.
            if (process.Process != 0) Native.TerminateProcess(process.Process, 1);
            throw;
        }
        finally
        {
            if (process.Thread != 0) Native.CloseHandle(process.Thread);
            if (process.Process != 0) Native.CloseHandle(process.Process);
            if (job != 0) Native.CloseHandle(job);
        }
    }

    private void WatchCompletions()
    {
        try
        {
            while (Native.GetQueuedCompletionStatus(_completionPort, out uint message, out nuint key, out _, uint.MaxValue))
            {
                if (key == 0) return;
                if (message != 4) continue; // JOB_OBJECT_MSG_ACTIVE_PROCESS_ZERO includes descendants.
                bool changed = false;
                lock (_gate)
                {
                    if (_disposed) return;
                    if (_completionJobs.Remove(key, out var owned))
                    {
                        _jobs.Remove(owned.Id);
                        string status = owned.StopRequested ? "Stopped" : "Exited";
                        if (!owned.StopRequested && Native.GetExitCodeProcess(owned.Process, out uint code) && code != 0) status += $" (code {code})";
                        _statuses[owned.Id] = status;
                        owned.Dispose();
                        changed = true;
                    }
                }
                if (changed) Notify();
            }
        }
        finally { Native.CloseHandle(_completionPort); }
    }

    private void Notify()
    {
        if (_context is not null) _context.Post(_ => { if (!_disposed) StatusChanged?.Invoke(this, EventArgs.Empty); }, null);
        else StatusChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            // No KILL_ON_JOB_CLOSE flag: closing the deck leaves the user's launched applications running.
            foreach (var owned in _jobs.Values) owned.Dispose();
            _jobs.Clear();
            _completionJobs.Clear();
            Native.PostQueuedCompletionStatus(_completionPort, 0, 0, 0);
        }
        GC.SuppressFinalize(this);
    }

    private static string Quote(string value) => "\"" + value + "\"";
    private static Win32Exception Error(string message) => new(Marshal.GetLastWin32Error(), message);

    private sealed class OwnedJob(string id, nuint key, nint job, nint process) : IDisposable
    {
        public string Id { get; } = id;
        public nuint Key { get; } = key;
        public nint Job { get; } = job;
        public nint Process { get; } = process;
        public bool StopRequested { get; set; }
        public void Dispose() { Native.CloseHandle(Process); Native.CloseHandle(Job); }
    }

    private static class Native
    {
        [StructLayout(LayoutKind.Sequential)] internal struct CompletionAssociation { public nint CompletionKey; public nint CompletionPort; }
        [StructLayout(LayoutKind.Sequential)] internal struct ProcessInformation { public nint Process; public nint Thread; public uint ProcessId; public uint ThreadId; }
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] internal struct StartupInfo
        {
            public uint Size; public string? Reserved; public string? Desktop; public string? Title;
            public uint X; public uint Y; public uint XSize; public uint YSize; public uint XCountChars; public uint YCountChars;
            public uint FillAttribute; public uint Flags; public ushort ShowWindow; public ushort ReservedSize;
            public nint ReservedPointer; public nint StandardInput; public nint StandardOutput; public nint StandardError;
        }
        internal delegate bool EnumWindowsCallback(nint window, nint parameter);
        [DllImport("kernel32.dll", SetLastError = true)] internal static extern nint CreateIoCompletionPort(nint file, nint existing, nuint key, uint concurrency);
        [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool GetQueuedCompletionStatus(nint port, out uint bytes, out nuint key, out nint overlapped, uint milliseconds);
        [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool PostQueuedCompletionStatus(nint port, uint bytes, nuint key, nint overlapped);
        [DllImport("kernel32.dll", EntryPoint = "CreateJobObjectW", CharSet = CharSet.Unicode, SetLastError = true)] internal static extern nint CreateJobObject(nint attributes, string? name);
        [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool SetInformationJobObject(nint job, int infoClass, ref CompletionAssociation information, uint length);
        [DllImport("kernel32.dll", EntryPoint = "CreateProcessW", CharSet = CharSet.Unicode, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool CreateProcess(string application, StringBuilder command, nint processAttributes, nint threadAttributes, [MarshalAs(UnmanagedType.Bool)] bool inheritHandles, uint flags, nint environment, string currentDirectory, ref StartupInfo startup, out ProcessInformation process);
        [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool AssignProcessToJobObject(nint job, nint process);
        [DllImport("kernel32.dll", SetLastError = true)] internal static extern uint ResumeThread(nint thread);
        [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool TerminateProcess(nint process, uint code);
        [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool TerminateJobObject(nint job, uint code);
        [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool CloseHandle(nint handle);
        [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool GetExitCodeProcess(nint process, out uint code);
        [DllImport("kernel32.dll", SetLastError = true)] internal static extern nint OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, uint pid);
        [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool IsProcessInJob(nint process, nint job, [MarshalAs(UnmanagedType.Bool)] out bool result);
        [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool EnumWindows(EnumWindowsCallback callback, nint parameter);
        [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool IsWindowVisible(nint window);
        [DllImport("user32.dll")] internal static extern uint GetWindowThreadProcessId(nint window, out uint pid);
        [DllImport("user32.dll", EntryPoint = "PostMessageW", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool PostMessage(nint window, uint message, nint wparam, nint lparam);
    }
}
