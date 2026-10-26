using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.ComponentModel;
using Image = System.Windows.Controls.Image;

namespace AppFlow;

public sealed class AppPickerWindow : Window
{
    private readonly TextBox search = new();
    private readonly ListBox list = new();
    private readonly TextBlock empty = new();
    private readonly ICollectionView view;
    public AppEntry? SelectedApp { get; private set; }

    public AppPickerWindow(IEnumerable<AppEntry> applications, Window owner, string? selectedId = null)
    {
        Owner = owner;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = (System.Windows.Media.Brush)FindResource("AppBackground");
        Foreground = (System.Windows.Media.Brush)FindResource("MainText");
        WindowTheme.ApplyDarkTitleBar(this);
        Title = "Choose an application";
        Width = 590;
        Height = 610;
        MinWidth = 480;
        MinHeight = 480;

        var items = applications.OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        view = CollectionViewSource.GetDefaultView(items);
        view.Filter = MatchesSearch;
        list.ItemsSource = view;
        list.ItemTemplate = CreateAppTemplate();
        list.SelectionChanged += (_, _) => UpdateEmptyText();
        list.MouseDoubleClick += (_, _) => ChooseSelected();

        var root = new DockPanel { Margin = new Thickness(20) };
        var heading = new StackPanel { Margin = new Thickness(2, 0, 2, 12) };
        heading.Children.Add(new TextBlock { Text = "Choose an application", FontSize = 21, FontWeight = FontWeights.SemiBold });
        heading.Children.Add(new TextBlock { Text = "Search by name or executable path, then select one app.", Foreground = (System.Windows.Media.Brush)FindResource("Muted"), Margin = new Thickness(0, 5, 0, 0) });
        DockPanel.SetDock(heading, Dock.Top); root.Children.Add(heading);

        search.Margin = new Thickness(0, 0, 0, 12);
        search.TextChanged += (_, _) => { view.Refresh(); UpdateEmptyText(); };
        DockPanel.SetDock(search, Dock.Top); root.Children.Add(search);

        empty.Text = "No matching applications.";
        empty.Foreground = (System.Windows.Media.Brush)FindResource("Muted");
        empty.HorizontalAlignment = HorizontalAlignment.Center;
        empty.VerticalAlignment = VerticalAlignment.Center;
        empty.IsHitTestVisible = false;

        var listHost = new Grid();
        listHost.Children.Add(list); listHost.Children.Add(empty);
        root.Children.Add(listHost);

        var footer = new DockPanel { Margin = new Thickness(0, 12, 0, 0), LastChildFill = false };
        var cancel = new Button { Content = "Cancel", MinWidth = 90 };
        cancel.Click += (_, _) => DialogResult = false;
        var choose = new Button { Content = "Choose app", MinWidth = 110, Style = (Style)FindResource("PrimaryButton") };
        choose.Click += (_, _) => ChooseSelected();
        footer.Children.Add(cancel); footer.Children.Add(choose);
        DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer);
        Content = root;

        var initial = items.FirstOrDefault(a => a.Id == selectedId);
        if (initial != null) list.SelectedItem = initial;
        Loaded += (_, _) => search.Focus();
        UpdateEmptyText();
    }

    private static DataTemplate CreateAppTemplate()
    {
        var template = new DataTemplate(typeof(AppEntry));
        var row = new FrameworkElementFactory(typeof(StackPanel));
        row.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);
        row.SetValue(FrameworkElement.MarginProperty, new Thickness(2, 3, 2, 3));
        var image = new FrameworkElementFactory(typeof(Image));
        image.SetValue(Image.WidthProperty, 26d); image.SetValue(Image.HeightProperty, 26d);
        image.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        image.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 10, 0));
        image.SetBinding(Image.SourceProperty, new System.Windows.Data.Binding(nameof(AppEntry.IconSource)));
        row.AppendChild(image);

        var content = new FrameworkElementFactory(typeof(StackPanel));
        content.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        var name = new FrameworkElementFactory(typeof(TextBlock));
        name.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding(nameof(AppEntry.Name)));
        name.SetValue(TextBlock.FontWeightProperty, FontWeights.SemiBold);
        content.AppendChild(name);
        var path = new FrameworkElementFactory(typeof(TextBlock));
        path.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding(nameof(AppEntry.Path)));
        path.SetValue(TextBlock.ForegroundProperty, (System.Windows.Media.Brush)Application.Current.FindResource("Muted"));
        path.SetValue(TextBlock.FontSizeProperty, 12d);
        path.SetValue(TextBlock.WidthProperty, 440d);
        path.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
        content.AppendChild(path);
        row.AppendChild(content);
        template.VisualTree = row;
        return template;
    }

    private bool MatchesSearch(object item)
    {
        if (item is not AppEntry app || string.IsNullOrWhiteSpace(search.Text)) return item is AppEntry;
        return app.Name.Contains(search.Text, StringComparison.CurrentCultureIgnoreCase) || app.Path.Contains(search.Text, StringComparison.CurrentCultureIgnoreCase);
    }
    private void UpdateEmptyText() => empty.Visibility = list.Items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    private void ChooseSelected()
    {
        if (list.SelectedItem is not AppEntry app) return;
        SelectedApp = app;
        DialogResult = true;
    }
}
