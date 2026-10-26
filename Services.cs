using Microsoft.Win32;
using System.Diagnostics;
using System.Management;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.IO;

namespace AppFlow;

public static class AppDiscovery
{
    public static List<AppEntry> Discover()
    {
        var found = new Dictionary<string, AppEntry>(StringComparer.OrdinalIgnoreCase);
        using var shortcutResolver = new ShortcutResolver();
        foreach (var root in new[] { Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu), Environment.GetFolderPath(Environment.SpecialFolder.StartMenu) })
        {
            if (!Directory.Exists(root)) continue;
            try
            {
                foreach (var file in Directory.EnumerateFiles(root, "*.lnk", SearchOption.AllDirectories))
                    try { var app = shortcutResolver.Resolve(file); if (app != null && IsLaunchable(app.Path)) found.TryAdd(Normalize(app.Path), app); } catch { }
            }
            catch { }
        }
        foreach (var hive in new[] { Registry.LocalMachine, Registry.CurrentUser })
            foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
                try
                {
                    using var baseKey = RegistryKey.OpenBaseKey(hive.Name == Registry.LocalMachine.Name ? RegistryHive.LocalMachine : RegistryHive.CurrentUser, view);
                    using var uninstall = baseKey.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall");
                    if (uninstall == null) continue;
                    foreach (var subName in uninstall.GetSubKeyNames())
                        try
                        {
                            using var sub = uninstall.OpenSubKey(subName);
                            var name = sub?.GetValue("DisplayName") as string;
                            var icon = sub?.GetValue("DisplayIcon") as string;
                            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(icon)) continue;
                            var path = icon.Trim().Trim('"').Split(',')[0];
                            if (IsLaunchable(path)) found.TryAdd(Normalize(path), new AppEntry { Name = name, Path = path, Source = "Installed app" });
                        }
                        catch { }
                }
                catch { }
        return found.Values.OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    public static AppEntry? ResolveShortcut(string shortcut)
    {
        using var resolver = new ShortcutResolver(); return resolver.Resolve(shortcut);
    }
    public static bool IsLaunchable(string path) => File.Exists(path) && (System.IO.Path.GetExtension(path).Equals(".exe", StringComparison.OrdinalIgnoreCase) || System.IO.Path.GetExtension(path).Equals(".com", StringComparison.OrdinalIgnoreCase));
    public static string Normalize(string path) { try { return System.IO.Path.GetFullPath(path).TrimEnd('\\'); } catch { return path; } }
    public static string ResolveTargetPath(string selected)
    {
        if (System.IO.Path.GetExtension(selected).Equals(".lnk", StringComparison.OrdinalIgnoreCase))
            return ResolveShortcut(selected)?.Path ?? throw new InvalidOperationException("That shortcut has no valid executable launch target.");
        if (!IsLaunchable(selected)) throw new InvalidOperationException("Choose an existing .exe or .com file, or a shortcut that points to one.");
        return selected;
    }
}

internal sealed class ShortcutResolver : IDisposable
{
    private readonly object shell;
    public ShortcutResolver()
    {
        var shellType = Type.GetTypeFromProgID("WScript.Shell") ?? throw new InvalidOperationException("Windows shortcut support is unavailable.");
        shell = Activator.CreateInstance(shellType)!;
    }
    public AppEntry? Resolve(string shortcut)
    {
        dynamic link = ((dynamic)shell).CreateShortcut(shortcut);
        try
        {
            string target = (string)link.TargetPath; string args = (string)link.Arguments;
            if (!string.IsNullOrWhiteSpace(target) && File.Exists(target))
                return new AppEntry { Name = System.IO.Path.GetFileNameWithoutExtension(shortcut), Path = target, Arguments = args, Source = "Start Menu" };
            return null;
        }
        finally { if (Marshal.IsComObject(link)) Marshal.FinalReleaseComObject(link); }
    }
    public void Dispose() { if (Marshal.IsComObject(shell)) Marshal.FinalReleaseComObject(shell); }
}

public sealed class ProcessMonitor : IDisposable
{
    private readonly Func<AppData> getData;
    private readonly HashSet<int> automationPids = [];
    private readonly Dictionary<int, List<AutomationProfile>> triggerPids = [];
    private readonly Dictionary<string, int> activeTriggerPaths = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Dictionary<string, List<int>>> ownedPids = [];
    private readonly HashSet<string> handledEvents = [];
    private ManagementEventWatcher? starts, stops;
    private Timer? fallbackTimer;
    private bool wmiActive;
    private Dictionary<int, string> fallbackSeen = [];
    private bool fallbackInitialized;
    private int polling;
    public event Action<string>? Status;
    public event Action<string>? Error;
    public bool IsPaused { get; private set; }
    public ProcessMonitor(Func<AppData> data) => getData = data;
    public void Start()
    {
        if (starts != null || fallbackTimer != null) return;
        try
        {
            starts = new(new WqlEventQuery("SELECT * FROM Win32_ProcessStartTrace"));
            stops = new(new WqlEventQuery("SELECT * FROM Win32_ProcessStopTrace"));
            starts.EventArrived += OnStarted; stops.EventArrived += OnStopped;
            starts.Start(); stops.Start(); wmiActive = true; Status?.Invoke("Monitoring");
        }
        catch
        {
            wmiActive = false;
            StopWatchers();
            Status?.Invoke("Monitoring (compatibility mode)");
        }
        // Keep the low-cost process snapshot as a backup even when WMI starts
        // successfully. Some Windows installations accept the WMI subscription
        // but do not reliably deliver process events to unelevated applications.
        fallbackTimer ??= new Timer(_ => PollProcesses(), null, TimeSpan.Zero, TimeSpan.FromSeconds(1));
    }
    public void Pause(bool pause) { IsPaused = pause; Status?.Invoke(pause ? "Paused" : wmiActive ? "Monitoring" : "Monitoring (compatibility mode)"); }
    private void OnStarted(object sender, EventArrivedEventArgs e)
    {
        try
        {
            var pid = Convert.ToInt32(e.NewEvent["ProcessID"]);
            var name = Convert.ToString(e.NewEvent["ProcessName"]) ?? "";
            string? exe = null;
            try { using var p = Process.GetProcessById(pid); exe = p.MainModule?.FileName; } catch { }
            HandleStarted(pid, name, exe);
        }
        catch { }
    }
    private void HandleStarted(int pid, string name, string? exe)
    {
        try
        {
            if (automationPids.Contains(pid) || IsPaused) return;
            var snapshot = getData();
            var candidates = snapshot.Profiles.Where(p => p.Enabled && snapshot.Applications.FirstOrDefault(a => a.Id == p.TriggerAppId) is { } t && System.IO.Path.GetFileName(t.Path).Equals(name, StringComparison.OrdinalIgnoreCase)).ToList();
            var matching = exe == null
                ? candidates.GroupBy(p => AppDiscovery.Normalize(snapshot.Applications.First(a => a.Id == p.TriggerAppId).Path), StringComparer.OrdinalIgnoreCase).Count() == 1 ? candidates : []
                : candidates.Where(p => AppDiscovery.Normalize(snapshot.Applications.First(a => a.Id == p.TriggerAppId).Path).Equals(AppDiscovery.Normalize(exe), StringComparison.OrdinalIgnoreCase)).ToList();
            if (matching.Count == 0) return;
            var triggerPath = AppDiscovery.Normalize(snapshot.Applications.First(a => a.Id == matching[0].TriggerAppId).Path);
            var key = $"{pid}:{triggerPath}";
            lock (handledEvents) { if (!handledEvents.Add(key)) return; if (handledEvents.Count > 2048) handledEvents.Clear(); }
            lock (activeTriggerPaths)
            {
                if (activeTriggerPaths.TryGetValue(triggerPath, out var activePid))
                {
                    try { using var active = Process.GetProcessById(activePid); if (!active.HasExited) return; } catch { }
                }
                activeTriggerPaths[triggerPath] = pid;
            }
            foreach (var profile in matching)
            {
                if (!triggerPids.TryGetValue(pid, out var matches)) triggerPids[pid] = matches = [];
                matches.Add(profile);
                _ = Task.Run(async () =>
                {
                    if (profile.TriggerOnWindow && !await WaitForWindow(pid)) return;
                    await ExecuteProfile(profile, pid);
                });
            }
        }
        catch { }
    }
    private async Task ExecuteProfile(AutomationProfile profile, int triggerPid)
    {
        var first = true;
        foreach (var id in profile.ActionAppIds)
        {
            if (!first && profile.DelayMs > 0) await Task.Delay(profile.DelayMs);
            first = false;
            var data = getData(); var app = data.Applications.FirstOrDefault(a => a.Id == id);
            if (app == null || !AppDiscovery.IsLaunchable(app.Path)) { Error?.Invoke($"{profile.Name}: {app?.Name ?? "An application"} is missing or no longer launchable."); continue; }
            if (!profile.RelaunchRunning && IsRunning(app.Path)) { Status?.Invoke($"Skipped already running: {app.Name}"); continue; }
            try
            {
                var process = Process.Start(new ProcessStartInfo { FileName = app.Path, Arguments = app.Arguments, UseShellExecute = true });
                if (process != null)
                {
                    automationPids.Add(process.Id);
                    lock (ownedPids)
                    {
                        if (!ownedPids.TryGetValue(profile.Id, out var apps)) ownedPids[profile.Id] = apps = [];
                        if (!apps.TryGetValue(id, out var pids)) apps[id] = pids = [];
                        pids.Add(process.Id);
                    }
                    process.Dispose();
                    Status?.Invoke($"Opened {app.Name}");
                }
            }
            catch (Exception ex) { Error?.Invoke($"Could not open {app.Name}: {ex.Message}"); }
        }
    }
    private void OnStopped(object sender, EventArrivedEventArgs e)
    {
        try { HandleStopped(Convert.ToInt32(e.NewEvent["ProcessID"])); } catch { }
    }
    private void HandleStopped(int pid)
    {
        automationPids.Remove(pid);
        lock (activeTriggerPaths) foreach (var pair in activeTriggerPaths.Where(x => x.Value == pid).ToArray()) activeTriggerPaths.Remove(pair.Key);
        if (triggerPids.Remove(pid, out var profiles)) foreach (var profile in profiles.Where(p => p.CloseAppIds.Count > 0)) _ = Task.Run(() => CloseProfileApps(profile));
    }
    private void PollProcesses()
    {
        if (Interlocked.Exchange(ref polling, 1) != 0) return;
        try
        {
            foreach (var pid in automationPids.ToArray())
                try { using var process = Process.GetProcessById(pid); if (process.HasExited) automationPids.Remove(pid); }
                catch { automationPids.Remove(pid); }
            var snapshot = getData();
            var names = snapshot.Profiles.Where(p => p.Enabled)
                .Select(p => snapshot.Applications.FirstOrDefault(a => a.Id == p.TriggerAppId)?.Path)
                .Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => System.IO.Path.GetFileName(p!))
                .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            if (names.Length == 0) { fallbackSeen.Clear(); fallbackInitialized = true; return; }
            var current = new Dictionary<int, string>();
            var namesSet = new HashSet<string>(names, StringComparer.OrdinalIgnoreCase);
            IntPtr snapshotHandle = CreateToolhelp32Snapshot(0x00000002, 0);
            if (snapshotHandle == IntPtr.Zero || snapshotHandle == new IntPtr(-1)) return;
            try
            {
                var entry = new ProcessEntry32 { Size = (uint)Marshal.SizeOf<ProcessEntry32>(), ExecutableFile = string.Empty };
                if (Process32First(snapshotHandle, ref entry))
                    do { if (namesSet.Contains(entry.ExecutableFile)) current[(int)entry.ProcessId] = entry.ExecutableFile; }
                    while (Process32Next(snapshotHandle, ref entry));
            }
            finally { CloseHandle(snapshotHandle); }
            if (!fallbackInitialized) { fallbackSeen = current; fallbackInitialized = true; return; }
            foreach (var pair in current) if (!fallbackSeen.ContainsKey(pair.Key)) HandleStarted(pair.Key, pair.Value, GetProcessPath(pair.Key));
            foreach (var pid in fallbackSeen.Keys.Except(current.Keys).ToArray()) HandleStopped(pid);
            fallbackSeen = current;
        }
        catch { }
        finally { Volatile.Write(ref polling, 0); }
    }
    private static async Task<bool> WaitForWindow(int pid)
    {
        for (var i = 0; i < 360; i++)
        {
            try { using var p = Process.GetProcessById(pid); p.Refresh(); if (p.MainWindowHandle != IntPtr.Zero) return true; if (p.HasExited) return false; }
            catch { return false; }
            await Task.Delay(250);
        }
        return false;
    }
    private async Task CloseProfileApps(AutomationProfile profile)
    {
        await Task.Delay(150);
        Dictionary<string, List<int>>? apps;
        lock (ownedPids) { if (!ownedPids.TryGetValue(profile.Id, out apps)) return; }
        foreach (var appId in profile.CloseAppIds)
        {
            var entry = getData().Applications.FirstOrDefault(a => a.Id == appId);
            if (entry == null || !apps.TryGetValue(appId, out var pids)) continue;
            foreach (var id in pids.ToArray())
                try
                {
                    using var process = Process.GetProcessById(id);
                    if (process.HasExited) continue;
                    if (process.CloseMainWindow()) Status?.Invoke($"Asked {entry.Name} to close");
                    else Status?.Invoke($"Could not request {entry.Name} to close (no main window)");
                }
                catch { }
        }
        lock (ownedPids) ownedPids.Remove(profile.Id);
    }
    private static bool IsRunning(string path)
    {
        var name = System.IO.Path.GetFileNameWithoutExtension(path);
        try { return Process.GetProcessesByName(name).Any(); } catch { return false; }
    }
    private static string? GetProcessPath(int pid)
    {
        var handle = OpenProcess(0x1000, false, (uint)pid);
        if (handle == IntPtr.Zero) return null;
        try
        {
            var path = new StringBuilder(1024); var length = (uint)path.Capacity;
            return QueryFullProcessImageName(handle, 0, path, ref length) ? path.ToString() : null;
        }
        finally { CloseHandle(handle); }
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ProcessEntry32
    {
        public uint Size, Usage, ProcessId;
        public IntPtr DefaultHeapId;
        public uint ModuleId, Threads, ParentProcessId;
        public int BasePriority;
        public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string ExecutableFile;
    }
    [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr CreateToolhelp32Snapshot(uint flags, uint processId);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool Process32First(IntPtr snapshot, ref ProcessEntry32 entry);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool Process32Next(IntPtr snapshot, ref ProcessEntry32 entry);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr OpenProcess(uint access, bool inheritHandle, uint processId);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool QueryFullProcessImageName(IntPtr process, uint flags, StringBuilder path, ref uint size);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool CloseHandle(IntPtr handle);
    private void StopWatchers() { try { starts?.Stop(); stops?.Stop(); } catch { } starts?.Dispose(); stops?.Dispose(); starts = stops = null; }
    public void Dispose() { StopWatchers(); fallbackTimer?.Dispose(); fallbackTimer = null; }
}
