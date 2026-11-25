using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using HotkeyAI.Platform;

namespace HotkeyAI.Views;

/// Key entry and default model for one API-key provider.
public partial class ApiKeyCard : UserControl
{
    public ProviderKind Kind { get; set; }
    public string Title { get => TitleText.Text; set => TitleText.Text = value; }
    public string Footer { get => FooterText.Text; set => FooterText.Text = value; }

    public ApiKeyCard()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            if (DataContext is ProviderState state && state.Kind == Kind) return;
            var provider = App.Current.Model.Providers[Kind];
            DataContext = provider;
            Picker.Provider = provider;
            Picker.Hint = $"Leave empty to use {provider.BuiltInModel}.";
            KeyLinkText.Text = $"Get your {Kind.ShortName()} API key";
            SaveButton.IsEnabled = false;
        };
    }

    private void Save()
    {
        if (string.IsNullOrWhiteSpace(KeyBox.Password)) return;
        App.Current.Model.SetApiKey(Kind, KeyBox.Password);
        KeyBox.Clear();
        _ = App.Current.Model.RefreshModelsAsync(Kind);
    }

    private void Save_Click(object sender, RoutedEventArgs e) => Save();

    private void KeyBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) Save();
    }

    private void KeyBox_PasswordChanged(object sender, RoutedEventArgs e) =>
        SaveButton.IsEnabled = !string.IsNullOrWhiteSpace(KeyBox.Password);

    private void Remove_Click(object sender, RoutedEventArgs e) => App.Current.Model.SetApiKey(Kind, null);

    private void KeyLink_Click(object sender, RoutedEventArgs e)
    {
        if (Kind.KeyPageUrl() is { } url) WindowInfo.OpenUrl(url);
    }
}
