using System.Globalization;
using LoupixDeck.PluginSdk;

namespace LoupixDeck.Plugin.LibreHardwareMonitor.Rendering;

/// <summary>
/// Turns a persisted <c>LibreHardwareMonitor.Sensor</c> command parameter (the sensor identifier)
/// and a live sensor snapshot into a <see cref="SensorReading"/>. Owns the sensor→display mapping
/// (value/unit splitting, gauge scaling, accent) so <see cref="SensorRenderer"/> stays a pure drawing
/// component.
///
/// <para>The model is <b>one reading per command</b>: a parameter is LibreHardwareMonitor's stable
/// sensor identifier (e.g. <c>/amdcpu/0/temperature/0</c>) and yields a single reading; the user
/// composes a multi-sensor button by chaining several commands (the renderer lays them out as rows).
/// The parameter format is unchanged from the former text command, so buttons saved before the rework
/// keep resolving.</para>
///
/// <para>LibreHardwareMonitor's web server only publishes an already-formatted, localized value
/// string (e.g. <c>"52.4 °C"</c>) — no raw number. The value string is split into a numeric part and
/// a unit for the large value/unit layout, and the numeric part is re-parsed (best effort) only to
/// scale the gauge; the displayed digits are LibreHardwareMonitor's own, unchanged.</para>
/// </summary>
public static class LibreReadingBuilder
{
    // The leading run of a formatted value that makes up the number (before the unit): digits, sign,
    // and either decimal separator. LibreHardwareMonitor emits "<number> <unit>".
    private static readonly char[] NumberChars = ['0', '1', '2', '3', '4', '5', '6', '7', '8', '9', '+', '-', '.', ','];

    /// <summary>Builds the single reading referenced by <paramref name="identifier"/>, or a
    /// placeholder reading when LibreHardwareMonitor is unreachable, the reference is empty, or the
    /// sensor is not currently present in the snapshot.</summary>
    public static SensorReading Build(string? identifier, IReadOnlyList<LibreSensor> sensors, bool isAvailable)
    {
        if (!isAvailable)
            return Placeholder("LibreHM", "N/A");

        if (!LibreSensorRef.TryParse(identifier, out string id))
            return Placeholder("LibreHM", "?");

        LibreSensor? sensor = null;
        foreach (LibreSensor candidate in sensors)
        {
            if (candidate.Identifier == id)
            {
                sensor = candidate;
                break;
            }
        }

        if (sensor is null)
            return Placeholder("LibreHM", "?");

        // LibreHardwareMonitor only publishes an already-formatted string; split it into a raw number
        // and its unit, then re-derive both to match Argus's compact look (F1, dropped ".0", GHz/G).
        (string rawNumber, string rawUnit) = SplitValue(sensor.ValueText);
        double? parsed = ParseNumber(rawNumber);

        (string value, string unit) = parsed is null
            ? (rawNumber, rawUnit)                 // Non-numeric value (e.g. a textual state) — show verbatim.
            : Format(parsed.Value, rawUnit);

        string header = Header(sensor);
        return new SensorReading(header, value, unit, Fraction(sensor, parsed), Accent(sensor.SensorType), ShortHeaderFrom(header));
    }

    // ── Value/unit splitting ───────────────────────────────────────────────────

    /// <summary>Splits a formatted value such as <c>"52.4 °C"</c> into its numeric part (<c>"52.4"</c>)
    /// and unit (<c>"°C"</c>): the leading run of number characters is the value, the rest is the unit.
    /// A value with no leading number is returned verbatim with no unit.</summary>
    private static (string value, string unit) SplitValue(string? valueText)
    {
        string text = (valueText ?? string.Empty).Trim();
        if (text.Length == 0)
            return ("?", string.Empty);

        int i = 0;
        while (i < text.Length && Array.IndexOf(NumberChars, text[i]) >= 0)
            i++;

        if (i == 0)
            return (text, string.Empty); // No leading number (e.g. a textual value) — show verbatim.

        string number = text[..i].Trim();
        string unit = text[i..].Trim();
        return (number, unit);
    }

    private static double? ParseNumber(string number)
    {
        // The value is localized: accept a comma as the decimal separator by normalizing to invariant.
        string normalized = number.Replace(',', '.');
        return double.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out double value)
            ? value
            : null;
    }

    // ── Formatting (mirrors Argus) ─────────────────────────────────────────────

    /// <summary>Formats a value + unit the same way the Argus plugin does, so both plugins render an
    /// identical look: memory MB/GB collapse to a compact "G", a four-digit MHz clock becomes GHz
    /// (e.g. 4200 MHz → 4.2 GHz), and the number is trimmed to one decimal with a trailing ".0"
    /// dropped so whole numbers read as integers.</summary>
    private static (string value, string unit) Format(double value, string rawUnit)
    {
        string unit = rawUnit;

        if (unit.Equals("MB", StringComparison.OrdinalIgnoreCase))
        {
            value /= 1024.0;
            unit = "G";
        }
        else if (unit.Equals("GB", StringComparison.OrdinalIgnoreCase))
        {
            unit = "G";
        }
        else if (unit.Equals("MHz", StringComparison.OrdinalIgnoreCase) && Math.Abs(value) >= 1000.0)
        {
            value /= 1000.0;
            unit = "GHz";
        }

        return (FormatNumber(value), unit);
    }

    /// <summary>One decimal place, but a trailing ".0" is dropped so whole numbers read as integers
    /// (36 °C) while fractional readings keep their decimal (6.6 %).</summary>
    private static string FormatNumber(double value)
    {
        string text = value.ToString("F1", CultureInfo.InvariantCulture);
        if (text.EndsWith(".0", StringComparison.Ordinal))
            text = text[..^2];
        return text;
    }

    // ── Gauge fill ──────────────────────────────────────────────────────────────

    // Nominal full-scale values for readings with no natural 0..100 range, matching Argus so the bars
    // fill comparably. Percentages ignore these (they are already 0..100).
    private const double TempMaxC = 100.0;
    private const double PowerMaxW = 100.0;
    private const double FanRpmMax = 3000.0;
    private const double FreqMaxDefaultMhz = 6000.0;

    /// <summary>Returns the 0..1 gauge fill for a reading, or null when it has no meaningful scale (so
    /// no bar is drawn). Percentages (Load/Level/Control or a "%" unit) use their value directly;
    /// temperature, power, fan and clock readings divide by a nominal per-type maximum (as Argus does)
    /// so most tiles carry a bar. Voltages, currents, data rates etc. have no natural full-scale and
    /// draw no bar.</summary>
    private static double? Fraction(LibreSensor sensor, double? parsed)
    {
        if (parsed is null)
            return null;

        double? max = MaxFor(sensor);
        if (max is null or <= 0)
            return null;

        return Math.Clamp(parsed.Value / max.Value, 0.0, 1.0);
    }

    private static double? MaxFor(LibreSensor sensor) => sensor.SensorType switch
    {
        "Temperature" => TempMaxC,
        "Load" or "Level" or "Control" => 100.0,
        "Power" => PowerMaxW,
        "Fan" => FanRpmMax,
        "Clock" => FreqMaxDefaultMhz,
        _ => null
    };

    // ── Accent (bar tint per sensor kind) ──────────────────────────────────────

    private static readonly PluginColor AccentTemp = new(0xC0, 0x76, 0x40);     // muted orange
    private static readonly PluginColor AccentUsage = new(0x57, 0x9E, 0x63);    // muted green
    private static readonly PluginColor AccentClock = new(0x53, 0x6D, 0x9E);    // muted steel blue
    private static readonly PluginColor AccentPower = new(0xA8, 0x5C, 0x5C);    // muted red
    private static readonly PluginColor AccentFan = new(0xB0, 0x92, 0x42);      // muted amber

    /// <summary>Muted accent tint for a sensor kind, used only for the gauge fill. Null → the theme's
    /// neutral default bar color.</summary>
    private static PluginColor? Accent(string sensorType) => sensorType switch
    {
        "Temperature" => AccentTemp,
        "Load" or "Level" or "Control" => AccentUsage,
        "Clock" => AccentClock,
        "Power" => AccentPower,
        "Fan" => AccentFan,
        _ => null
    };

    // ── Headers ────────────────────────────────────────────────────────────────

    private static SensorReading Placeholder(string header, string value) =>
        new(header, value, string.Empty);

    /// <summary>The tile / row title for a reading: its LibreHardwareMonitor name, falling back to the
    /// identifier.</summary>
    private static string Header(LibreSensor sensor) =>
        string.IsNullOrWhiteSpace(sensor.Name) ? sensor.Identifier : sensor.Name;

    /// <summary>Compact form of a header for use as a row label when several readings share the tile:
    /// drops a leading "CPU "/"GPU " subsystem word so e.g. "CPU Core #1" fits beside its value as
    /// "Core #1". Returns the header unchanged when there is nothing to drop.</summary>
    private static string ShortHeaderFrom(string header)
    {
        if ((header.StartsWith("CPU ", StringComparison.Ordinal) || header.StartsWith("GPU ", StringComparison.Ordinal))
            && header.Length > 4)
            return header[4..];

        return header;
    }
}
