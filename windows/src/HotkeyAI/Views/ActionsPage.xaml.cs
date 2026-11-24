using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace HotkeyAI.Views;

public partial class ActionsPage : UserControl
{
    private AppModel _model = null!;
    private ActionEditor? _editor;

    public ActionsPage()
    {
        InitializeComponent();
        Recorder.RecordingChanged += recording => _model.HotkeysSuspended = recording;
    }

    internal void Attach(AppModel model)
    {
        _model = model;
        DataContext = model;
        List.ItemsSource = model.Actions;
        if (model.Actions.Count > 0) List.SelectedIndex = 0;
        UpdateDetail();
    }

    private HotkeyAction? Selected => List.SelectedItem as HotkeyAction;

    private void List_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateDetail();

    private void UpdateDetail()
    {
        _editor?.Dispose();
        _editor = Selected is { } action ? new ActionEditor(_model, action) : null;
        Detail.DataContext = _editor;
        Detail.Visibility = _editor is null ? Visibility.Collapsed : Visibility.Visible;
        EmptyHint.Visibility = _editor is null ? Visibility.Visible : Visibility.Collapsed;
        RemoveButton.IsEnabled = _editor is not null;
        Detail.ScrollToTop();
    }

    private void Add_Click(object sender, RoutedEventArgs e)
    {
        List.SelectedItem = _model.AddAction();
        List.ScrollIntoView(List.SelectedItem);
    }

    private void Remove_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } action) return;
        var index = _model.Actions.IndexOf(action);
        _model.Actions.Remove(action);
        if (_model.Actions.Count > 0) List.SelectedIndex = Math.Min(index, _model.Actions.Count - 1);
    }

    private void Move(int delta)
    {
        if (Selected is not { } action) return;
        var index = _model.Actions.IndexOf(action);
        var target = index + delta;
        if (target < 0 || target >= _model.Actions.Count) return;
        _model.Actions.Move(index, target);
        List.SelectedItem = action;
    }

    private void MoveUp_Click(object sender, RoutedEventArgs e) => Move(-1);

    private void MoveDown_Click(object sender, RoutedEventArgs e) => Move(1);

    private void More_Click(object sender, RoutedEventArgs e)
    {
        var menu = MoreButton.ContextMenu!;
        menu.PlacementTarget = MoreButton;
        menu.Placement = PlacementMode.Top;
        ((MenuItem)menu.Items[0]).IsEnabled = Selected is not null;
        menu.IsOpen = true;
    }

    private void Duplicate_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is { } action) List.SelectedItem = _model.Duplicate(action);
    }

    private void RestoreDefaults_Click(object sender, RoutedEventArgs e) => _model.RestoreDefaults();

    private async void RunTest_Click(object sender, RoutedEventArgs e)
    {
        if (_editor is { } editor) await editor.RunTestAsync();
    }
}
