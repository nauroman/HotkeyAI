# HotkeyAI for macOS

**Website: [nauroman.github.io/HotkeyAI](https://nauroman.github.io/HotkeyAI/)**, with downloads for Mac and Windows, setup and help.

A menu bar app that rewrites the text you are typing, in any app, with one shortcut.
It is the macOS successor of [ChatGPTHotkey](https://github.com/nauroman/ChatGPTHotkey):
the same two prompts (translate into polished English, edit in the original language),
plus your own shortcuts and prompts, several AI providers and "Sign in with ChatGPT".

## What it does

- Lives in the menu bar (no Dock icon) and can open automatically at login.
- **Actions**: each action has a name, a global shortcut, a prompt, an AI provider/model
  and what text it takes (whole field, selection, or selection-or-field). Add as many as you like.
- Defaults, chosen for a MacBook keyboard (no F13; ⌥⌘ is one thumb press):
  - **⌥⌘E** – improve the text in natural English, translating it if it is in another language
    (the Windows tool's prompt, unchanged);
  - **⌥⇧⌘E** – correct it in the language you typed, keeping your own wording and register;
  - *Polish elegantly* – the Windows tool's literary rewrite, no shortcut by default.
  ⌃⌥ + letter is avoided because Rectangle and Magnet use it for window layouts.
- Press the shortcut in any text field → HotkeyAI copies the text (⌘A ⌘C), sends it to the AI,
  pastes the result (⌘V) and restores your clipboard (all types, not just text).
- If you switch apps while it is working, nothing is pasted; the result is left on the clipboard.
- A small non-activating banner shows progress and errors. Errors never replace your text.
- A **Try it** box in each action runs the prompt without touching other apps.

## AI providers

| Provider | How you connect | Notes |
| --- | --- | --- |
| **Apple Intelligence** (default) | Nothing | On-device model on macOS 26+. Free, offline, private; shorter texts. Russian isn't officially supported yet: it works, but results are weaker. |
| **ChatGPT** | **Sign in with ChatGPT** (OAuth, no key) | Uses your ChatGPT **Plus/Pro** plan. You set HotkeyAI's weekly share at chatgpt.com/settings/usage. |
| OpenAI API | API key | Billed separately on platform.openai.com. Default `gpt-5-nano`, minimal reasoning (same as the Windows tool). |
| Claude | Anthropic API key | Anthropic forbids third-party apps from using Claude Pro/Max sign-in (Feb 2026), so a key is the only allowed way. Default `claude-opus-5-5`, low effort. |
| Gemini | Google AI Studio key | Gemini OAuth needs every user to own a Google Cloud project, so a key is the practical option. Default `gemini-flash-latest`. |

Keys and tokens are stored in the Keychain. Text goes only to the provider chosen for the action.

### About "Sign in with ChatGPT"

The app implements OpenAI's documented public-client flow (<https://developers.openai.com/siwc>):
dynamic registration (`client_id=dynamic_agent_client`), PKCE S256, a loopback redirect to
`http://127.0.0.1:1455/auth/callback`, refresh-token rotation, revocation on sign-out, and
inference through `POST /v1/responses` with `store:false, stream:true`.

OpenAI currently offers this self-serve to **open-source and locally run apps**. A paid,
closed-source or hosted app must apply through OpenAI's interest form. If you publish this
code (as with ChatGPTHotkey) you are on the self-serve path; confirm with OpenAI before
shipping a closed-source App Store build.

## Download

Get `HotkeyAI-<version>.dmg` from [Releases](https://github.com/nauroman/HotkeyAI/releases/latest),
open it and drag HotkeyAI to Applications (macOS 14 or later). The app is signed with
Developer ID and notarized by Apple. `TEAM_ID=<your team ID> scripts/developer-id-release.sh`
makes that disk image from the same archive as the App Store build.

## Build and run (no Xcode required)

```bash
scripts/test.sh                 # 15 tests: parsing, prompts, icons, OAuth loopback listener
scripts/build-app.sh            # → build/HotkeyAI.app (sandboxed, ad-hoc signed)
open build/HotkeyAI.app
```

Move the app to /Applications for "Open at Login" to be reliable.

macOS ties the Accessibility permission to the app's signature. Run
`scripts/make-signing-cert.sh` once: it creates a self-signed "HotkeyAI Local Signing"
certificate in your login keychain, and `build-app.sh` then signs every build with it, so the
permission survives rebuilds. Without it the build is signed ad-hoc and must be re-approved
after every rebuild. To use another certificate: `SIGN_IDENTITY="Apple Development: …" scripts/build-app.sh`.

### Permission

To press ⌘A/⌘C/⌘V in other apps, macOS asks once for permission under
**System Settings → Privacy & Security → Accessibility**. If it is missing, HotkeyAI opens
that page itself (on first launch, from the menu's "Allow keyboard access…", or when a
shortcut is pressed), waits for the switch and confirms. If the switch is on but access
still isn't detected (typical after a rebuild), remove HotkeyAI with − and add it again. HotkeyAI only *posts* key events
(the sandbox-compatible "post event" privilege); it does not read the screen or other apps.

## Mac App Store

App Store Connect only accepts builds made with a release version of Xcode, so the store
build uses `HotkeyAI.xcodeproj` (same sources; the SwiftPM build stays for quick local work).

1. Install Xcode, open it once, and add your Apple ID in Xcode → Settings → Accounts.
2. In App Store Connect create the app (bundle ID `com.nauroman.HotkeyAI`). All texts, URLs,
   review notes and privacy answers are in [AppStore/SUBMISSION.md](AppStore/SUBMISSION.md).
3. `TEAM_ID=<your team ID> scripts/appstore-upload.sh` archives, signs and uploads the build.
   Bump `CFBundleVersion` in `Resources/Info.plist` before each new upload.
4. Add screenshots, pick the build and submit for review.

The support and privacy pages live in `docs/` and are served by GitHub Pages at
<https://nauroman.github.io/HotkeyAI/>.

Already in place: App Sandbox (`network.client`, `network.server` for the loopback sign-in),
hardened runtime, `LSUIElement`, SMAppService login item that is only enabled with the user's
consent (guideline 2.4.5(iii)), privacy manifest, 1024-px icon, weak-linked FoundationModels
so macOS 14–15 are supported.

**Main review risk – guideline 2.4.5.** Apple's engineers confirm that posting key events is
allowed in the sandbox, but App Review has rejected at least one clipboard app for using
`CGEvent.post` to paste. Explain in the review notes that the user explicitly triggers each
action with their own shortcut, and that the app only sends ⌘A/⌘C/⌘V to the focused field.
If review still refuses, the same build can be distributed outside the store with a
Developer ID signature and notarization, with no code changes.

## Icons

The app icon (a keycap with an AI sparkle) and the single-color menu bar glyph are drawn in
code in `Sources/HotkeyAI/IconArt.swift`. The menu bar uses a template image, so macOS
recolors it for light and dark menu bars; while an action runs, the sparkle turns into "•••".
`scripts/make-icons.sh` re-renders `Resources/AppIcon.icns`, `Resources/AppIcon-1024.png`
and `Resources/MenuBarIcon/` (PNG @1x–3x and PDF, for an Xcode asset catalog set to
"Template Image"). A preview sheet is written to `build/icons/preview.png`.

## Layout

```
Sources/HotkeyAI/
  HotkeyAIApp.swift      menu bar, app menu, first-run welcome
  AppState.swift         settings, hotkey bindings, the capture → AI → paste pipeline
  HotkeyManager.swift    Carbon RegisterEventHotKey (no permission needed, sandbox-safe)
  TextBridge.swift       ⌘A/⌘C/⌘V posting, clipboard snapshot/restore
  ShortcutRecorder.swift, KeyNames.swift, HUD.swift, Keychain.swift, LoginItem.swift
  Providers/             ChatGPT sign-in + loopback server, OpenAI, Anthropic, Gemini, Apple
  Views/                 Settings window: Actions, AI Providers, General
Resources/               Info.plist, entitlements, privacy manifest, icon
```

## Known limitations

- Rich formatting is not preserved; the result is pasted as plain text.
- In editors that copy the current line when nothing is selected (VS Code, JetBrains),
  use "Whole field" or "Selected text only" rather than "Selection, or whole field".
- UI is English only for now.

## Support HotkeyAI

HotkeyAI is free. If it saves you time, you can [leave a tip on Ko-fi](https://ko-fi.com/nauroman). It pays for the Apple developer membership and for time to keep HotkeyAI updated.

## License

MIT, see [LICENSE](LICENSE).
