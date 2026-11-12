# Legal Notice

## Disclaimer

MUTEit is provided **"as is"**, without warranty of any kind, express or implied, including but not limited to the warranties of merchantability, fitness for a particular purpose and non-infringement (see the MIT License in [LICENSE.md](LICENSE.md)).

To the extent permitted by applicable law, the author is not liable for any damages arising from the use of MUTEit. Mandatory statutory liability (e.g. for intent or gross negligence, fraudulently concealed defects, or under product liability law) remains unaffected.

## Microphone state

- **Do not rely on MUTEit alone in sensitive situations.** The mute state is set through the Windows audio system. Programs that open the microphone exclusively, use their own drivers or bypass the Windows mixer, as well as hardware with its own mute logic, may behave differently. Check the state in the program you use (e.g. the meeting app) when it matters.
- Other programs, drivers or hardware buttons can change the state at any time; MUTEit shows such changes but does not prevent them.
- When MUTEit quits, the microphone keeps the state chosen under *When MUTEit quits*. After a forced kill or a crash the last state may remain.

## Keyboard / mouse hook

- MUTEit uses global low-level hooks of Windows for the hotkey. With *Swallow the hotkey* switched on, the hotkey does not reach other programs; some keys or mouse buttons may then not work as expected in other programs.
- Security software may report programs with keyboard hooks. MUTEit only compares keys with the configured hotkey and neither records nor transmits them (see [PRIVACY.md](PRIVACY.md)).

## Desktop overlay

The overlay is drawn above other windows. It does not appear above applications in exclusive full screen.

## Operator responsibility

On shared or managed PCs, the operator is responsible for installing MUTEit, for its compliance with company policies and for informing other users of the PC. Group policies of an organisation take precedence.
