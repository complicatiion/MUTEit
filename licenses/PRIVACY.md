# Privacy

MUTEit is **local-first**:

- **No telemetry, no account, no cloud.**
- **MUTEit does not record, store or transmit audio.** It only switches the mute state and the level of your microphone through the Windows audio system; it never opens an audio stream of the microphone.
- **One optional network connection – the update check (off by default).** If you switch it on (Settings › General › Check for updates), MUTEit asks `api.github.com/repos/complicatiion/MUTEit/releases/latest` at most once a day, and when you click *Check now*. GitHub receives your IP address and the user agent `MUTEit/<version>` (GitHub's privacy statement applies). Nothing is downloaded or installed; a new version is only announced and linked.
- **Keyboard and mouse hook.** For the hotkey MUTEit watches the keyboard (and, only if a mouse button is the hotkey, the middle and side mouse buttons). Every key is only compared with your hotkey in memory and then forgotten. Nothing is logged, stored or sent.
- **No external resources.** Fonts, icons and sounds are embedded.
- Links on the *About* page (project page, sksdesign.de) open in your browser only when you click them.

## Data stored by MUTEit

| Location | Content |
| --- | --- |
| `%AppData%\MUTEit\settings.json` (portable: `data\settings.json`) | microphone choice (device ID and name), mode, hotkey, ignore / swallow switches, start and exit state, sound switches, output device (ID and name), sound volume, paths of own sound and icon files, overlay settings and position, taskbar button switch, Windows notifications switch, design, Mica and accent switches, language, update check switch, time and result of the last update check |
| `%AppData%\MUTEit\error.log` (portable: `data\error.log`) | only if the tray hits an unexpected error: time and exception text |
| `HKCU\Software\Microsoft\Windows\CurrentVersion\Run\MUTEit` | start with Windows (if enabled) |
| Windows jump list of `sksdesign.MUTEit.TaskbarSwitch` | the tasks *Settings* and *Exit MUTEit* (only while the taskbar button is on) |

The uninstaller asks whether to delete `%AppData%\MUTEit` and removes the autostart entry.
