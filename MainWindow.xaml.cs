using Microsoft.Win32;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using Velopack;
using Velopack.Locators;
using Icon = System.Drawing.Icon;
using NotifyIcon = System.Windows.Forms.NotifyIcon;
using ContextMenuStrip = System.Windows.Forms.ContextMenuStrip;
using ToolStripSeparator = System.Windows.Forms.ToolStripSeparator;
using ToolTipIcon = System.Windows.Forms.ToolTipIcon;
using ToolStripMenuItem = System.Windows.Forms.ToolStripMenuItem;

namespace AppFlow;

public partial class MainWindow : Window
{
    private readonly AppData data = DataStore.Load();
    private readonly ProcessMonitor monitor;
    private readonly Icon appIcon;
    private readonly UpdateService updateService = new();
    private UpdateInfo? pendingUpdate;
    private ToolStripMenuItem restartUpdateMenuItem = null!;
    private bool checkingForUpdates;
    private readonly NotifyIcon tray = new() { Text = "AppFlow — monitoring", Icon = System.Drawing.SystemIcons.Application, Visible = true };
    private ToolStripMenuItem pauseMenuItem = null!;
    private bool exitRequested;
    private bool stateLoaded;
    private bool applicationsScanned;
    private Task? discoveryTask;
    private readonly ObservableCollection<AppEntry> appItems = [];
    private readonly ObservableCollection<AutomationProfile> profileItems = [];
    public MainWindow()
    {
        InitializeComponent();
        WindowTheme.ApplyDarkTitleBar(this);
        appIcon = LoadAppIcon();
        tray.Icon = appIcon;
        ShowOnly(ProfilesPage);
        monitor = new ProcessMonitor(() => data);
        monitor.Status += s => Dispatcher.Invoke(() => { MonitorStatus.Text = s; if (data.Settings.Notifications && s.StartsWith("Opened ")) tray.ShowBalloonTip(1200, "AppFlow", s, ToolTipIcon.Info); });
        monitor.Error += s => Dispatcher.Invoke(() => { MonitorStatus.Text = "Needs attention"; tray.ShowBalloonTip(3500, "AppFlow", s, ToolTipIcon.Warning); });
        tray.ContextMenuStrip = new ContextMenuStrip();
        tray.ContextMenuStrip.Items.Add("Open AppFlow", null, (_, _) => Dispatcher.Invoke(ShowWindow));
        pauseMenuItem = new ToolStripMenuItem("Pause automation", null, (_, _) => Dispatcher.Invoke(() => { PauseToggle.IsChecked = !PauseToggle.IsChecked; }));
        tray.ContextMenuStrip.Items.Add(pauseMenuItem);
        tray.ContextMenuStrip.Items.Add(new ToolStripSeparator());
        tray.ContextMenuStrip.Items.Add("Check for updates", null, (_, _) => Dispatcher.BeginInvoke(new Action(() => _ = CheckForUpdatesAsync(manual: true))));
        restartUpdateMenuItem = new ToolStripMenuItem("Restart to install update", null, (_, _) => Dispatcher.Invoke(ApplyUpdate)) { Enabled = false };
        tray.ContextMenuStrip.Items.Add(restartUpdateMenuItem);
        tray.ContextMenuStrip.Items.Add(new ToolStripSeparator());
        tray.ContextMenuStrip.Items.Add("Exit AppFlow", null, (_, _) => Dispatcher.Invoke(ExitApplication));
        tray.DoubleClick += (_, _) => Dispatcher.Invoke(ShowWindow);
        Microsoft.Win32.SystemEvents.SessionEnding += OnSessionEnding;
        LoadState();
        monitor.Start();
        Loaded += async (_, _) => await CheckForUpdatesAsync(manual: false);
        if (data.Settings.LaunchMinimized) Loaded += (_, _) => Hide();
    }
    private void LoadState()
    {
        foreach (var profile in data.Profiles) UpdateTriggerName(profile);
        RefreshProfiles(); RefreshApps();
        StartupToggle.IsChecked = data.Settings.StartWithWindows; MinimizedToggle.IsChecked = data.Settings.LaunchMinimized;
        NotifyToggle.IsChecked = data.Settings.Notifications;
        DelayBox.Text = data.Settings.DefaultDelayMs.ToString();
        stateLoaded = true;
        data.Settings.MinimizeToTray = true;
        UpdatePauseLabel();
        Save();
    }
    private Task LoadDiscoveredApps()
    {
        if (discoveryTask is { IsCompleted: false }) return discoveryTask;
        return discoveryTask = LoadDiscoveredAppsCore();
    }
    private async Task LoadDiscoveredAppsCore()
    {
        if (appItems.Count == 0) AppsEmptyText.Text = "Searching your Start menu for applications…";
        try
        {
            var discovered = await Task.Run(AppDiscovery.Discover);
            applicationsScanned = true;
            var existing = new HashSet<string>(data.Applications.Select(a => AppDiscovery.Normalize(a.Path)), StringComparer.OrdinalIgnoreCase);
            foreach (var app in discovered) if (existing.Add(AppDiscovery.Normalize(app.Path))) data.Applications.Add(app);
            RefreshApps(); Save();
        }
        catch (Exception ex)
        {
            applicationsScanned = true;
            AppsEmptyText.Text = "Could not scan for applications. You can add one manually.";
            MonitorStatus.Text = "Application scan failed: " + ex.Message;
        }
    }
    private void RefreshProfiles()
    {
        foreach (var p in data.Profiles) UpdateTriggerName(p);
        profileItems.Clear(); foreach (var p in data.Profiles) profileItems.Add(p);
        ProfileList.ItemsSource = profileItems;
        ProfileEmptyText.Visibility = profileItems.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }
    private void RefreshApps()
    {
        appItems.Clear(); foreach (var a in data.Applications.OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase)) appItems.Add(a);
        AppList.ItemsSource = appItems;
        AppsEmptyText.Visibility = appItems.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        ApplyAppFilter();
    }
    private void UpdateTriggerName(AutomationProfile p) => p.TriggerName = data.Applications.FirstOrDefault(a => a.Id == p.TriggerAppId)?.Name ?? "No trigger selected";
    private void Save() { try { DataStore.Save(data); } catch (Exception e) { MonitorStatus.Text = "Could not save settings: " + e.Message; } }
    private void ShowOnly(Grid page)
    {
        ProfilesPage.Visibility = Visibility.Collapsed; AppsPage.Visibility = Visibility.Collapsed; SettingsPage.Visibility = Visibility.Collapsed;
        page.Visibility = Visibility.Visible;
        SetNavigationState(ProfilesNav, page == ProfilesPage); SetNavigationState(AppsNav, page == AppsPage); SetNavigationState(SettingsNav, page == SettingsPage);
    }
    private static void SetNavigationState(System.Windows.Controls.Button button, bool selected)
    {
        button.Background = selected ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(42, 45, 55)) : System.Windows.Media.Brushes.Transparent;
        button.BorderBrush = selected ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(78, 86, 113)) : System.Windows.Media.Brushes.Transparent;
        button.Foreground = selected ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(185, 194, 255)) : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(220, 221, 225));
    }
    private void ShowProfiles(object sender, RoutedEventArgs e) => ShowOnly(ProfilesPage);
    private void ShowApps(object sender, RoutedEventArgs e)
    {
        ShowOnly(AppsPage);
        if (!applicationsScanned) _ = LoadDiscoveredApps();
    }
    private void ShowSettings(object sender, RoutedEventArgs e) => ShowOnly(SettingsPage);
    private async void NewProfile(object sender, RoutedEventArgs e)
    {
        if (!applicationsScanned && data.Applications.Count == 0)
        {
            ShowOnly(AppsPage);
            AppsEmptyText.Text = "Searching your Start menu for applications…";
            await LoadDiscoveredApps();
            if (data.Applications.Count == 0)
            {
                System.Windows.MessageBox.Show(this, "No applications were found. Add an application first, then create a profile.", "No applications found", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            ShowOnly(ProfilesPage);
        }
        else if (!applicationsScanned) _ = LoadDiscoveredApps();
        var profile = new AutomationProfile { DelayMs = data.Settings.DefaultDelayMs };
        if (new ProfileEditorWindow(profile, data.Applications, this).ShowDialog() == true) { data.Profiles.Add(profile); Save(); RefreshProfiles(); ProfileList.SelectedItem = profile; }
    }
    private void EditProfile(object sender, RoutedEventArgs e)
    {
        if (ProfileList.SelectedItem is not AutomationProfile original) { System.Windows.MessageBox.Show(this, "Select a profile to edit.", "AppFlow"); return; }
        var copy = System.Text.Json.JsonSerializer.Deserialize<AutomationProfile>(System.Text.Json.JsonSerializer.Serialize(original))!;
        if (new ProfileEditorWindow(copy, data.Applications, this).ShowDialog() == true)
        {
            original.Name = copy.Name; original.TriggerAppId = copy.TriggerAppId; original.ActionAppIds = copy.ActionAppIds;
            original.CloseAppIds = copy.CloseAppIds; original.Enabled = copy.Enabled; original.DelayMs = copy.DelayMs;
            original.RelaunchRunning = copy.RelaunchRunning; original.TriggerOnWindow = copy.TriggerOnWindow;
            UpdateTriggerName(original); Save(); RefreshProfiles(); ProfileList.SelectedItem = original;
        }
    }
    private void DuplicateProfile(object sender, RoutedEventArgs e)
    {
        if (ProfileList.SelectedItem is not AutomationProfile p) return;
        var clone = System.Text.Json.JsonSerializer.Deserialize<AutomationProfile>(System.Text.Json.JsonSerializer.Serialize(p))!;
        clone.Id = Guid.NewGuid().ToString("N"); clone.Name += " copy"; data.Profiles.Add(clone); Save(); RefreshProfiles(); ProfileList.SelectedItem = clone;
    }
    private void DeleteProfile(object sender, RoutedEventArgs e)
    {
        if (ProfileList.SelectedItem is not AutomationProfile p) return;
        if (System.Windows.MessageBox.Show(this, $"Delete profile “{p.Name}”?", "Delete profile", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        data.Profiles.Remove(p); Save(); RefreshProfiles();
    }
    private void ProfileSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var hasSelection = ProfileList.SelectedItem is AutomationProfile;
        EditProfileButton.IsEnabled = hasSelection; DuplicateButton.IsEnabled = hasSelection; DeleteProfileButton.IsEnabled = hasSelection;
    }
    private void ProfileEnabledChanged(object sender, RoutedEventArgs e) => Save();
    private void PauseChanged(object sender, RoutedEventArgs e)
    {
        monitor.Pause(PauseToggle.IsChecked == true);
        UpdatePauseLabel();
    }
    private void UpdatePauseLabel()
    {
        var paused = PauseToggle.IsChecked == true;
        if (pauseMenuItem != null) pauseMenuItem.Text = paused ? "Resume automation" : "Pause automation";
        if (MonitorDetail != null) MonitorDetail.Text = paused ? "Automation is paused" : "Watching for trigger apps";
    }
    private void AddApplication(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Title = "Choose an application", Filter = "Applications and shortcuts (*.exe;*.com;*.lnk)|*.exe;*.com;*.lnk|All files (*.*)|*.*", CheckFileExists = true };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            var path = AppDiscovery.ResolveTargetPath(dialog.FileName);
            var duplicate = data.Applications.FirstOrDefault(a => AppDiscovery.Normalize(a.Path).Equals(AppDiscovery.Normalize(path), StringComparison.OrdinalIgnoreCase));
            if (duplicate == null)
            {
                var entry = new AppEntry { Name = System.IO.Path.GetFileNameWithoutExtension(path), Path = path };
                if (new ApplicationEditorWindow(entry, this).ShowDialog() != true) return;
                data.Applications.Add(entry);
            }
            RefreshApps(); Save();
        }
        catch (Exception ex) { System.Windows.MessageBox.Show(this, ex.Message, "Cannot add application", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }
    private void EditApplication(object sender, RoutedEventArgs e)
    {
        if (AppList.SelectedItem is not AppEntry app) return;
        if (new ApplicationEditorWindow(app, this).ShowDialog() == true) { Save(); RefreshApps(); AppList.SelectedItem = app; }
    }
    private void RemoveApplication(object sender, RoutedEventArgs e)
    {
        if (AppList.SelectedItem is not AppEntry app) return;
        if (data.Profiles.Any(p => p.TriggerAppId == app.Id || p.ActionAppIds.Contains(app.Id) || p.CloseAppIds.Contains(app.Id)))
        { System.Windows.MessageBox.Show(this, "This application is used by a profile. Remove it from that profile first.", "Application in use"); return; }
        data.Applications.Remove(app); Save(); RefreshApps();
    }
    private void RefreshApplications(object sender, RoutedEventArgs e) { applicationsScanned = false; _ = LoadDiscoveredApps(); }
    private void SearchChanged(object sender, TextChangedEventArgs e) => ApplyAppFilter();
    private void ApplyAppFilter()
    {
        if (AppList == null) return;
        var view = CollectionViewSource.GetDefaultView(appItems);
        if (SearchPlaceholder != null) SearchPlaceholder.Visibility = string.IsNullOrEmpty(SearchBox?.Text) ? Visibility.Visible : Visibility.Collapsed;
        view.Filter = o => o is AppEntry a && (string.IsNullOrWhiteSpace(SearchBox?.Text) || a.Name.Contains(SearchBox.Text, StringComparison.CurrentCultureIgnoreCase) || a.Path.Contains(SearchBox.Text, StringComparison.CurrentCultureIgnoreCase));
        if (AppsEmptyText != null)
        {
            AppsEmptyText.Text = appItems.Count == 0 ? "No applications found. Add an executable or shortcut to get started." : "No apps match this search. Try another name or path.";
            AppsEmptyText.Visibility = view.IsEmpty ? Visibility.Visible : Visibility.Collapsed;
        }
    }
    private void AppSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var hasSelection = AppList.SelectedItem is AppEntry;
        EditApplicationButton.IsEnabled = hasSelection; RemoveApplicationButton.IsEnabled = hasSelection;
    }
    private void SettingsChanged(object sender, RoutedEventArgs e)
    {
        if (!stateLoaded) return;
        data.Settings.StartWithWindows = StartupToggle.IsChecked == true; data.Settings.LaunchMinimized = MinimizedToggle.IsChecked == true;
        data.Settings.MinimizeToTray = true; data.Settings.Notifications = NotifyToggle.IsChecked == true;
        if (int.TryParse(DelayBox.Text, out var delay)) data.Settings.DefaultDelayMs = Math.Clamp(delay, 0, 60000);
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true) ?? Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
            if (data.Settings.StartWithWindows)
            {
                var installRoot = VelopackLocator.IsCurrentSet ? VelopackLocator.Current.RootAppDir : null;
                var launchPath = !string.IsNullOrWhiteSpace(installRoot)
                    ? System.IO.Path.Combine(installRoot, "AppFlow.exe")
                    : Environment.ProcessPath;
                key.SetValue("AppFlow", $"\"{launchPath}\"");
            }
            else key.DeleteValue("AppFlow", false);
        }
        catch (Exception ex) { MonitorStatus.Text = "Startup setting unavailable: " + ex.Message; }
        Save();
    }
    private void ShowWindow() { WindowState = WindowState.Normal; Show(); Activate(); }
    internal void ActivateFromSecondLaunch() => ShowWindow();
    private void Window_StateChanged(object? sender, EventArgs e)
    {
        if (WindowState == WindowState.Minimized && !exitRequested)
        {
            WindowState = WindowState.Normal;
            Hide();
        }
    }
    private void ExitApplication() { exitRequested = true; monitor.Dispose(); tray.Visible = false; tray.Dispose(); Close(); }

    private static System.Drawing.Icon LoadAppIcon()
    {
        try
        {
            var iconResource = System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/Assets/AppFlow.ico"));
            if (iconResource != null)
            {
                using var stream = iconResource.Stream;
                using var embeddedIcon = new System.Drawing.Icon(stream, 32, 32);
                return (System.Drawing.Icon)embeddedIcon.Clone();
            }
        }
        catch { }

        try
        {
            var executable = Environment.ProcessPath;
            if (!string.IsNullOrWhiteSpace(executable) && System.Drawing.Icon.ExtractAssociatedIcon(executable) is { } icon) return icon;
        }
        catch { }
        return (System.Drawing.Icon)System.Drawing.SystemIcons.Application.Clone();
    }

    private async void CheckForUpdatesClicked(object sender, RoutedEventArgs e) => await CheckForUpdatesAsync(manual: true);

    private async Task CheckForUpdatesAsync(bool manual)
    {
        if (checkingForUpdates) return;
        if (!updateService.IsConfigured)
        {
            UpdateStatusText.Text = updateService.ConfigurationProblem ?? "GitHub updates are not configured for this build yet.";
            return;
        }
        if (!updateService.IsInstalled)
        {
            UpdateStatusText.Text = "Install AppFlow with Setup.exe to enable updates.";
            return;
        }

        checkingForUpdates = true;
        CheckUpdatesButton.IsEnabled = false;
        try
        {
            UpdateStatusText.Text = "Checking GitHub for updates…";
            var update = await updateService.CheckAsync();
            if (update == null)
            {
                UpdateStatusText.Text = $"AppFlow is up to date (v{updateService.CurrentVersion}).";
                return;
            }

            var version = update.TargetFullRelease.Version.ToString();
            UpdateStatusText.Text = $"Downloading update v{version}…";
            await updateService.DownloadAsync(update);
            pendingUpdate = update;
            UpdateStatusText.Text = $"Update v{version} is ready. Restart AppFlow to install it.";
            ApplyUpdateButton.Visibility = Visibility.Visible;
            restartUpdateMenuItem.Enabled = true;
            tray.ShowBalloonTip(5000, "AppFlow update ready", $"Version {version} is ready. Choose Restart to install update from the tray menu.", ToolTipIcon.Info);
        }
        catch (Exception ex)
        {
            UpdateStatusText.Text = "Could not check for updates. Check your internet connection and GitHub release settings.";
            if (manual) tray.ShowBalloonTip(5000, "AppFlow update check failed", ex.Message, ToolTipIcon.Warning);
        }
        finally
        {
            checkingForUpdates = false;
            CheckUpdatesButton.IsEnabled = true;
        }
    }

    private void ApplyUpdateClicked(object sender, RoutedEventArgs e) => ApplyUpdate();

    private void ApplyUpdate()
    {
        if (pendingUpdate == null) return;
        monitor.Dispose();
        tray.Visible = false;
        tray.Dispose();
        appIcon.Dispose();
        updateService.ApplyAndRestart(pendingUpdate);
    }
    private void OnSessionEnding(object? sender, Microsoft.Win32.SessionEndingEventArgs e)
    {
        exitRequested = true;
        Dispatcher.BeginInvoke(new Action(Close));
    }
    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (!exitRequested) { e.Cancel = true; WindowState = WindowState.Normal; Hide(); return; }
        Microsoft.Win32.SystemEvents.SessionEnding -= OnSessionEnding;
        monitor.Dispose(); tray.Visible = false; tray.Dispose(); Save();
    }
}
