namespace LoupixDeck.Plugin.LibreHardwareMonitor.Telemetry;

/// <summary>
/// Describes a single LibreHardwareMonitor sensor as a metric: which native value to track, how to
/// format it and which alert rule applies. Decided by the sensor type, the unit and the component
/// (<see cref="Components"/>). Used for every sensor, so any sensor a user puts on a tile gets a
/// history and a state.
/// </summary>
internal static class SensorMetrics
{
    /// <summary>
    /// The value to track, in the unit <see cref="Describe"/> formats: temperatures in °C, transfer
    /// rates in bytes per second, data sizes in MB; everything else is LibreHardwareMonitor's own
    /// value. NaN when the reading has no number.
    /// </summary>
    public static double NativeValue(LibreSensor sensor)
    {
        double value = sensor.Value;
        return sensor.SensorType switch
        {
            "Temperature" when sensor.Unit.EndsWith('F') => (value - 32) * 5 / 9,
            "Throughput" => MetricFormatter.ToBytesPerSecond(value, sensor.Unit) ?? value,
            "Data" or "SmallData" => Megabytes(value, sensor.Unit),
            _ => value
        };
    }

    public static MetricInfo Describe(LibreSensor sensor, double tjMax)
    {
        string component = Components.Of(sensor);

        return sensor.SensorType switch
        {
            "Temperature" => Temperature(sensor, component, tjMax),

            "Load" when component == Components.Memory && !IsVirtualMemory(sensor) =>
                new MetricInfo(MetricFormat.Percent, 0, 100, ThresholdKind.RamLoad),
            "Load" when component is Components.Cpu or Components.Gpu =>
                new MetricInfo(MetricFormat.Percent, 0, 100, Smooth: true),
            "Load" or "Control" or "Level" or "Humidity" =>
                new MetricInfo(MetricFormat.Percent, 0, 100),

            "Fan" when component == Components.Gpu =>
                new MetricInfo(MetricFormat.Rpm, 0, 3300, ThresholdKind.GpuFanStall, GrowToPeak: true),
            "Fan" =>
                new MetricInfo(MetricFormat.Rpm, 0, 3000, ThresholdKind.CpuFanStall, GrowToPeak: true),

            "Power" => new MetricInfo(MetricFormat.Watt, 0, 100, GrowToPeak: true),

            "Clock" when component == Components.Gpu =>
                new MetricInfo(MetricFormat.ClockMhz, 0, 3200, Smooth: true, GrowToPeak: true),
            "Clock" => new MetricInfo(MetricFormat.ClockMhz, 0, 6000, Smooth: true, GrowToPeak: true),

            // The CPU's factors are its per-core multipliers.
            "Factor" when component == Components.Cpu =>
                new MetricInfo(MetricFormat.Multiplier, 0, 60, Smooth: true, GrowToPeak: true),

            "Data" or "SmallData" when IsSize(sensor.Unit) => new MetricInfo(MetricFormat.Megabytes, 0, 0),
            "Throughput" when MetricFormatter.ToBytesPerSecond(1, sensor.Unit) is not null =>
                new MetricInfo(MetricFormat.BytesPerSecond, 0, 0),

            _ when sensor.Unit == "%" => new MetricInfo(MetricFormat.Percent, 0, 100),
            _ => new MetricInfo(MetricFormat.Generic, 0, 0, Unit: sensor.Unit)
        };
    }

    /// <summary>
    /// Readings LibreHardwareMonitor files as temperatures that are really fixed limits or
    /// properties of the sensor ("Warning Temperature", "Thermal Sensor Critical Limit",
    /// "Temperature Sensor Resolution"). They get no alert rule and are left out of page values.
    /// </summary>
    public static bool IsTemperatureLimit(LibreSensor sensor) =>
        sensor.SensorType == "Temperature"
        && (Contains(sensor.Name, "Warning") || Contains(sensor.Name, "Critical") || Contains(sensor.Name, "Limit")
            || Contains(sensor.Name, "Threshold") || Contains(sensor.Name, "Resolution")
            || Contains(sensor.Name, "Distance to TjMax"));

    public static bool IsVirtualMemory(LibreSensor sensor) =>
        sensor.HardwareId.Equals("/vram", StringComparison.OrdinalIgnoreCase) || Contains(sensor.Name, "Virtual");

    private static MetricInfo Temperature(LibreSensor sensor, string component, double tjMax)
    {
        if (IsTemperatureLimit(sensor))
            return new MetricInfo(MetricFormat.Temperature, 0, 0);

        return component switch
        {
            Components.Cpu => new MetricInfo(MetricFormat.Temperature, 30, tjMax, ThresholdKind.CpuTemperature),
            // Only the GPU core has the design's 80/88 limits; hot spot and memory run hotter by design.
            Components.Gpu => new MetricInfo(MetricFormat.Temperature, 30, 95,
                IsGpuCore(sensor) ? ThresholdKind.GpuTemperature : ThresholdKind.None),
            Components.Storage => new MetricInfo(MetricFormat.Temperature, 20, 80, ThresholdKind.StorageTemperature),
            _ => new MetricInfo(MetricFormat.Temperature, 20, 100)
        };
    }

    /// <summary>The GPU core temperature: LibreHardwareMonitor names it "GPU Core" and lists it first.</summary>
    private static bool IsGpuCore(LibreSensor sensor) =>
        sensor.Name.EndsWith("Core", StringComparison.OrdinalIgnoreCase)
        || sensor.Identifier.EndsWith("/temperature/0", StringComparison.Ordinal);

    private static bool IsSize(string unit) => unit is "GB" or "MB" or "KB" or "TB";

    private static double Megabytes(double value, string unit) => unit switch
    {
        "TB" => value * 1024 * 1024,
        "GB" => value * 1024,
        "KB" => value / 1024,
        _ => value
    };

    private static bool Contains(string text, string word) => text.Contains(word, StringComparison.OrdinalIgnoreCase);
}
