namespace LoupixDeck.Plugin.LibreHardwareMonitor;

/// <summary>
/// A sensor reference is LibreHardwareMonitor's sensor identifier (e.g. <c>/amdcpu/0/temperature/0</c>),
/// with <c>#n</c> appended only for the n-th repeat of an identifier LibreHardwareMonitor reports
/// twice (see <see cref="LibreSensor.Key"/>). References saved before are plain identifiers and keep
/// resolving to the first reading with that id, as they always did.
/// </summary>
internal static class LibreSensorRef
{
    public static string Format(LibreSensor sensor) => sensor.Key;

    public static bool TryParse(string? raw, out string key)
    {
        key = raw?.Trim() ?? string.Empty;
        return !string.IsNullOrEmpty(key);
    }
}
