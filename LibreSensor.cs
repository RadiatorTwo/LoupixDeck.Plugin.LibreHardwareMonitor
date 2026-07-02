namespace LoupixDeck.Plugin.LibreHardwareMonitor;

/// <summary>
/// One sensor reading from LibreHardwareMonitor's HTTP web server (<c>/data.json</c>).
/// </summary>
/// <param name="Identifier">Stable, unique sensor id (<c>SensorId</c>, e.g. <c>/amdcpu/0/temperature/0</c>).
/// Used verbatim as the sensor reference for buttons.</param>
/// <param name="Name">Human-readable sensor name (<c>Text</c>).</param>
/// <param name="SensorType">LHM sensor type string (<c>Type</c>, e.g. <c>Temperature</c>).</param>
/// <param name="HardwareName">Display name of the owning hardware device.</param>
/// <param name="ValueText">Pre-formatted current value incl. unit (e.g. <c>"52.4 °C"</c>), already
/// localized by LibreHardwareMonitor. Shown verbatim on the button.</param>
public sealed record LibreSensor(
    string Identifier,
    string Name,
    string SensorType,
    string HardwareName,
    string ValueText);
