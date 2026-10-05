# RDP icon monitor

A Windows .NET 10 tray app that replaces the icons of `msrdc.exe` connection windows by exact, case-insensitive window title. It watches window creation/title events, reconciles periodically, and recovers icons reset by the client. It does not reconnect sessions or change taskbar grouping.

## Build and run

Requirements: Windows, a .NET 10 SDK to build, a .NET 10 Desktop Runtime to run the framework-dependent build, and the installed Segoe UI Variable font when text is requested.

```powershell
dotnet restore .\RdpIconMonitor.slnx
dotnet test .\tests\RdpIconMonitor.Tests\RdpIconMonitor.Tests.csproj
dotnet publish .\src\RdpIconMonitor\RdpIconMonitor.csproj -c Release -r win-x64 --self-contained false -o .\publish
.\publish\RdpIconMonitor.exe
```

### GitHub Actions build

`.github\workflows\build.yml` builds on pushes, pull requests, and manual dispatch. Download the **RdpIconMonitor-win-x64** artifact from the workflow run.

The artifact contains a self-contained single `RdpIconMonitor.exe`, your current `config.example.json`, and `published.marker`. Extract all files together into a stable folder before running the executable. A separate .NET installation is not required for this build; the native rendering libraries are bundled and extract at runtime. The installed Segoe UI Variable font is still required for text rendering.

The default configuration is `%LOCALAPPDATA%\RdpIconMonitor\instances.json`. First launch creates an empty configuration, leaving all connection windows untouched. Use **Open configuration** in the tray menu, or choose a file explicitly:

```powershell
.\publish\RdpIconMonitor.exe --config "E:\status-bar-icon\config.example.json"
```

The example has editable colors and labels for the four connection names from the smoke test. Choosing that example applies its styles to matching windows.

Only one monitor instance runs per Windows desktop session. Stop it with **Exit and restore icons** in the tray, or:

```powershell
.\publish\RdpIconMonitor.exe --stop
```

The stop command signals graceful shutdown; it does not forcibly kill the process. If a live window cannot be restored, the app remains in the tray with an error and retries.

## Rules

```json
{
  "instances": [
    {
      "name": "remote-foo",
      "hue": 300,
      "smallText": "R",
      "largeText": "F"
    },
    {
      "name": "remote-bar",
      "hue": 150,
      "smallText": "R",
      "largeText": "B"
    },
    {
      "name": "local-baz",
      "hue": 250,
      "smallText": "L",
      "largeText": "Z"
    }
  ]
}
```

| Field | Meaning |
|---|---|
| `name` | Required exact full title; comparison ignores case. Every matching `msrdc.exe` window receives the rule. |
| `hue` | Optional finite OKLCH hue in degrees, normalized modulo 360. Missing/null keeps original colors. |
| `smallText` | Optional left-hand text run. |
| `largeText` | Optional right-hand text run. |

Missing, null, empty, or whitespace-only text is absent. One populated field replaces the cloud by itself; both absent keep the cloud. Text must be single-line and supported by Segoe UI Variable. Unknown fields, invalid types, and duplicate names are rejected.

Edits reload automatically, including save-by-rename. A valid reload is rendered before activation. Invalid JSON, invalid rules, or unsupported glyphs leave the previous rules active and report a rejected reload. Removing a rule from valid JSON restores previously matched windows. Deleting/breaking the entire file is a rejected reload, not a request to remove all rules; use `"instances": []` instead.

## Icon rendering

- Uses all eight supplied native SVG templates: 16, 20, 24, 32, 40, 48, 64, and 256 pixels. Templates are embedded; rebuild to pick up SVG changes.
- A hue override rotates all colored rectangle gradient stops equally in OKLCH, anchored to the original `#0078D4` stop. Lightness is preserved; out-of-sRGB-gamut colors reduce chroma instead of clamping channels independently.
- Text uses the installed **Segoe UI Variable** font at genuine **Bold (`wght=700`)**. The font file is not bundled.
- Small and large text share a baseline, appear left-to-right, and use a **0.72:1** size ratio. There is **no inserted gap**; ordinary glyph side bearings and spaces supplied in the fields still apply.
- The combined ink bounds are centered within the middle rectangle, with **8% of the rectangle side length as padding on every side**. Text shrinks uniformly to fit without wrapping or truncating.
- Text retains the cloud's light-gray-to-white gradient and the two original shadow components. The filter region is resized to avoid clipping the new glyph geometry.
- DPI-dependent window icon dimensions are rendered separately. Intermediate dimensions use the next larger SVG template.

Tiny icons and long labels can fit geometrically but become unreadable; short labels work best.

## Previews and exported icons

Render without monitoring or modifying windows:

```powershell
dotnet .\publish\RdpIconMonitor.dll --render-only .\config.example.json .\artifacts\preview
```

The output contains `instance-N.ico` with all eight sizes, individual PNGs and transformed SVGs, `manifest.json` mapping filenames to rules, and a native-size `contact-sheet.png`. Generated outputs at the chosen path are overwritten on repeated export.

The app uses individual owned HICONs for the small/large window slots; an eight-frame ICO is an export, not a promise that assigning one handle exposes every frame to Explorer.

## Tray and login

The tray provides configuration access/reload, pause/resume, diagnostics, login startup, and graceful exit.

**Pause** restores original icons; **Resume** reconciles current windows. Errors appear in the tray and in `%LOCALAPPDATA%\RdpIconMonitor\monitor.log`; repeated identical log messages are rate-limited.

**Start at login** is opt-in. Enable it from the published executable in a stable folder. The app writes only its own `RdpIconMonitor` value under the current user's Run key, quoting both executable and configuration paths. Disabling it removes only that value. An existing entry pointing to another executable is not removed or overwritten. Build, first launch, and render-only mode do not register startup.

## Limitations and cleanup

`WM_SETICON` also affects title-bar/Alt+Tab icons where the client uses the window icons; this is not a taskbar-only customization. Unmatched windows and unrelated processes remain untouched.

Run at the same privilege level as the connection windows if Windows denies cross-process messages. The app does not auto-elevate or inject code. A hung/closed target is reported without stalling the tray interface.

Normal pause, rule removal, retitle away from a matching rule, and graceful exit restore managed icon slots without overwriting newer external replacements. Borrowed original handles are never destroyed. Generated handles remain alive while referenced by managed windows, including after ambiguous message timeouts.

Forced termination, a crash, or system shutdown can bypass cleanup. Exit from the tray or use `--stop` rather than killing the process. If the tool was forcibly terminated and a session icon is wrong, restarting the connection window/client can reset it.

The original PowerShell extraction and smoke-test scripts remain available as diagnostics.
