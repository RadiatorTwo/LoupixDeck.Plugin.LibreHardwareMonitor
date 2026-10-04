# LoupixDeck.Plugin.LibreHardwareMonitor

LibreHardwareMonitor integration plugin for [LoupixDeck](https://github.com/RadiatorTwo/LoupixDeck),
built against [LoupixDeck.PluginSdk](https://github.com/RadiatorTwo/LoupixDeck.PluginSdk).

Reads a running [LibreHardwareMonitor](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor)
instance through its built-in **HTTP web server** (current LibreHardwareMonitor no longer publishes
WMI). When the web server isn't reachable, the plugin shows "not reachable" and recovers
automatically once it's enabled. It also reports this as an unmet requirement: the LoupixDeck
Plugins page marks it "Needs attention" with the same reason "Test Connection" gives.

## Setup in LibreHardwareMonitor

Enable the web server: **Options → "Run web server"** (default port **8085**). To change the port,
edit the plugin setting **"Web server URL"** accordingly.

If the web server's HTTP authentication is enabled, fill in the optional **Username** / **Password**
plugin settings — the plugin then sends HTTP Basic auth. Leave both empty when authentication is off.
Use **"Test Connection"** to verify the URL and credentials. When the web server cannot be read it
says why (not reachable, no answer, login required or rejected, an invalid URL or one that does not
point to LibreHardwareMonitor, no sensors) and shows the last error.

## Features

Both commands draw pixel tiles (5×7 bitmap font, no anti-aliasing) with a gauge bar and a
72-second history chart. Readings turn amber or red past their limits (CPU relative to the
**CPU TjMax** setting, GPU core, drives, RAM load, a stalled fan while its temperature is high).
The **Transparent background** setting lets the page wallpaper show through.

- `LibreHardwareMonitor.Sensor` — one sensor per command. Chain up to four on one button for a
  multi-row tile. Sensors are offered as a live menu sorted by component (CPU, GPU, Memory,
  Storage, Mainboard, Network, Other), device and quantity. Buttons saved with earlier versions
  keep their sensor.
- `LibreHardwareMonitor.Pages` — component pages CPU, GPU, RAM, NET, DISK, PWR, VRAM, BAT and a
  CPU summary; a key press shows the next page. The menu's "All pages" entry cycles through every
  page, including pages added later. Chain several `Pages` commands to build your own cycle. Pages
  without data are skipped. NET follows the adapter that carried the most data, DISK
  sums the transfer rates of all drives. PWR shows CPU package power, GPU power and their sum; VRAM
  the primary GPU's memory in use; BAT the battery charge level (laptops).

Settings: transparent background, the CPU's TjMax (CPU temperature turns amber at
TjMax − 15 °C and red at TjMax − 5 °C), temperatures in °F instead of °C (display only), and
the alert limits: GPU 80/88 °C, drives 55/65 °C, RAM load 85/95 % (warning/critical) and a
stalled fan below 200 RPM by default.

The menu, the settings and the command texts are available in English, German and Spanish.
Requires LoupixDeck with Plugin SDK 1.28 or later.

## Troubleshooting

"Test Connection" in the plugin settings says why the web server cannot be read and shows the last
error. For a log, start LoupixDeck with the environment variable
`LOUPIXDECK_DEBUG_LIBREHARDWAREMONITOR=1`: error events then go to the host log (in release builds
`%USERPROFILE%\.config\LoupixDeck\loupixdeck-startup.log`). Without it the plugin writes nothing
there.

## Build & deploy

```bash
dotnet build LoupixDeck.Plugin.LibreHardwareMonitor.csproj -c Release
```

Copy the `bin\Release` contents together with `plugin.json` into
`LoupixDeck/plugins/librehardwaremonitor/`. The plugin ships no runtime dependencies of its own.
`release.ps1` stages this into `dist\librehardwaremonitor\`.
