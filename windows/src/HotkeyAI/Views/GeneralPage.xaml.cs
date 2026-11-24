using System.Windows;
using System.Windows.Controls;
using HotkeyAI.Platform;

namespace HotkeyAI.Views;

public partial class GeneralPage : UserControl
{
    public const string PrivacyUrl = "https://nauroman.github.io/HotkeyAI/privacy.html";
    public const string SourceUrl = "https://github.com/nauroman/HotkeyAI";
    public const string IssuesUrl = "https://github.com/nauroman/HotkeyAI/issues";

    public GeneralPage()
    {
        InitializeComponent();
        IsVisibleChanged += async (_, _) =>
        {
            if (IsVisible) await RefreshStartupAsync();
        };
    }

    internal void Attach(AppModel model)
    {
        DataContext = model;
        VersionText.Text = $"HotkeyAI for Windows {PackageInfo.Version}" + (PackageInfo.IsPackaged ? "" : " (not installed from the Store)");
    }

    private async Task RefreshStartupAsync()
    {
        try
        {
            Show(await StartupRegistration.GetStateAsync());
        }
        catch (Exception e)
        {
            Log.Error("Reading startup state", e);
            StartupBox.IsEnabled = false;
        }
    }

    private void Show(StartupState state)
    {
        StartupBox.IsChecked = state is StartupState.Enabled or StartupState.EnabledByPolicy;
        StartupBox.IsEnabled = state is StartupState.Enabled or StartupState.Disabled;
        StartupBlocked.Visibility = state is StartupState.DisabledByUser or StartupState.DisabledByPolicy
            ? Visibility.Visible : Visibility.Collapsed;
        StartupBlockedText.Text = state == StartupState.DisabledByPolicy
            ? "Your organization turned this off."
            : "You turned HotkeyAI off in Windows Settings → Apps → Startup. Turn it on there.";
    }

    private async void Startup_Click(object sender, RoutedEventArgs e)
    {
        StartupError.Visibility = Visibility.Collapsed;
        try
        {
            Show(await StartupRegistration.SetEnabledAsync(StartupBox.IsChecked == true));
        }
        catch (Exception error)
        {
            Log.Error("Changing startup", error);
            StartupError.Text = "Couldn't change the startup setting: " + error.Message;
            StartupError.Visibility = Visibility.Visible;
            await RefreshStartupAsync();
        }
    }

    private void OpenStartupSettings_Click(object sender, RoutedEventArgs e) => WindowInfo.OpenUrl("ms-settings:startupapps");
    private void Privacy_Click(object sender, RoutedEventArgs e) => WindowInfo.OpenUrl(PrivacyUrl);
    private void Source_Click(object sender, RoutedEventArgs e) => WindowInfo.OpenUrl(SourceUrl);
    private void Issues_Click(object sender, RoutedEventArgs e) => WindowInfo.OpenUrl(IssuesUrl);
}
