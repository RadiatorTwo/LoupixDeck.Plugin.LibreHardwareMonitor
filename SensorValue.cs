using System.Globalization;

namespace LoupixDeck.Plugin.LibreHardwareMonitor;

/// <summary>
/// Splits LibreHardwareMonitor's formatted value ("52,4 °C", "1,9 MB/s", "NaN %") into a number and
/// its unit. LibreHardwareMonitor formats in its own culture without group separators, so the only
/// separator in the number is the decimal one — a comma or a point.
/// </summary>
internal static class SensorValue
{
    // The leading run of a formatted value that makes up the number (before the unit).
    private static readonly char[] NumberChars = ['0', '1', '2', '3', '4', '5', '6', '7', '8', '9', '+', '-', '.', ','];

    /// <summary>The number (NaN when there is none, e.g. "NaN %" or a text value) and the unit.</summary>
    public static (double Value, string Unit) Parse(string? valueText)
    {
        string text = (valueText ?? string.Empty).Trim();

        int i = 0;
        while (i < text.Length && Array.IndexOf(NumberChars, text[i]) >= 0)
            i++;

        if (i == 0)
            return (double.NaN, UnitAfterWord(text));

        string number = text[..i].Replace(',', '.');
        double value = double.TryParse(number, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed)
            ? parsed
            : double.NaN;
        return (value, text[i..].Trim());
    }

    /// <summary>The unit of a value without a number ("NaN %" → "%").</summary>
    private static string UnitAfterWord(string text)
    {
        int space = text.IndexOf(' ');
        return space < 0 ? string.Empty : text[(space + 1)..].Trim();
    }
}
