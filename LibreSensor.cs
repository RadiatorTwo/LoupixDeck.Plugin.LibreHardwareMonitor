namespace LoupixDeck.Plugin.LibreHardwareMonitor;

/// <summary>
/// One sensor reading from LibreHardwareMonitor's HTTP web server (<c>/data.json</c>).
/// </summary>
/// <param name="Identifier">Sensor id as LibreHardwareMonitor reports it (<c>SensorId</c>, e.g.
/// <c>/amdcpu/0/temperature/0</c>). Not always unique: some devices report two readings under one id.</param>
/// <param name="Key">The reference buttons store: the identifier, with <c>#n</c> appended to the n-th
/// repeat of an identifier in the snapshot. The first occurrence keeps the bare identifier, so
/// buttons saved before keys existed resolve to the same reading as before.</param>
/// <param name="Name">Human-readable sensor name (<c>Text</c>).</param>
/// <param name="SensorType">LHM sensor type string (<c>Type</c>, e.g. <c>Temperature</c>).</param>
/// <param name="HardwareId">Identifier of the owning hardware device (e.g. <c>/amdcpu/0</c>).</param>
/// <param name="HardwareName">Display name of the owning hardware device.</param>
/// <param name="ValueText">Pre-formatted current value incl. unit (e.g. <c>"52,4 °C"</c>), localized by
/// LibreHardwareMonitor.</param>
/// <param name="Value">The number parsed from <paramref name="ValueText"/>, NaN when it holds none.</param>
/// <param name="Unit">The unit parsed from <paramref name="ValueText"/> (e.g. <c>°C</c>, <c>MB/s</c>).</param>
public sealed record LibreSensor(
    string Identifier,
    string Key,
    string Name,
    string SensorType,
    string HardwareId,
    string HardwareName,
    string ValueText,
    double Value,
    string Unit);
