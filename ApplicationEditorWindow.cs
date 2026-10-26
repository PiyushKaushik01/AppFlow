using System.Windows;
using System.Windows.Controls;

namespace AppFlow;

public sealed class ApplicationEditorWindow : Window
{
    private readonly AppEntry app;
    private readonly TextBox name = new() { MinWidth = 350 };
    private readonly TextBox arguments = new() { MinWidth = 350 };
    public ApplicationEditorWindow(AppEntry app, Window owner)
    {
        this.app = app; Owner = owner; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = (System.Windows.Media.Brush)FindResource("AppBackground");
        Foreground = (System.Windows.Media.Brush)FindResource("MainText");
        WindowTheme.ApplyDarkTitleBar(this);
        Title = "Application details"; Width = 470; Height = 270; ResizeMode = ResizeMode.NoResize;
        var root = new StackPanel { Margin = new Thickness(22), Background = (System.Windows.Media.Brush)FindResource("AppBackground") };
        root.Children.Add(new TextBlock { Text = "Application name", Foreground = (System.Windows.Media.Brush)FindResource("MainText"), Margin = new Thickness(3, 6, 3, 5) });
        name.Text = app.Name; root.Children.Add(name);
        root.Children.Add(new TextBlock { Text = "Command-line arguments (optional)", Foreground = (System.Windows.Media.Brush)FindResource("MainText"), Margin = new Thickness(3, 14, 3, 5) });
        arguments.Text = app.Arguments; root.Children.Add(arguments);
        root.Children.Add(new TextBlock { Text = app.Path, Foreground = (System.Windows.Media.Brush)FindResource("Muted"), FontSize = 11, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(3, 10, 3, 8) });
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var cancel = new Button { Content = "Cancel", Width = 90 }; cancel.Click += (_, _) => DialogResult = false;
        var save = new Button { Content = "Save", Width = 90 }; save.Click += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(name.Text)) { MessageBox.Show(this, "Enter an application name.", "Application"); return; }
            app.Name = name.Text.Trim(); app.Arguments = arguments.Text.Trim(); DialogResult = true;
        };
        buttons.Children.Add(cancel); buttons.Children.Add(save); root.Children.Add(buttons);
        Content = root;
    }
}
