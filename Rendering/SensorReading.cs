using LoupixDeck.PluginSdk;

namespace LoupixDeck.Plugin.LibreHardwareMonitor.Rendering;

/// <summary>
/// One sensor reading to draw as a row (or, when it is the only one, as a full tile). Decoupled
/// from LibreHardwareMonitor so <see cref="SensorRenderer"/> stays a pure, reusable drawing
/// component. All numbers arrive pre-formatted as strings — value/unit splitting happens in the
/// caller (see <see cref="LibreReadingBuilder"/>).
/// </summary>
/// <param name="Header">Full label / tile title (e.g. "CPU Core #1", "GPU Core"). Used when the
/// reading fills the tile on its own.</param>
/// <param name="Value">Main value string (e.g. "52.4", "12").</param>
/// <param name="Unit">Unit drawn small next to the value (e.g. "°C", "%", "RPM"). May be empty.</param>
/// <param name="Fraction">Gauge fill 0..1 for the value, or null when the reading has no meaningful
/// scale (no bar is drawn).</param>
/// <param name="Accent">Accent color for the gauge fill, or null for the theme's neutral default.</param>
/// <param name="ShortHeader">Compact label used when the reading shares the tile as one of several
/// rows (e.g. "Core #1" for a "CPU Core #1" header), so it fits beside the value. Null → use
/// <paramref name="Header"/>.</param>
public sealed record SensorReading(
    string Header,
    string Value,
    string Unit,
    double? Fraction = null,
    PluginColor? Accent = null,
    string? ShortHeader = null);
