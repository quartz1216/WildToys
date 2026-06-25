using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace WildToys.Pages;

public sealed partial class MouseGesturePage : Page
{
    private bool _loading;
    private readonly ObservableCollection<GestureItem> _items = new();

    public MouseGesturePage()
    {
        InitializeComponent();
        EntriesList.ItemsSource = _items;
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _loading = true;
        EnabledToggle.IsOn = App.Current.Settings.IsMouseGestureEnabled;
        _loading = false;
        RebuildItems();
    }

    private void EnabledToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_loading) return;

        bool enabled = EnabledToggle.IsOn;
        App.Current.Settings.IsMouseGestureEnabled = enabled;
        App.Current.SaveSettings();

        if (enabled)
            App.Current.MouseGesture.Start();
        else
            App.Current.MouseGesture.Stop();
    }

    private void RebuildItems()
    {
        _items.Clear();
        foreach (var entry in App.Current.Settings.MouseGestures)
            _items.Add(new GestureItem(entry));

        EmptyHint.Visibility = _items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Persist()
    {
        App.Current.SaveSettings();
        App.Current.MouseGesture.ReloadSettings();
        RebuildItems();
    }

    private async void AddButton_Click(object sender, RoutedEventArgs e) => await ShowEditorAsync(null);

    private async void EditButton_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is GestureItem item)
            await ShowEditorAsync(item.Entry);
    }

    private void DeleteButton_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is GestureItem item)
        {
            App.Current.Settings.MouseGestures.Remove(item.Entry);
            Persist();
        }
    }

    private async Task ShowEditorAsync(GestureEntry? existing)
    {
        bool isNew = existing is null;

        var dirs = new List<string>(SplitDirections(existing?.Gesture ?? ""));

        var arrowText = new TextBlock { FontSize = 24, MinHeight = 34 };
        void Refresh() => arrowText.Text = dirs.Count == 0 ? "(no strokes)" : string.Join("  ", dirs.Select(ToArrow));
        Refresh();

        void AddDir(string dir)
        {
            if (dirs.Count == 0 || dirs[^1] != dir) { dirs.Add(dir); Refresh(); }
        }

        var buttonCombo = new ComboBox { Header = "Gesture button" };
        buttonCombo.Items.Add("Right");
        buttonCombo.Items.Add("Middle");
        buttonCombo.SelectedIndex = existing?.Button == "Middle" ? 1 : 0;

        var actionBox = new TextBox { Header = "Action (shortcut)", PlaceholderText = "e.g. Ctrl+Alt+Left", Text = existing?.Action ?? "" };
        var processBox = new TextBox { Header = "Process (optional)", PlaceholderText = "e.g. chrome  —  empty = all apps", Text = existing?.Process ?? "" };

        // Draw pad: perform the gesture here with the right or middle button.
        var padHint = new TextBlock
        {
            Text = "Draw here with the right or middle button",
            Opacity = 0.6,
            FontSize = 12,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            TextAlignment = TextAlignment.Center,
        };
        var pad = new Border
        {
            Height = 150,
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(Microsoft.UI.Colors.Gray) { Opacity = 0.5 },
            Background = new SolidColorBrush(Microsoft.UI.Colors.Gray) { Opacity = 0.08 },
            Child = padHint,
        };

        bool capturing = false;
        Windows.Foundation.Point last = default;

        pad.PointerPressed += (_, ev) =>
        {
            var p = ev.GetCurrentPoint(pad);
            string? btn = p.Properties.IsRightButtonPressed ? "Right"
                : p.Properties.IsMiddleButtonPressed ? "Middle" : null;
            if (btn is null) return;

            capturing = true;
            dirs.Clear();
            Refresh();
            last = p.Position;
            buttonCombo.SelectedIndex = btn == "Middle" ? 1 : 0;
            pad.CapturePointer(ev.Pointer);
            ev.Handled = true;
        };
        pad.PointerMoved += (_, ev) =>
        {
            if (!capturing) return;
            var pos = ev.GetCurrentPoint(pad).Position;
            double dx = pos.X - last.X;
            double dy = pos.Y - last.Y;
            if (System.Math.Max(System.Math.Abs(dx), System.Math.Abs(dy)) < 30) return;
            AddDir(System.Math.Abs(dx) > System.Math.Abs(dy) ? (dx > 0 ? "Right" : "Left") : (dy > 0 ? "Down" : "Up"));
            last = pos;
            ev.Handled = true;
        };
        pad.PointerReleased += (_, ev) =>
        {
            if (!capturing) return;
            capturing = false;
            pad.ReleasePointerCapture(ev.Pointer);
            ev.Handled = true;
        };
        pad.PointerCaptureLost += (_, _) => capturing = false;

        var content = new StackPanel { Spacing = 12, MinWidth = 380 };
        content.Children.Add(new TextBlock { Text = "Gesture strokes", FontWeight = FontWeights.SemiBold });
        content.Children.Add(arrowText);
        content.Children.Add(pad);
        content.Children.Add(buttonCombo);
        content.Children.Add(actionBox);
        content.Children.Add(processBox);

        var dialog = new ContentDialog
        {
            Title = isNew ? "Add gesture" : "Edit gesture",
            PrimaryButtonText = "Save",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot,
            Content = content,
        };

        // Suspend the global gesture hook so it doesn't swallow the right/middle button
        // events the pad needs while drawing.
        var module = App.Current.MouseGesture;
        module.SuspendForCapture = true;
        ContentDialogResult result;
        try
        {
            result = await dialog.ShowAsync();
        }
        finally
        {
            module.SuspendForCapture = false;
        }

        if (result != ContentDialogResult.Primary)
            return;

        var action = actionBox.Text.Trim();
        if (dirs.Count == 0 || string.IsNullOrWhiteSpace(action))
            return; // nothing meaningful to save

        var entry = existing ?? new GestureEntry();
        entry.Gesture = string.Join(",", dirs);
        entry.Button = buttonCombo.SelectedIndex == 1 ? "Middle" : "Right";
        entry.Action = action;
        entry.Process = processBox.Text.Trim();

        if (isNew)
            App.Current.Settings.MouseGestures.Add(entry);

        Persist();
    }

    internal static IEnumerable<string> SplitDirections(string gesture) =>
        gesture.Split(',', System.StringSplitOptions.TrimEntries | System.StringSplitOptions.RemoveEmptyEntries);

    internal static string ToArrow(string dir) => dir.ToLowerInvariant() switch
    {
        "up" => "↑",
        "down" => "↓",
        "left" => "←",
        "right" => "→",
        _ => "?"
    };
}

public sealed class GestureItem
{
    public GestureEntry Entry { get; }
    public string Arrows { get; }
    public string Detail { get; }

    public GestureItem(GestureEntry entry)
    {
        Entry = entry;

        var arrows = string.Join("  ", MouseGesturePage.SplitDirections(entry.Gesture).Select(MouseGesturePage.ToArrow));
        Arrows = string.IsNullOrEmpty(arrows) ? "(no strokes)" : arrows;

        var proc = string.IsNullOrWhiteSpace(entry.Process) ? "All apps" : entry.Process;
        Detail = $"{entry.Button} button   ·   {entry.Action}   ·   {proc}";
    }
}
