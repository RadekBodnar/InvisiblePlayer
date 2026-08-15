# Third-Party Notices

InvisiblePlayer's own code is licensed under the MIT License — the full text is in
[LICENSE](LICENSE). This file lists third-party libraries, which come with their own
license terms.

---

## LibVLCSharp / LibVLCSharp.WPF (and LibVLC)

- **License:** GNU Lesser General Public License 2.1 or later (LGPL-2.1-or-later)
- **Source:** https://github.com/videolan/libvlcsharp
- **Note:** The library is referenced via dynamic linking (NuGet package / .dll reference),
  which satisfies the LGPL requirements. InvisiblePlayer's own code, which only consumes
  LibVLCSharp, remains under the MIT license.
  Full text of the LGPL 2.1 license: https://www.gnu.org/licenses/old-licenses/lgpl-2.1.html

## NAudio (NAudio, NAudio.Asio, NAudio.Midi, NAudio.Wasapi, NAudio.WinForms, NAudio.WinMM, NAudio.Core)

- **License:** MIT
- **Source:** https://github.com/naudio/NAudio

## Melanchall.DryWetMidi

- **License:** MIT
- **Source:** https://github.com/melanchall/drywetmidi

## System.Device.Gpio

- **License:** MIT
- **Source:** https://github.com/dotnet/iot

## MathNet.Numerics

- **License:** MIT
- **Source:** https://github.com/mathnet/mathnet-numerics

## ScottPlot / ScottPlot.WPF

- **License:** MIT
- **Source:** https://github.com/ScottPlot/ScottPlot
- **Note:** Transitively pulls in SkiaSharp (MIT, https://github.com/mono/SkiaSharp).

## xUnit.net, xunit.runner.visualstudio

- **License:** Apache-2.0 (xunit.runner.visualstudio: Apache-2.0)
- **Source:** https://github.com/xunit/xunit
- **Note:** Test-only dependency; not distributed with the application.

## coverlet.collector

- **License:** MIT
- **Source:** https://github.com/coverlet-coverage/coverlet
- **Note:** Test-only dependency; not distributed with the application.

---

*If additional third-party libraries are added to the project, please list them here
along with their license and source.*

