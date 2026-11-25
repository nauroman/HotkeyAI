using System.Windows;

namespace HotkeyAI.Views;

public enum WelcomeChoice { Closed, SignIn, OtherProviders }

public partial class WelcomeWindow : Window
{
    public WelcomeWindow()
    {
        InitializeComponent();
    }

    public WelcomeChoice Choice { get; private set; } = WelcomeChoice.Closed;

    /// Startup stays off unless the user ticks the box (Store policy: no silent auto-start).
    public bool OpenAtSignIn => StartupBox.IsChecked == true;

    private void SignIn_Click(object sender, RoutedEventArgs e)
    {
        Choice = WelcomeChoice.SignIn;
        Close();
    }

    private void Other_Click(object sender, RoutedEventArgs e)
    {
        Choice = WelcomeChoice.OtherProviders;
        Close();
    }
}
