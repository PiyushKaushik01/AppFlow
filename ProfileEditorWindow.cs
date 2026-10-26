using System.Windows;
using System.Windows.Controls;
using CheckBox = System.Windows.Controls.CheckBox;
using ListBox = System.Windows.Controls.ListBox;
using TextBox = System.Windows.Controls.TextBox;

namespace AppFlow;

public sealed class ProfileEditorWindow : Window
{
    private readonly AutomationProfile profile;
    private readonly List<AppEntry> apps;
    private readonly TextBox name = new();
    private readonly TextBlock triggerName = new();
    private readonly TextBlock triggerPath = new();
    private readonly ListBox actions = new() { MinHeight = 135, MaxHeight = 190 };
    private readonly ListBox closeActions = new() { MinHeight = 100, MaxHeight = 145, SelectionMode = SelectionMode.Multiple };
    private readonly TextBox delay = new() { Width = 150 };
    private readonly CheckBox enabled = new() { Content = "Profile enabled" };
    private readonly CheckBox relaunch = new() { Content = "Launch again if a companion app is already running" };
    private readonly CheckBox windowTrigger = new() { Content = "Wait until the trigger app's main window appears" };
    private AppEntry? selectedTrigger;
    private HashSet<string>? closeSelectionState;
    private bool refreshingCloseItems;

    public ProfileEditorWindow(AutomationProfile profile, List<AppEntry> apps, Window owner)
    {
        this.profile = profile; this.apps = apps; Owner = owner;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = (System.Windows.Media.Brush)FindResource("AppBackground");
        Foreground = (System.Windows.Media.Brush)FindResource("MainText");
        WindowTheme.ApplyDarkTitleBar(this);
        Title = "Profile"; Width = 650; Height = 760; MinWidth = 540; MinHeight = 600;

        var root = new DockPanel { Margin = new Thickness(22) };
        var footer = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
        DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer);
        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var content = new StackPanel { Margin = new Thickness(2, 0, 6, 2) };
        scroll.Content = content; root.Children.Add(scroll);

        var cancel = new Button { Content = "Cancel", MinWidth = 90 }; cancel.Click += (_, _) => DialogResult = false;
        var save = new Button { Content = "Save profile", MinWidth = 120, Style = (Style)FindResource("PrimaryButton") }; save.Click += SaveProfile;
        footer.Children.Add(cancel); footer.Children.Add(save);

        content.Children.Add(Heading("Profile name"));
        name.Text = profile.Name; content.Children.Add(name);

        content.Children.Add(Heading("Trigger application"));
        selectedTrigger = apps.FirstOrDefault(a => a.Id == profile.TriggerAppId);
        var triggerCard = new DockPanel { Background = (System.Windows.Media.Brush)FindResource("Panel"), Margin = new Thickness(2, 0, 2, 2), LastChildFill = false };
        var chooseTrigger = new Button { Content = "Choose trigger...", MinWidth = 145 };
        chooseTrigger.Click += (_, _) => ChooseTrigger();
        DockPanel.SetDock(chooseTrigger, Dock.Right); triggerCard.Children.Add(chooseTrigger);
        var triggerInfo = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 9, 12, 9) };
        triggerName.FontWeight = FontWeights.SemiBold;
        triggerPath.Foreground = (System.Windows.Media.Brush)FindResource("Muted"); triggerPath.FontSize = 12; triggerPath.TextTrimming = TextTrimming.CharacterEllipsis;
        triggerInfo.Children.Add(triggerName); triggerInfo.Children.Add(triggerPath); triggerCard.Children.Add(triggerInfo);
        content.Children.Add(triggerCard);
        UpdateTriggerDisplay();
        windowTrigger.IsChecked = profile.TriggerOnWindow; windowTrigger.Margin = new Thickness(3, 8, 3, 2); content.Children.Add(windowTrigger);

        content.Children.Add(Heading("Companion apps"));
        content.Children.Add(new TextBlock { Text = "Apps open in the order shown. Select a row to move or remove it.", Foreground = (System.Windows.Media.Brush)FindResource("Muted"), Margin = new Thickness(3, -2, 3, 7), TextWrapping = TextWrapping.Wrap });
        actions.ItemsSource = profile.ActionAppIds.Select(id => apps.FirstOrDefault(a => a.Id == id)).Where(a => a != null).ToList();
        actions.ItemTemplate = AppNameTemplate();
        content.Children.Add(actions);
        var actionButtons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 5, 0, 0) };
        var add = new Button { Content = "Add companion app...", Style = (Style)FindResource("PrimaryButton") };
        add.Click += (_, _) => ChooseAction();
        var up = new Button { Content = "Move up" }; up.Click += (_, _) => Move(-1);
        var down = new Button { Content = "Move down" }; down.Click += (_, _) => Move(1);
        var remove = new Button { Content = "Remove" }; remove.Click += (_, _) => RemoveAction();
        actionButtons.Children.Add(add); actionButtons.Children.Add(up); actionButtons.Children.Add(down); actionButtons.Children.Add(remove);
        content.Children.Add(actionButtons);

        content.Children.Add(Heading("Close when the trigger exits (optional)"));
        content.Children.Add(new TextBlock { Text = "Choose companion apps AppFlow launched. They receive a normal close request and can ask you to save work.", Foreground = (System.Windows.Media.Brush)FindResource("Muted"), Margin = new Thickness(3, -2, 3, 7), TextWrapping = TextWrapping.Wrap });
        closeActions.ItemTemplate = AppNameTemplate();
        closeActions.SelectionChanged += (_, _) =>
        {
            if (!refreshingCloseItems) closeSelectionState = closeActions.SelectedItems.Cast<AppEntry>().Select(a => a.Id).ToHashSet();
        };
        RefreshCloseActions(); content.Children.Add(closeActions);

        content.Children.Add(Heading("Launch behavior"));
        content.Children.Add(new TextBlock { Text = "Delay between companion launches (milliseconds)", Foreground = (System.Windows.Media.Brush)FindResource("MainText"), Margin = new Thickness(3, 0, 3, 6) });
        delay.Text = profile.DelayMs.ToString(); content.Children.Add(delay);
        relaunch.IsChecked = profile.RelaunchRunning; relaunch.Margin = new Thickness(3, 9, 3, 3); content.Children.Add(relaunch);
        enabled.IsChecked = profile.Enabled; enabled.Margin = new Thickness(3, 7, 3, 3); content.Children.Add(enabled);
        Content = root;
    }

    private void ChooseTrigger()
    {
        var picker = new AppPickerWindow(apps, this, selectedTrigger?.Id);
        if (picker.ShowDialog() == true) { selectedTrigger = picker.SelectedApp; UpdateTriggerDisplay(); }
    }
    private void ChooseAction()
    {
        var picker = new AppPickerWindow(apps, this);
        if (picker.ShowDialog() != true || picker.SelectedApp is not AppEntry app) return;
        var list = actions.Items.Cast<AppEntry>().ToList(); list.Add(app); actions.ItemsSource = list; RefreshCloseActions();
    }
    private void UpdateTriggerDisplay()
    {
        triggerName.Text = selectedTrigger?.Name ?? "No trigger selected";
        triggerPath.Text = selectedTrigger?.Path ?? "Choose the app that starts this profile";
    }
    private void Move(int delta)
    {
        var index = actions.SelectedIndex; var list = actions.Items.Cast<AppEntry>().ToList(); var target = index + delta;
        if (index < 0 || target < 0 || target >= list.Count) return;
        (list[index], list[target]) = (list[target], list[index]); actions.ItemsSource = list; actions.SelectedIndex = target;
    }
    private void RemoveAction()
    {
        if (actions.SelectedIndex < 0) return;
        var list = actions.Items.Cast<AppEntry>().ToList(); list.RemoveAt(actions.SelectedIndex); actions.ItemsSource = list; RefreshCloseActions();
    }
    private void RefreshCloseActions()
    {
        var prior = closeSelectionState ?? profile.CloseAppIds.ToHashSet();
        var unique = actions.Items.Cast<AppEntry>().GroupBy(a => a.Id).Select(g => g.First()).ToList();
        refreshingCloseItems = true;
        closeActions.ItemsSource = unique;
        closeSelectionState = prior.Intersect(unique.Select(a => a.Id)).ToHashSet();
        foreach (var app in unique) if (closeSelectionState.Contains(app.Id)) closeActions.SelectedItems.Add(app);
        refreshingCloseItems = false;
    }
    private void SaveProfile(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(name.Text)) { MessageBox.Show(this, "Enter a profile name.", "Profile"); return; }
        if (selectedTrigger == null) { MessageBox.Show(this, "Choose a trigger application.", "Profile"); return; }
        if (!int.TryParse(delay.Text, out var ms) || ms < 0 || ms > 60000) { MessageBox.Show(this, "Enter a delay from 0 to 60000 milliseconds.", "Profile"); return; }
        profile.Name = name.Text.Trim(); profile.TriggerAppId = selectedTrigger.Id;
        profile.ActionAppIds = actions.Items.Cast<AppEntry>().Select(a => a.Id).ToList();
        profile.CloseAppIds = closeActions.SelectedItems.Cast<AppEntry>().Select(a => a.Id).ToList();
        profile.DelayMs = ms; profile.Enabled = enabled.IsChecked == true;
        profile.RelaunchRunning = relaunch.IsChecked == true; profile.TriggerOnWindow = windowTrigger.IsChecked == true;
        DialogResult = true;
    }
    private TextBlock Heading(string text) => new() { Text = text, Foreground = (System.Windows.Media.Brush)FindResource("MainText"), FontSize = 14, FontWeight = FontWeights.SemiBold, Margin = new Thickness(3, 16, 3, 7) };
    private static DataTemplate AppNameTemplate()
    {
        var item = new FrameworkElementFactory(typeof(TextBlock));
        item.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding(nameof(AppEntry.Name)));
        item.SetValue(TextBlock.FontSizeProperty, 14d);
        return new DataTemplate(typeof(AppEntry)) { VisualTree = item };
    }
}
