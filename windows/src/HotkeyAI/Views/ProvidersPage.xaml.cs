using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using HotkeyAI.Core.Providers;
using HotkeyAI.Platform;

namespace HotkeyAI.Views;

public partial class ProvidersPage : UserControl
{
    private AppModel _model = null!;

    public ProvidersPage()
    {
        InitializeComponent();
    }

    public AppModel Model => _model;

    public IReadOnlyList<Choice> ProviderChoices { get; } =
        ProviderInfo.All.Select(k => new Choice(k, k.DisplayName())).ToList();

    internal void Attach(AppModel model)
    {
        _model = model;
        DataContext = this;
        var chatGPT = model.Providers[ProviderKind.ChatGPT];
        ChatGPTModel.Provider = chatGPT;
        ChatGPTModel.SetBinding(ModelPicker.TextProperty, new Binding(nameof(ProviderState.DefaultModel)) { Source = chatGPT });
    }

    private async void SignIn_Click(object sender, RoutedEventArgs e) => await _model.ChatGPT.SignInAsync();

    private async void AllowPlan_Click(object sender, RoutedEventArgs e) => await _model.ChatGPT.SignInAsync(requestConsent: true);

    private void CancelSignIn_Click(object sender, RoutedEventArgs e) => _model.ChatGPT.CancelSignIn();

    private async void SignOut_Click(object sender, RoutedEventArgs e) => await _model.ChatGPT.SignOutAsync();

    private void ManageUsage_Click(object sender, RoutedEventArgs e) => WindowInfo.OpenUrl(ChatGPTAuth.ManageUsageUrl);
}
