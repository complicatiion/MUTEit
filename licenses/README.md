# Licenses

## MUTEit

MUTEit is released under the **MIT License** – see [LICENSE.md](LICENSE.md) (identical copy in the repository root).
Copyright © 2026 Sven Klocke (*sksdesign | complicatiion*).

| Document | Content |
| --- | --- |
| [LICENSE.md](LICENSE.md) | MIT License of MUTEit |
| [COPYRIGHT.md](COPYRIGHT.md) | Copyright and trademark notes |
| [LEGAL-NOTICE.md](LEGAL-NOTICE.md) | Disclaimer, limitation of liability, notes on microphone state, hooks and overlay |
| [PRIVACY.md](PRIVACY.md) | Privacy notes (local-first, no network, no telemetry) |

## Third-party components

Components shipped with MUTEit keep their own copyrights and licenses. The full license texts are in [third-party/](third-party/).

| Component | Version | License | Shipped as | File |
| --- | --- | --- | --- | --- |
| .NET Runtime incl. Windows Forms | 10.0 | MIT | self-contained runtime | [third-party/DOTNET.md](third-party/DOTNET.md) |
| Avalonia UI (incl. Fluent theme, Inter font package) | 11.3.14 | MIT | library (settings window) | [third-party/AVALONIA.md](third-party/AVALONIA.md) |
| SkiaSharp / HarfBuzzSharp (via Avalonia) | – | MIT (+ Skia BSD-3-Clause, HarfBuzz MIT-style) | native libraries | [third-party/SKIASHARP.md](third-party/SKIASHARP.md) |
| CommunityToolkit.Mvvm | 8.4.0 | MIT | library | [third-party/COMMUNITYTOOLKIT-MVVM.md](third-party/COMMUNITYTOOLKIT-MVVM.md) |
| Material Design Icons (Pictogrammers) | 7.4.47 | Apache-2.0 | vector icons (window) and rendered masks (tray menu) | [third-party/MATERIAL-DESIGN-ICONS.md](third-party/MATERIAL-DESIGN-ICONS.md) |
| Inter (font) | 4.x | SIL OFL 1.1 | embedded font | [third-party/INTER-FONT.md](third-party/INTER-FONT.md) |

Shared license texts: [Apache License 2.0](third-party/LICENSE-APACHE-2.0.md) · [SIL Open Font License 1.1](third-party/LICENSE-OFL-1.1.md) · [MIT](third-party/LICENSE-MIT.md) · [BSD 3-Clause](third-party/LICENSE-BSD-3-CLAUSE.md)

## Own implementations (no third-party code)

| Part | Note |
| --- | --- |
| Core Audio, taskbar and shell interop | own declarations of the documented Windows COM interfaces in `MUTEit.Platform.Windows` |
| Notification sounds | synthesised by `build/make-assets.py`, MIT like MUTEit; played by MUTEit's own WASAPI output |

Build-time only (not shipped): xUnit (Apache-2.0), Microsoft.NET.Test.Sdk (MIT), Inno Setup (own licence, installer only), Pillow / cairosvg / numpy (asset script only).
