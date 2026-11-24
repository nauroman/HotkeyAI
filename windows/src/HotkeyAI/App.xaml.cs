using System.Windows;
using System.Windows.Threading;
using HotkeyAI.Core.Providers;
using HotkeyAI.Native;
using HotkeyAI.Platform;
using HotkeyAI.Views;

namespace HotkeyAI;

public partial class App : Application
{
    private const string InstanceName = @"Local\HotkeyAI.SingleInstance";
    private const string ShowSettingsSignal = @"Local\HotkeyAI.ShowSettings";

    private Mutex? _instance;
    private EventWaitHandle? _showSignal;
    private MessageWindow? _window;
    private HotkeyManager? _hotkeys;
    private ForegroundTracker? _tracker;
    private TrayIcon? _tray;
    private SettingsWindow? _settings;
    private volatile bool _exiting;

    internal AppModel Model { get; private set; } = null!;
    internal Hud Hud { get; } = new();
    internal ActionRunner Runner { get; private set; } = null!;

    internal static new App Current => (App)Application.Current;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // One HotkeyAI per Windows session. Starting it again (e.g. from Start) opens Settings
        // in the running copy.
        bool isFirst;
        try
        {
            _instance = new Mutex(true, InstanceName, out isFirst);
            _showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, ShowSettingsSignal);
        }
        catch (UnauthorizedAccessException)
        {
            // The running copy was started as administrator; this one can't even open its objects.
            _instance = null;
            Shutdown();
            return;
        }
        if (!isFirst)
        {
            Win32.AllowSetForegroundWindow(Win32.ASFW_ANY);
            _showSignal.Set();
            _showSignal.Dispose();
            _showSignal = null;
            _instance.Dispose();
            _instance = null;
            Shutdown();
            return;
        }

        DispatcherUnhandledException += (_, args) =>
        {
            Log.Error("Unhandled", args.Exception);
            Hud.Show("Something went wrong in HotkeyAI. Details were saved to error.log.", HudStyle.Error);
            args.Handled = true;
        };
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Log.Error("Unobserved task", args.Exception);
            args.SetObserved();
        };

        KeyLabels.Install();
        SystemTheme.AllowDarkMenus();
        var dataDirectory = SettingsStore.DefaultDirectory;
        Model = new AppModel(new SettingsStore(dataDirectory), new DpapiSecretStore(dataDirectory));
        Model.ChatGPT.OpenBrowser = url =>
        {
            WindowInfo.OpenUrl(url);
            return true;
        };
        Model.ChatGPT.ActivateApp = () => _settings?.Activate();

        _window = new MessageWindow();
        ClipboardService.Owner = _window.Handle;
        _hotkeys = new HotkeyManager(_window);
        _tracker = new ForegroundTracker();
        Hud.Enabled = Model.ShowHud;
        Runner = new ActionRunner(Model, Hud, _tracker);
        _tray = new TrayIcon(Model, Runner, _window, () => ShowSettings(), Quit);

        Model.HotkeysChanged += (_, _) => RegisterHotkeys();
        Model.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(AppModel.ShowHud)) Hud.Enabled = Model.ShowHud;
        };
        RegisterHotkeys();
        ListenForSecondLaunch();

        var selfTest = Array.IndexOf(e.Args, "--self-test");
        if (selfTest >= 0)
        {
            _ = SelfTest.RunAsync(this, e.Args.ElementAtOrDefault(selfTest + 1) ?? "self-test");
            return;
        }
        if (Model.IsFirstLaunch)
            Dispatcher.BeginInvoke(ShowWelcome, DispatcherPriority.ApplicationIdle);
        else if (!PackageInfo.LaunchedAtStartup(e.Args))
            ShowSettings();
    }

    private void ListenForSecondLaunch()
    {
        var signal = _showSignal!;
        new Thread(() =>
        {
            while (signal.WaitOne())
            {
                if (_exiting) return;
                Dispatcher.BeginInvoke(() => ShowSettings());
            }
        })
        { IsBackground = true, Name = "HotkeyAI second launch" }.Start();
    }

    internal void RegisterHotkeys()
    {
        if (_hotkeys is null) return;
        if (Model.HotkeysSuspended)
        {
            _hotkeys.UnregisterAll();
            return;
        }
        var seen = new HashSet<KeyCombo>();
        var bindings = new List<(KeyCombo, Action)>();
        foreach (var action in Model.Actions)
        {
            if (!action.Enabled || action.Shortcut is not { } combo || !seen.Add(combo)) continue;
            var id = action.Id;
            bindings.Add((combo, () => Runner.Perform(id)));
        }
        Model.FailedShortcuts = _hotkeys.SetBindings(bindings);
    }

    internal void ShowSettings(SettingsTab? tab = null)
    {
        if (_settings is null)
        {
            _settings = new SettingsWindow(Model);
            _settings.Closed += (_, _) =>
            {
                _settings = null;
                // Make sure hotkeys come back if the window closes mid-recording.
                Model.HotkeysSuspended = false;
            };
        }
        if (tab is { } t) _settings.SelectTab(t);
        _settings.Show();
        if (_settings.WindowState == WindowState.Minimized) _settings.WindowState = WindowState.Normal;
        _settings.Activate();
    }

    internal SettingsWindow? SettingsWindow => _settings;

    private void ShowWelcome()
    {
        var welcome = new WelcomeWindow();
        welcome.Closed += async (_, _) =>
        {
            Model.MarkOnboarded();
            if (welcome.OpenAtSignIn)
            {
                try { await StartupRegistration.SetEnabledAsync(true); }
                catch (Exception error) { Log.Error("Enable startup", error); }
            }
            // Nothing works until an AI provider is connected, so continue there.
            ShowSettings(SettingsTab.Providers);
            if (welcome.Choice == WelcomeChoice.SignIn) await Model.ChatGPT.SignInAsync();
        };
        welcome.Show();
        welcome.Activate();
    }

    internal void Quit()
    {
        _exiting = true;
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _exiting = true;
        try
        {
            if (Model is not null) Model.SaveNow();
        }
        catch (Exception error)
        {
            Log.Error("Saving settings on exit", error);
        }
        _tray?.Dispose();
        _hotkeys?.Dispose();
        _tracker?.Dispose();
        _window?.Dispose();
        if (_instance is not null)
        {
            _showSignal?.Set(); // wakes our listener thread so it can end
            _instance.ReleaseMutex();
            _instance.Dispose();
        }
        base.OnExit(e);
    }
}
