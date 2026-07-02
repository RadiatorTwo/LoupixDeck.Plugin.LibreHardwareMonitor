# LoupixDeck.Plugin.LibreHardwareMonitor

LoupixDeck-Plugin. Erstellt mit dem `create-loupix-plugin` Skill.

## Referenz-Verzeichnisse

Diese Ordner liegen lokal und enthalten alles, was zum Verständnis und zur Entwicklung dieses Plugins nötig ist:

- **Wiki (Plugin-SDK-Dokumentation):** `C:\!Code\LoupixDeck.PluginSdk.wiki`
  Erste Anlaufstelle für SDK-Konzepte, Lifecycle, Manifest, Commands, Settings, Folder-Provider.
- **SDK (Quellcode + lokales NuGet-Feed):** `C:\!Code\LoupixDeck.PluginSdk`
  Enthält die Basisklassen (`LoupixPlugin`), Interfaces (`IPluginCommand`, `IPluginHost`, `IDisplayCommand`, `IFolderProvider`, `IPluginSettingsPage`) und unter `nupkg\` das NuGet-Paket, gegen das hier gebaut wird (siehe `nuget.config`).
- **Host-Software (LoupixDeck):** `C:\!Code\LoupixDeck`
  Lädt das Plugin zur Laufzeit. Hier liegt der `PluginManager` und die Logik für Plugin-Discovery, Manifest-Parsing und Command-Ausführung.
- **Referenz-Plugin (vollständiges Beispiel):** `C:\!Code\LoupixDeck.Plugin.Audio`
  Funktionierendes Plugin mit Commands, Folder-Providern, Settings-Page und Plattform-spezifischen Services. Als Vorlage für komplexere Features verwenden.

## Plugin-Grundgerüst

- **Assembly-/Ordnername:** `LoupixDeck.Plugin.LibreHardwareMonitor` (Konvention: `LoupixDeck.Plugin.<Name>`)
- **Namespace:** `LoupixDeck.Plugin.LibreHardwareMonitor`
- **Plugin-Klasse:** `LibreHardwareMonitorPlugin` erbt von `LoupixPlugin`
- **Manifest:** `plugin.json` (id = `librehardwaremonitor`, sdkVersion = `1.3`, entryAssembly = `LoupixDeck.Plugin.LibreHardwareMonitor.dll`)
- **Target Framework:** `net9.0`
- **SDK-Paket:** `LoupixDeck.PluginSdk` 1.3.0 mit `<ExcludeAssets>runtime</ExcludeAssets>` — der Host stellt die SDK-DLL bereit, nie mit ausliefern.

## Build & Deploy

```bash
dotnet build -c Release
```

Output landet in `bin\Release\` (kein TFM-Suffix wegen `AppendTargetFrameworkToOutputPath=false`). Zum Testen den Inhalt von `bin\Release\` nach `<LoupixDeck>\plugins\librehardwaremonitor\` kopieren; `plugin.json` muss dort neben der DLL liegen. Das Plugin liest die Sensordaten über den HTTP-Webserver von LibreHardwareMonitor (Options → „Run web server", Standard-Port 8085) und bringt keine eigenen Runtime-Abhängigkeiten mit.

**Datenquelle:** Aktuelle LibreHardwareMonitor-Versionen veröffentlichen **kein** WMI mehr (`root\LibreHardwareMonitor` existiert nicht). Der unterstützte Weg ist der eingebaute Webserver, der `/data.json` liefert (Baum aus Hardware-→Typ-→Sensor-Knoten; Sensorknoten haben `SensorId`, `Text`, `Type` und ein bereits formatiertes `Value` inkl. Einheit).

## Pflicht-Member von `LoupixPlugin`

- `Metadata` — `PluginMetadata` mit `Id`, `Name`, `Version`, `SdkVersion`, optional `Author`/`Description`/`Icon`.
- `Initialize(IPluginHost host)` — einmaliger Setup-Hook; Host für Logging (`host.Logger`), Settings (`host.Settings`), Command-Ausführung und Button-Refresh nutzen.
- `GetCommands()` — `IEnumerable<IPluginCommand>` aller Commands.
- `Shutdown()` (optional überschreiben) — Ressourcen freigeben.

## Commands schreiben

Jeder Command implementiert `IPluginCommand`:

- `Descriptor` — `CommandName` (stabile öffentliche ID, Konvention: `LibreHardwareMonitor.<Feature>`), `DisplayName`, `Group` (= `"LibreHardwareMonitor"`).
- `SupportedTargets` — `ButtonTargets.All` oder einschränken.
- `Execute(CommandContext ctx)` — gibt `Task` zurück, läuft im Background-Thread, MUSS `try/catch` umschließen, darf nicht blockieren.

Für dynamisch beschriftete Buttons zusätzlich `IDisplayCommand` implementieren. Für Touchscreen-Ordner `IFolderProvider`. Für Settings-UI `IPluginSettingsPage`. Konkrete Patterns: siehe Referenz-Plugin `LoupixDeck.Plugin.Audio`.

## Wichtige Regeln

1. **Nicht** die SDK-DLL mit dem Plugin ausliefern (`<ExcludeAssets>runtime</ExcludeAssets>` ist gesetzt).
2. **Genau eine** konkrete `LoupixPlugin`-Subklasse pro Assembly — der Host findet sie per Reflection.
3. `CommandName` ist eine **stabile öffentliche API** — nach Release nicht mehr umbenennen.
4. `Metadata.Id` (lowercase), `plugin.json#id` und der Plugin-Ordnername unter `plugins\` müssen identisch sein.
5. `Execute` läuft **nicht** auf dem UI-Thread — keine Avalonia-Objekte direkt anfassen.
