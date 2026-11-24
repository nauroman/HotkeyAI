# Microsoft Store submission — HotkeyAI for Windows 1.0

Everything to paste into Partner Center. Publisher account: the one that already
publishes ReasonKey and Arrow Keys. Field lengths are checked by `windows/store/check-listing.py`.

## 1. Product (reserved 2026-11-23)

| Partner Center | Value |
| --- | --- |
| Product name | HotkeyAI |
| Store ID | 9P6DQ3CCTWFX — https://apps.microsoft.com/detail/9P6DQ3CCTWFX |
| Package/Identity/Name | RotorlashLabs.HotkeyAI |
| Package/Identity/Publisher | CN=0EB9C82F-5B55-416E-AF5F-023ED1301555 |
| Package/Properties/PublisherDisplayName | Rotorlash Labs |
| Package family name | RotorlashLabs.HotkeyAI_5mfc7h91dnj4r |

These values are in `windows/packaging/StoreIdentity.json` (they are public: every installed
package carries them). The name must be submitted within three months of the reservation.

## 2. The package

Every run of the **Windows** workflow builds `HotkeyAI_<version>.msixbundle` (x64 + arm64)
with this identity and uploads it as the **HotkeyAI-Store** artifact, after the tests pass.
On a Windows PC: `.\windows\packaging\Build-Msix.ps1 -Store -OutputDirectory build\store`.
Upload the `.msixbundle` in Partner Center → Packages. It is unsigned on purpose: the Store
signs it. `build.json` next to it records the identity, version and SHA-256 of every file.
For an update, raise `<Version>` in `windows/Directory.Build.props` first.

## 3. Pricing and availability

Free · all markets · public · no time limit.

## 4. Properties

| Field | Value |
| --- | --- |
| Category | Productivity (subcategory: none; secondary: Utilities + tools if offered) |
| Privacy policy URL | https://nauroman.github.io/HotkeyAI/privacy.html |
| Website | https://nauroman.github.io/HotkeyAI/ |
| Support contact | https://github.com/nauroman/HotkeyAI/issues |
| Product declarations | ☐ Kinect, ☐ pen; "This app has been tested to meet accessibility guidelines" — leave unchecked unless tested |
| System requirements | Minimum: keyboard. Recommended: internet connection |

## 5. Age ratings (IARC questionnaire)

Category: Utility/productivity. Answer **No** to violence, sexuality, gambling, drugs, language,
purchases, location sharing and personal-info sharing. Users don't interact with each other.
If asked whether the app contains or generates AI content: **Yes** — it rewrites the user's own
text with an AI provider the user chooses; it has no web browser and no chat with other people.

## 6. Packages

Upload the `.msixbundle`. Device families: Desktop only.

## 7. Store listing — English (United States)

**Product name**
HotkeyAI

**Short description** (≤ 270)
Rewrite the text you are typing, in any app, with one shortcut: turn a draft into clear, natural English, or fix grammar and spelling in your own language.

**Description** (≤ 10000)
HotkeyAI improves the text you are typing without leaving the app you are in. Press a shortcut in any text field — Outlook, Teams, Slack, Word, a browser — and HotkeyAI replaces your draft with a clearer version.

TWO ACTIONS, READY TO GO
• Win+Alt+E — Improve in English: translates from any language into natural, fluent English, or polishes text that is already in English.
• Win+Alt+Shift+E — Fix in your language: corrects spelling, grammar and punctuation while keeping your own words and tone.

MAKE IT YOURS
• Add your own actions, each with a name, a shortcut and a prompt: "make it shorter", "make it polite", "turn this into bullet points".
• Choose what each action works on: the whole field, the selection, or the selection when there is one.
• Try any prompt in Settings before you use it.

YOUR AI, YOUR ACCOUNT
• Sign in with your ChatGPT Plus or Pro account, or use your own OpenAI, Anthropic (Claude) or Google Gemini API key.
• Your text goes straight from your PC to the provider you chose. Keys and sign-in tokens are encrypted for your Windows account. No account with us, no analytics, no tracking.

MADE FOR WINDOWS
• Lives in the notification area and can open when you sign in.
• Restores your clipboard after every action and keeps its own paste out of clipboard history.
• If you switch windows while it is working, nothing is replaced: the result is left on the clipboard.

HotkeyAI presses Ctrl+A, Ctrl+C and Ctrl+V for you, only in the field you are typing in and only when you press one of your shortcuts. Windows doesn't let apps type into programs running as administrator, so HotkeyAI can't change text there.

An AI provider is required: a ChatGPT Plus or Pro plan, or an API key from OpenAI, Anthropic or Google (Google AI Studio offers free keys with a daily limit). HotkeyAI is open source: github.com/nauroman/HotkeyAI

**What's new in this version**
(empty for the first release, as Partner Center suggests)

**Product features** (≤ 20, each ≤ 200)
- Win+Alt+E turns your draft into clear, natural English, translating from any language
- Win+Alt+Shift+E fixes spelling, grammar and punctuation in the language you typed
- Your own actions with their own shortcuts and prompts
- Works on the whole field, the selection, or the selection when there is one
- Sign in with ChatGPT, or use an OpenAI, Claude or Gemini API key
- Restores your clipboard after every action
- Keys and tokens encrypted for your Windows account; no analytics
- Open source

**Search terms** (≤ 7, each ≤ 30)
grammar · translate · proofread · rewrite · spelling · AI writing · hotkey

**Copyright and trademark info**
© 2026 nauroman

**Additional license terms**
MIT License — https://github.com/nauroman/HotkeyAI/blob/main/LICENSE

**Developed by**
nauroman

**Screenshots** (desktop, PNG, 1920×1080): `windows/store/screenshots/1-welcome.png`,
`2-actions.png`, `3-providers.png`. They frame the real windows captured on Windows by the CI
self-test (HotkeyAI-tests artifact); rebuild them with `windows/store/make-screenshots.sh`.

**Store logos**
9:16 poster art: `windows/store/images/PosterArt-1440x2160.png`; 1:1 box art:
`windows/store/images/BoxArt-1080x1080.png` (both from `windows/packaging/make-assets.sh`).

## 8. Notes for certification (Additional Testing Information)

```
HotkeyAI is an open-source, full-trust Win32 tray utility (WPF, .NET 10) packaged as MSIX. It rewrites the text in the focused text field when the user presses one of their own global shortcuts.

How to test:
1. Launch HotkeyAI from Start. A welcome window explains the shortcuts. The "Open HotkeyAI when I sign in to Windows" box is unticked by default; the startup task is only enabled if the user ticks it (or turns it on in Settings → General).
2. HotkeyAI needs an AI provider. Use the Google Gemini test API key from the Credentials section of this page: in Settings > AI Providers, paste it under "Google Gemini", press Save, then set "Default provider" at the top of that page to "Gemini (Google API key)". ("Sign in with ChatGPT" also works with any ChatGPT Plus or Pro account.)
3. In Settings → Actions, choose "Improve in English" and press "Run on this text" in the Try it box. The Russian sample is translated into English.
4. Open Notepad, type "we has two ideas and i thinks they is good", and press Win+Alt+E. The text is replaced with corrected English and a banner above the taskbar says "Done". Win+Alt+Shift+E corrects text in its own language.
5. The keycap icon in the notification area (it may be under ^) opens Settings with a left click; a right click shows the actions and Quit.

Restricted capability runFullTrust: HotkeyAI is a Win32 desktop app. Full trust is used only to (a) register the user's global shortcuts with RegisterHotKey, (b) send Ctrl+A, Ctrl+C and Ctrl+V with SendInput to the focused window after the user presses one of those shortcuts, (c) read and restore the clipboard around that copy and paste, and (d) show a notification-area icon. It runs at the user's normal privilege level, does not install drivers or services, does not use a keyboard hook, does not read the screen and does not record typing.

Network: HTTPS requests go only to the AI provider the user configured (api.openai.com, auth.openai.com for "Sign in with ChatGPT", api.anthropic.com or generativelanguage.googleapis.com). "Sign in with ChatGPT" opens the system browser and receives the OAuth redirect on a temporary listener at http://127.0.0.1:1455/auth/callback. The publisher runs no server and collects no data; API keys and tokens are stored encrypted with DPAPI in the package's local app data.

Source code and privacy policy: https://github.com/nauroman/HotkeyAI and https://nauroman.github.io/HotkeyAI/privacy.html
```

These notes go to Partner Center → Supplemental info → **Additional Testing Information**
(Description). The test key itself goes in **Credentials** on the same page, never in the text:
a free Google AI Studio key created only for certification, revoked after publication.

## 9. After certification

- Install the public build on a clean Windows user profile; check first launch, both shortcuts in
  Notepad and Word, the tray menu, open-at-sign-in on and off, and uninstall.
- Revoke the certification test key once the app is published.
