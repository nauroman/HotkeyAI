# Mac App Store submission — HotkeyAI 1.0

Everything to paste into App Store Connect. Character limits are checked by
`scripts/check-metadata.py`.

## App record (App Store Connect → Apps → +  → New App)

| Field | Value |
| --- | --- |
| Platform | macOS |
| Name | HotkeyAI |
| Primary language | English (U.S.) |
| Bundle ID | com.nauroman.HotkeyAI (register it first in developer.apple.com → Identifiers, or let Xcode create it) |
| SKU | hotkeyai-mac-1 |
| User access | Full access |
| Price | Free (all territories) |
| Category | Productivity (secondary: Utilities) |
| Copyright | 2026 nauroman |
| Support URL | https://nauroman.github.io/HotkeyAI/ |
| Marketing URL | https://nauroman.github.io/HotkeyAI/ |
| Privacy Policy URL | https://nauroman.github.io/HotkeyAI/privacy.html |

## English (U.S.)

**Subtitle** (30)
Rewrite text with one shortcut

**Promotional text** (170)
Press ⌥⌘E in any text field to turn your draft into clear, natural English, or ⌥⇧⌘E to fix it in your own language. Runs on your Mac with Apple Intelligence.

**Keywords** (100)
translate,grammar,proofread,rewrite,writing,english,spelling,editor,polish,shortcut,menu bar,text,AI

**Description** (4000)
HotkeyAI improves the text you are typing without leaving the app you are in. Press a shortcut in any text field — Mail, Messages, Notes, Slack, a browser — and HotkeyAI replaces your draft with a clearer version.

TWO ACTIONS, READY TO GO
• ⌥⌘E — Improve in English: translates from any language into natural, fluent English, or polishes text that is already in English.
• ⌥⇧⌘E — Fix in your language: corrects spelling, grammar and punctuation while keeping your own words and tone.

MAKE IT YOURS
• Add your own actions, each with a name, a shortcut and a prompt: "make it shorter", "make it polite", "turn this into bullet points".
• Choose what each action works on: the whole field, the selection, or the selection when there is one.
• Try any prompt in Settings before you use it.

PRIVATE BY DEFAULT
• Works out of the box with Apple Intelligence on your Mac: free, offline, and your text never leaves the device.
• Prefer a cloud model? Sign in with your ChatGPT account, or use your own OpenAI, Anthropic or Google Gemini API key.
• Keys and sign-in tokens stay in your Keychain. No account with us, no analytics, no tracking.

MADE FOR THE MAC
• Lives in the menu bar and can open at login.
• Restores your clipboard after every action.
• If you switch apps while it is working, nothing is replaced: the result is left on the clipboard.

HotkeyAI asks for permission to press ⌘A, ⌘C and ⌘V for you (System Settings → Privacy & Security → Accessibility). It sends those keys only to the field you are typing in, and only when you press one of your shortcuts.

Apple Intelligence requires a compatible Mac with macOS 26 or later and does not support every language yet. ChatGPT sign-in requires a ChatGPT Plus or Pro plan. HotkeyAI is open source: github.com/nauroman/HotkeyAI

**What's New** — first release, leave empty.

## Russian (add localization → Russian)

**Subtitle** (30)
Текст лучше одним нажатием

**Promotional text** (170)
Нажмите ⌥⌘E в любом текстовом поле — и черновик станет ясным английским текстом, а ⌥⇧⌘E исправит его на вашем языке. Работает на Mac с Apple Intelligence.

**Keywords** (100)
перевод,грамматика,орфография,английский,текст,редактор,исправить,переписать,ИИ,горячие клавиши

**Description** (4000)
HotkeyAI улучшает текст, который вы набираете, прямо в том приложении, где вы пишете. Нажмите сочетание клавиш в любом текстовом поле — в Почте, Сообщениях, Заметках, Slack или браузере, — и HotkeyAI заменит черновик более ясным вариантом.

ДВА ДЕЙСТВИЯ СРАЗУ
• ⌥⌘E — улучшить по-английски: переводит с любого языка на естественный английский или улучшает текст, уже написанный по-английски.
• ⌥⇧⌘E — исправить на вашем языке: исправляет орфографию, грамматику и пунктуацию, сохраняя ваши слова и тон.

НАСТРОЙТЕ ПОД СЕБЯ
• Добавляйте свои действия — у каждого своё название, сочетание клавиш и промпт: «сделай короче», «сделай вежливее», «оформи списком».
• Выбирайте, с каким текстом работает действие: всё поле, выделенный фрагмент или выделение, если оно есть.
• Проверяйте любой промпт в настройках, прежде чем им пользоваться.

ПРИВАТНОСТЬ ПО УМОЛЧАНИЮ
• Сразу работает с Apple Intelligence на вашем Mac: бесплатно, без интернета, текст не покидает устройство.
• Нужна облачная модель? Войдите с аккаунтом ChatGPT или укажите свой API-ключ OpenAI, Anthropic или Google Gemini.
• Ключи и данные входа хранятся в вашей связке ключей. Без регистрации, аналитики и слежки.

СДЕЛАНО ДЛЯ MAC
• Живёт в строке меню и может запускаться при входе в систему.
• После каждого действия восстанавливает буфер обмена.
• Если во время работы вы переключились в другое приложение, текст не заменяется — результат остаётся в буфере обмена.

HotkeyAI попросит разрешение нажимать за вас ⌘A, ⌘C и ⌘V (Системные настройки → Конфиденциальность и безопасность → Универсальный доступ). Эти клавиши отправляются только в поле, где вы пишете, и только когда вы нажимаете своё сочетание.

Apple Intelligence требует совместимый Mac с macOS 26 или новее и пока поддерживает не все языки. Вход через ChatGPT требует подписку ChatGPT Plus или Pro. Исходный код HotkeyAI открыт: github.com/nauroman/HotkeyAI

## App Review information

Sign-in required: **No**. Notes:

```
HotkeyAI is a menu bar utility that rewrites the text in the focused text field when the user presses one of their own global shortcuts.

How to test:
1. Launch HotkeyAI (keycap icon in the menu bar). The welcome dialog asks whether to open at login; the login item is only enabled if the user agrees.
2. In TextEdit, type: "we has two ideas and i thinks they is good". Press Option-Command-E. The text is replaced with corrected English.
3. The first time, HotkeyAI opens System Settings → Privacy & Security → Accessibility. Turn on HotkeyAI and press the shortcut again.
4. Without granting the permission, every action can be tried in Settings → Actions → "Try it".

AI processing: by default HotkeyAI uses Apple Intelligence on-device (FoundationModels, macOS 26+), so no account is needed. Optional providers: "Sign in with ChatGPT" (OpenAI's OAuth flow; requires a ChatGPT Plus/Pro account) and the user's own OpenAI, Anthropic or Google Gemini API keys. HotkeyAI has no server and collects no data.

Use of the Accessibility permission: HotkeyAI only posts the keystrokes Command-A, Command-C and Command-V with CGEvent.post (the sandbox-compatible "post event" privilege) to the focused field, and only after the user presses one of their shortcuts. It does not use the Accessibility (AXUIElement) API, does not read the screen and does not monitor typing; shortcuts are registered with RegisterEventHotKey.

Network: the app is sandboxed with network.client (AI APIs) and network.server, which is used only for a temporary listener on 127.0.0.1 that receives the "Sign in with ChatGPT" OAuth redirect.
```

## App Privacy (App Store Connect → App Privacy)

Answer **"No, we do not collect data from this app."**
Reason: the developer has no server and receives nothing. Text goes from the user's Mac directly to
the AI provider the user picked, under the user's own account, only to fulfil the request; Apple
Intelligence runs on-device. Keys and tokens stay in the user's Keychain.

## Age rating

Answer "None" to every content question. If the questionnaire asks about generative AI or
unrestricted web access, answer truthfully: the app generates text with AI on the user's request
and has no web browser.

## Screenshots (required)

At least one, 16:10, one of: 1280×800, 1440×900, 2560×1600, 2880×1800 (PNG or JPEG).
Suggested: (1) a Notes window before/after ⌥⌘E with the banner, (2) Settings → Actions,
(3) Settings → AI Providers.
