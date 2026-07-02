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

`LibreHardwareMonitor.Sensor` — a display command that renders a chosen sensor reading onto a
touch button (updated every 2 s). Sensors are offered as a live tree in the touch-button command
menu, grouped by hardware device and sensor type. The reading is shown exactly as LibreHardwareMonitor
formats it (e.g. `45.0 °C`, `1200 RPM`).

## Build & deploy

```bash
dotnet build LoupixDeck.Plugin.LibreHardwareMonitor.csproj -c Release
```

Copy the `bin\Release` contents together with `plugin.json` into
`LoupixDeck/plugins/librehardwaremonitor/`. The plugin ships no runtime dependencies of its own.
`release.ps1` stages this into `dist\librehardwaremonitor\`.
