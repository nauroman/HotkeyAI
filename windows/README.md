# HotkeyAI for Windows

The Windows edition of HotkeyAI: a notification-area app that rewrites the text you are typing,
in any app, with one shortcut. Same actions, prompts and providers as the Mac app
(and the same prompts as the original [ChatGPTHotkey](https://github.com/nauroman/ChatGPTHotkey)
script), as a self-contained app for the Microsoft Store.

| Mac | Windows |
| --- | --- |
| Menu bar icon | Notification-area icon (left click: Settings, right click: actions and Quit) |
| ⌥⌘E / ⌥⇧⌘E | **Win+Alt+E** / **Win+Alt+Shift+E** — the same two keys left of the space bar. Ctrl+Alt is avoided: it is AltGr on many European layouts. |
| ⌘A ⌘C ⌘V via CGEvent | Ctrl+A Ctrl+C Ctrl+V via SendInput. No permission needed; Windows blocks it only for apps running as administrator. |
| Apple Intelligence (default) | Not available. **ChatGPT sign-in** is the default; OpenAI, Claude and Gemini keys work as on the Mac. |
| Keychain | DPAPI-encrypted files (only the signed-in Windows user can read them) |
| SMAppService login item | MSIX startup task (off until the user turns it on) |
| Banner at the top of the screen | Banner above the taskbar, never takes focus |

Also Windows-specific: the clipboard snapshot keeps every data format (text, images, files, rich
text), HotkeyAI's own paste is kept out of clipboard history (Win+V), the Win/Alt key release
after a shortcut can't open Start or an app's menu bar, and launching HotkeyAI again opens Settings in
the running copy.

## Layout

```
windows/
  src/HotkeyAI.Core/     prompts, settings, AI providers, Sign in with ChatGPT (any OS)
  src/HotkeyAI/          the WPF app: tray, hotkeys, clipboard, banner, settings
  tests/HotkeyAI.Core.Tests/   unit tests (run on macOS, Linux or Windows)
  tests/HotkeyAI.EndToEnd/     drives the real app on a Windows desktop (CI)
  packaging/             MSIX manifest, logos, Build-Msix.ps1, Test-Msix.ps1
  store/                 Partner Center texts, certification notes, listing images
```

## Build and test

On any OS with the .NET 10 SDK:

```bash
dotnet test windows/tests/HotkeyAI.Core.Tests
dotnet build windows/src/HotkeyAI -c Release      # builds the Windows app on a Mac too
```

On Windows:

```powershell
dotnet run --project windows\src\HotkeyAI                     # run from source
.\windows\packaging\Build-Msix.ps1                            # test MSIX packages + bundle in build\msix
dotnet run --project windows\tests\HotkeyAI.EndToEnd -- build\msix-work\x64\HotkeyAI.exe
```

A build run outside the package keeps its settings in `%LOCALAPPDATA%\HotkeyAI` and uses the
per-user Run key for "Open at sign-in". `HotkeyAI.exe --self-test <folder>` checks hotkeys,
clipboard, DPAPI and the windows, and saves pictures of the settings pages.

The GitHub workflow `.github/workflows/windows.yml` runs all of this on `windows-2022`: unit tests,
MSIX build for x64 and arm64, the self-test, the end-to-end test (presses Win+Alt+E in a real text
box against a local stand-in for the OpenAI API), and installs, launches and removes the package.

Icons are drawn from `Sources/HotkeyAI/IconArt.swift`, like the Mac icons. To regenerate them on a
Mac: `windows/packaging/make-assets.sh`.

## Microsoft Store

The product is reserved in Partner Center (Rotorlash Labs, Store ID 9P6DQ3CCTWFX). Every CI run
builds the Store package as the **HotkeyAI-Store** artifact. [store/SUBMISSION.md](store/SUBMISSION.md)
has the identity, the listing texts and the certification notes.
