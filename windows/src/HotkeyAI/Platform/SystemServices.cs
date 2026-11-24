using System.Runtime.InteropServices;
using System.Text;
using HotkeyAI.Native;
using Microsoft.Win32;
using Windows.ApplicationModel;
using Windows.ApplicationModel.Activation;

namespace HotkeyAI.Platform;

/// API keys and sign-in tokens, encrypted with Windows Data Protection (DPAPI) so only the
/// signed-in Windows user can read them. One file per secret in the app's data folder.
internal sealed class DpapiSecretStore(string directory) : ISecretStore
{
    private string PathFor(string account) => System.IO.Path.Combine(directory, "secrets", account + ".bin");

    public byte[]? Get(string account)
    {
        var path = PathFor(account);
        if (!File.Exists(path)) return null;
        try
        {
            return Transform(File.ReadAllBytes(path), protect: false);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            return null; // e.g. copied from another PC or user: treat as not set
        }
    }

    public void Set(string account, byte[]? value)
    {
        var path = PathFor(account);
        if (value is null)
        {
            if (File.Exists(path)) File.Delete(path);
            return;
        }
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        var temp = path + ".tmp";
        File.WriteAllBytes(temp, Transform(value, protect: true));
        File.Move(temp, path, overwrite: true);
    }

    private static byte[] Transform(byte[] data, bool protect)
    {
        var input = Marshal.AllocHGlobal(Math.Max(data.Length, 1));
        try
        {
            Marshal.Copy(data, 0, input, data.Length);
            var blob = new Win32.DATA_BLOB { cbData = data.Length, pbData = input };
            Win32.DATA_BLOB output;
            var ok = protect
                ? Win32.CryptProtectData(ref blob, "HotkeyAI", IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, Win32.CRYPTPROTECT_UI_FORBIDDEN, out output)
                : Win32.CryptUnprotectData(ref blob, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, Win32.CRYPTPROTECT_UI_FORBIDDEN, out output);
            if (!ok) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            try
            {
                var result = new byte[output.cbData];
                Marshal.Copy(output.pbData, result, 0, output.cbData);
                return result;
            }
            finally
            {
                Win32.LocalFree(output.pbData);
            }
        }
        finally
        {
            Marshal.FreeHGlobal(input);
        }
    }
}

internal static class PackageInfo
{
    /// True when running from the MSIX package (Microsoft Store install).
    public static bool IsPackaged { get; } = DetectPackage();

    private static bool DetectPackage()
    {
        var length = 0;
        return Win32.GetCurrentPackageFullName(ref length, null) != Win32.APPMODEL_ERROR_NO_PACKAGE;
    }

    public static string Version
    {
        get
        {
            if (IsPackaged)
            {
                var v = Package.Current.Id.Version;
                return $"{v.Major}.{v.Minor}.{v.Build}";
            }
            return Core.Providers.AppVersion.Short;
        }
    }

    /// True when Windows started HotkeyAI at sign-in (startup task or the Run key).
    public static bool LaunchedAtStartup(string[] args)
    {
        if (args.Contains("--startup")) return true;
        if (!IsPackaged) return false;
        try
        {
            return AppInstance.GetActivatedEventArgs()?.Kind == ActivationKind.StartupTask;
        }
        catch (Exception)
        {
            return false;
        }
    }
}

public enum StartupState { Disabled, Enabled, DisabledByUser, DisabledByPolicy, EnabledByPolicy }

/// "Open at sign-in". The Store package uses its declared startup task (off until the user
/// turns it on); a build run outside the package uses the per-user Run key.
internal static class StartupRegistration
{
    private const string TaskId = "HotkeyAIStartup";
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValue = "HotkeyAI";

    public static async Task<StartupState> GetStateAsync()
    {
        if (PackageInfo.IsPackaged) return Map((await StartupTask.GetAsync(TaskId)).State);
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(RunValue) is string ? StartupState.Enabled : StartupState.Disabled;
    }

    public static async Task<StartupState> SetEnabledAsync(bool enabled)
    {
        if (PackageInfo.IsPackaged)
        {
            var task = await StartupTask.GetAsync(TaskId);
            if (enabled) return Map(await task.RequestEnableAsync());
            task.Disable();
            return Map(task.State);
        }
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (enabled) key.SetValue(RunValue, $"\"{Environment.ProcessPath}\" --startup");
        else key.DeleteValue(RunValue, throwOnMissingValue: false);
        return enabled ? StartupState.Enabled : StartupState.Disabled;
    }

    private static StartupState Map(StartupTaskState state) => state switch
    {
        StartupTaskState.Enabled => StartupState.Enabled,
        StartupTaskState.DisabledByUser => StartupState.DisabledByUser,
        StartupTaskState.DisabledByPolicy => StartupState.DisabledByPolicy,
        StartupTaskState.EnabledByPolicy => StartupState.EnabledByPolicy,
        _ => StartupState.Disabled,
    };
}

internal static class SystemTheme
{
    private const string Personalize = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    private static bool ReadFlag(string name)
    {
        using var key = Registry.CurrentUser.OpenSubKey(Personalize);
        return key?.GetValue(name) is not int value || value != 0;
    }

    /// Apps (windows, banners) use the light theme.
    public static bool AppsUseLight => ReadFlag("AppsUseLightTheme");

    /// The taskbar and tray use the light theme.
    public static bool TaskbarUsesLight => ReadFlag("SystemUsesLightTheme");

    private delegate int SetPreferredAppModeFn(int mode);

    private delegate void FlushMenuThemesFn();

    /// Lets native menus (the tray menu) follow the dark theme. uxtheme exposes this only by
    /// ordinal; on Windows versions without it nothing happens and menus stay light.
    public static void AllowDarkMenus()
    {
        try
        {
            var uxtheme = Win32.LoadLibrary("uxtheme.dll");
            if (uxtheme == IntPtr.Zero) return;
            var setMode = Win32.GetProcAddress(uxtheme, new IntPtr(135));
            var flush = Win32.GetProcAddress(uxtheme, new IntPtr(136));
            if (setMode == IntPtr.Zero || flush == IntPtr.Zero) return;
            Marshal.GetDelegateForFunctionPointer<SetPreferredAppModeFn>(setMode)(1); // AllowDark
            Marshal.GetDelegateForFunctionPointer<FlushMenuThemesFn>(flush)();
        }
        catch (Exception)
        {
            // Purely cosmetic.
        }
    }
}

internal static class KeyLabels
{
    /// Label for punctuation keys from the active keyboard layout (e.g. "ж" vs ";").
    public static string? FromLayout(uint vk)
    {
        var ch = Win32.MapVirtualKey(vk, Win32.MAPVK_VK_TO_CHAR) & 0x7FFF;
        return ch is 0 or < 0x20 ? null : char.ToUpperInvariant((char)ch).ToString();
    }

    public static void Install() => KeyNames.LayoutResolver = FromLayout;
}

/// Errors that would otherwise go unnoticed in a tray app, kept next to the settings.
internal static class Log
{
    public static void Error(string context, Exception e)
    {
        try
        {
            var path = System.IO.Path.Combine(SettingsStore.DefaultDirectory, "error.log");
            Directory.CreateDirectory(SettingsStore.DefaultDirectory);
            if (File.Exists(path) && new FileInfo(path).Length > 512 * 1024) File.Delete(path);
            File.AppendAllText(path, $"{DateTime.Now:u} {context}: {e}{Environment.NewLine}", Encoding.UTF8);
        }
        catch (Exception)
        {
            // Logging must never take the app down.
        }
    }
}
