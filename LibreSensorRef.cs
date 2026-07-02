namespace LoupixDeck.Plugin.LibreHardwareMonitor;

/// <summary>
/// A sensor reference is simply LibreHardwareMonitor's stable WMI identifier string
/// (e.g. <c>/amdcpu/0/temperature/0</c>). Unlike Argus (which encodes <c>Type:Index</c>),
/// the identifier is already globally unique and stable, so no encoding is needed. This
/// thin helper keeps the call shape identical to the shared command / side-strip code.
/// </summary>
internal static class LibreSensorRef
{
    public static string Format(LibreSensor sensor) => sensor.Identifier;

    public static bool TryParse(string? raw, out string identifier)
    {
        identifier = raw?.Trim() ?? string.Empty;
        return !string.IsNullOrEmpty(identifier);
    }
}
