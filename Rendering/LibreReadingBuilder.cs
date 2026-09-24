using LoupixDeck.Plugin.LibreHardwareMonitor.Rendering.Tiles;
using LoupixDeck.Plugin.LibreHardwareMonitor.Telemetry;

namespace LoupixDeck.Plugin.LibreHardwareMonitor.Rendering;

/// <summary>
/// Turns a persisted <c>LibreHardwareMonitor.Sensor</c> command parameter into the
/// <see cref="SensorRow"/> a tile draws. The parameter is the sensor reference
/// (<see cref="LibreSensorRef"/>): LibreHardwareMonitor's sensor identifier, so buttons saved before
/// the pixel tiles keep resolving. Values, units, history and alert state come from the
/// <see cref="TelemetrySampler"/>, which tracks every sensor under that same reference.
/// </summary>
internal static class LibreReadingBuilder
{
    private const string Fallback = "LibreHM";

    public static SensorRow Build(string? parameter, IReadOnlyList<LibreSensor> sensors)
    {
        if (!LibreSensorRef.TryParse(parameter, out string key))
            return Placeholder(Fallback);

        LibreSensor? sensor = sensors.FirstOrDefault(s => s.Key == key);
        if (sensor is null)
            return Placeholder(Fallback);

        // The menu's name for the sensor; a sensor the menu does not offer keeps its own name.
        if (TileLabels.For(sensors, key) is { } labels)
            return new SensorRow(labels.Header, labels.Short, key);

        string header = string.IsNullOrWhiteSpace(sensor.Name) ? sensor.Identifier : sensor.Name;
        return new SensorRow(header, ShortHeaderFrom(header), MetricKeys.ForSensor(sensor));
    }

    private static SensorRow Placeholder(string header) => new(header, header, null);

    /// <summary>Compact form of a header for a row of a multi-reading tile: drops a leading
    /// "CPU "/"GPU " so e.g. "CPU Core #1" reads "Core #1".</summary>
    private static string ShortHeaderFrom(string header)
    {
        if ((header.StartsWith("CPU ", StringComparison.Ordinal) || header.StartsWith("GPU ", StringComparison.Ordinal))
            && header.Length > 4)
            return header[4..];

        return header;
    }
}
