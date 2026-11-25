using System.Windows;

namespace HotkeyAI.Views;

public enum SettingsTab { Actions, Providers, General }

public partial class SettingsWindow : Window
{
    internal SettingsWindow(AppModel model)
    {
        InitializeComponent();
        ActionsPage.Attach(model);
        ProvidersPage.Attach(model);
        GeneralPage.Attach(model);
    }

    internal void SelectTab(SettingsTab tab) => Tabs.SelectedItem = tab switch
    {
        SettingsTab.Providers => ProvidersTab,
        SettingsTab.General => GeneralTab,
        _ => ActionsTab,
    };
}
