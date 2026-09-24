# LoupixDeck.Plugin.LibreHardwareMonitor

LibreHardwareMonitor integration plugin for [LoupixDeck](https://github.com/RadiatorTwo/LoupixDeck),
built against [LoupixDeck.PluginSdk](https://github.com/RadiatorTwo/LoupixDeck.PluginSdk).

Reads a running [LibreHardwareMonitor](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor)
instance through its built-in **HTTP web server** (current LibreHardwareMonitor no longer publishes
WMI). When the web server isn't reachable, the plugin shows "not reachable" and recovers
automatically once it's enabled.

## Setup in LibreHardwareMonitor

Enable the web server: **Options → "Run web server"** (default port **8085**). To change the port,
edit the plugin setting **"Web server URL"** accordingly.

If the web server's HTTP authentication is enabled, fill in the optional **Username** / **Password**
plugin settings — the plugin then sends HTTP Basic auth. Leave both empty when authentication is off.
Use **"Test Connection"** to verify the URL and credentials.

## Features

Both commands draw pixel tiles (5×7 bitmap font, no anti-aliasing) with a gauge bar and a
72-second history chart. Readings turn amber or red past their limits (CPU relative to the
**CPU TjMax** setting, GPU core, drives, RAM load, a stalled fan while its temperature is high).
The **Transparent background** setting lets the page wallpaper show through.

- `LibreHardwareMonitor.Sensor` — one sensor per command. Chain up to four on one button for a
  multi-row tile. Sensors are offered as a live menu sorted by component (CPU, GPU, Memory,
  Storage, Mainboard, Network, Other), device and quantity. Buttons saved with earlier versions
  keep their sensor.
- `LibreHardwareMonitor.Pages` — component pages CPU, GPU, RAM, NET, DISK and a CPU summary; a key
  press shows the next page. Chain several `Pages` commands to build your own cycle. NET follows
  the adapter that carried the most data, DISK sums the transfer rates of all drives.

The menu, the settings and the command texts are available in English, German and Spanish.
Requires LoupixDeck with Plugin SDK 1.26 or later.

## Build & deploy

```bash
dotnet build LoupixDeck.Plugin.LibreHardwareMonitor.csproj -c Release
```

Copy the `bin\Release` contents together with `plugin.json` into
`LoupixDeck/plugins/librehardwaremonitor/`. The plugin ships no runtime dependencies of its own.
`release.ps1` stages this into `dist\librehardwaremonitor\`.
