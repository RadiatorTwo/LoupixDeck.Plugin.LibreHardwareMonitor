using System.Text.RegularExpressions;
using LoupixDeck.Plugin.LibreHardwareMonitor.Rendering.Pixel;
using LoupixDeck.Plugin.LibreHardwareMonitor.Rendering.Tiles;
using LoupixDeck.Plugin.LibreHardwareMonitor.Telemetry;

namespace LoupixDeck.Plugin.LibreHardwareMonitor.Rendering;

/// <summary>
/// The labels a LibreHardwareMonitor.Sensor tile shows, derived from the names the menu gives the sensors
/// (<see cref="SensorMenu.Name"/>), so the tile and the menu speak the same language. Each label
/// is the component ("CPU", "GPU", …) followed by the menu's entry name: CPU core 0 and the GPU
/// core temperature read "CPU Core" and "GPU Core" instead of both "Core". The quantity is left to
/// the unit printed beside the value.
///
/// <para>A tile header takes about eleven characters, a row label of a multi-reading tile about
/// seven, so the row label is abbreviated ("CPU C1", "GPU Hot"). Labels are unique among the
/// readings that share a unit; where the words alone would collide, a running number is added.</para>
/// </summary>
internal static partial class TileLabels
{
    public sealed record Labels(string Header, string Short);

    private sealed record Cache(IReadOnlyList<LibreSensor> Sensors, Dictionary<string, Labels> Labels);

    /// <summary>Width of a single-reading tile's header text (the header band minus its margins).</summary>
    private const int HeaderRoom = TileDrawing.W - 2;

    /// <summary>Characters a row label keeps beside a two-digit value at 2×; longer labels are cut
    /// when drawn, so <see cref="Shorten"/> makes the part that tells them apart fit first.</summary>
    private const int ShortBudget = 8;

    private static volatile Cache? _cache;

    private static readonly Dictionary<string, string> ComponentTags = new()
    {
        ["CPU"] = "CPU",
        ["GPU"] = "GPU",
        ["Memory"] = "RAM",
        ["Storage"] = "Disk",
        ["Network"] = "Net"
    };

    /// <summary>Words that only repeat the quantity. Dropped when something else is left.</summary>
    private static readonly Dictionary<string, string[]> QuantityWords = new()
    {
        ["Temperature"] = ["Temperature"],
        ["Power"] = ["Power"],
        ["Clock"] = ["Clock"]
    };

    /// <summary>Row-label abbreviations, applied as whole words, case-insensitively.</summary>
    private static readonly (string Word, string Short)[] Abbreviations =
    [
        ("Hot Spot", "Hot"), ("Memory Junction", "Mem"),
        ("Temperature", "Temp"),
        ("Clock", "Clk"),
        ("Memory", "Mem"),
        ("Multiplier", "Mult"),
        ("Power", "Pwr"),
        ("Total", "Tot"),
        ("System", "Sys"),
        ("Average", "Avg"), ("Effective", "Eff"),
        ("Maximum", "Max"), ("Minimum", "Min"),
        ("Controller", "Ctrl"), ("Available", "Free"),
        ("Activity", "Act"), ("Space", ""),
        ("Chipset", "Chip"), ("Composite", "Comp"),
        ("Virtual", "Virt"),
        ("read", "Rd"), ("write", "Wr"), ("down", "Dn")
    ];

    /// <summary>The labels of the sensor under <paramref name="key"/> (<see cref="LibreSensor.Key"/>),
    /// or null when the menu does not offer it. Computed once per sensor snapshot.</summary>
    public static Labels? For(IReadOnlyList<LibreSensor> sensors, string key)
    {
        Cache? cache = _cache;
        if (cache is null || !ReferenceEquals(cache.Sensors, sensors))
            _cache = cache = new Cache(sensors, Compute(sensors));

        return cache.Labels.GetValueOrDefault(key);
    }

    private static Dictionary<string, Labels> Compute(IReadOnlyList<LibreSensor> sensors)
    {
        List<SensorName> named = SensorMenu.Name(sensors);
        List<string> headers = [];
        List<string> shorts = [];
        foreach (SensorName name in named)
        {
            string tag = ComponentTags.GetValueOrDefault(name.Component, string.Empty);
            string entry = EntryWords(name);
            string header = Join(tag, entry);
            string compact = Join(tag, Abbreviate(entry));

            headers.Add(PixelFont.Measure(header) > HeaderRoom ? compact : header);
            shorts.Add(Shorten(Join(tag, Aggregate().Replace(Abbreviate(entry), "$1")), tag.Length > 0));
        }

        Number(headers, named);
        Number(shorts, named);

        Dictionary<string, Labels> labels = [];
        for (int i = 0; i < named.Count; i++)
            labels[MetricKeys.ForSensor(named[i].Sensor)] = new Labels(headers[i], shorts[i]);

        return labels;
    }

    /// <summary>The entry part of the label: the menu name, minus words the tile does not need.</summary>
    private static string EntryWords(SensorName name)
    {
        LibreSensor sensor = name.Sensor;
        switch (sensor.SensorType)
        {
            // "Read Rate" → "Read", "Download Speed" → "Down".
            case "Throughput" when Direction(sensor.Name) is { } direction:
                return direction;
            // "GPU Used" would not say what is used; "GPU Mem" is the memory temperature.
            case "Data" or "SmallData" when name.Component == Components.Gpu:
                return Join("VRAM", name.Name);
        }

        // An entry that replaced its one-entry submenu is named after the quantity ("GPU > Load").
        string entry = name.MenuName == name.Section ? name.Section : name.Name;
        entry = Whitespace().Replace(entry.Replace('(', ' ').Replace(")", ""), " ").Replace(" ,", ",").Trim();

        if (QuantityWords.TryGetValue(name.Section, out string[]? words))
        {
            string stripped = entry;
            foreach (string word in words)
                stripped = ReplaceWord(stripped, word, "");

            stripped = Whitespace().Replace(stripped, " ").Trim();
            // Keep the word where nothing but a number would be left ("Temp. 4").
            if (stripped.Any(char.IsLetter))
                entry = stripped;
        }

        // "RAM Used" twice would only be told apart by a number.
        if (name.Component == Components.Memory && SensorMetrics.IsVirtualMemory(sensor))
            entry = Join("Virtual", entry);

        return entry;
    }

    /// <summary>The direction a rate's name states, as the word the row label abbreviates.</summary>
    private static string? Direction(string name)
    {
        foreach ((string word, string direction) in DirectionWords)
        {
            if (name.Contains(word, StringComparison.OrdinalIgnoreCase))
                return direction;
        }

        return null;
    }

    private static readonly (string Word, string Direction)[] DirectionWords =
        [("Download", "Down"), ("Upload", "Up"), ("Read", "Read"), ("Write", "Write")];

    private static string Abbreviate(string entry)
    {
        // "Core #1" → "C1"; a "Core" in front of another word adds nothing ("Core CCD1", "Core Clock").
        string text = CoreNumber().Replace(entry, "C$1");
        text = CoreBeforeWord().Replace(text, "");

        foreach ((string word, string abbreviation) in Abbreviations)
            text = ReplaceWord(text, word, abbreviation);

        return Whitespace().Replace(text, " ").Trim();
    }

    /// <summary>
    /// Cuts a row label to <see cref="ShortBudget"/> characters word by word, so every word keeps a
    /// few letters instead of the tail being lost: "Vorne Unten" → "Vor Unte", "Mem-Modul 3" →
    /// "Mem-Mo 3". Never touches the component tag or trailing digits.
    /// </summary>
    private static string Shorten(string label, bool hasTag)
    {
        List<string> words = label.Split(' ').ToList();
        int first = hasTag ? 1 : 0;

        while (string.Join(' ', words).Length > ShortBudget)
        {
            // The longest word other than the last with more than three letters, else the last one.
            int pick = -1;
            for (int i = first; i < words.Count - 1; i++)
            {
                if (Letters(words[i]) > 3 && (pick < 0 || words[i].Length > words[pick].Length))
                    pick = i;
            }

            if (pick < 0 && words.Count - 1 >= first && Letters(words[^1]) > 3)
                pick = words.Count - 1;

            if (pick < 0)
                break;

            words[pick] = DropLetter(words[pick]);
        }

        return string.Join(' ', words);
    }

    private static int Letters(string word) => word.Count(char.IsLetter);

    /// <summary>Removes the last letter before any trailing digits ("NotConnected1" → "NotConnecte1").</summary>
    private static string DropLetter(string word)
    {
        int end = word.Length;
        while (end > 0 && !char.IsLetter(word[end - 1]))
            end--;

        return (word[..(end - 1)] + word[end..]).TrimEnd('-', '.', ',');
    }

    /// <summary>Appends a running number to labels that collide among readings of the same unit.</summary>
    private static void Number(List<string> labels, List<SensorName> named)
    {
        foreach (IGrouping<(string, string), int> clash in Enumerable.Range(0, labels.Count)
                     .GroupBy(i => (labels[i].ToUpperInvariant(), SensorMenu.UnitTag(named[i].Sensor)))
                     .Where(g => g.Count() > 1))
        {
            int number = 1;
            foreach (int i in clash)
                labels[i] = $"{labels[i]} {number++}";
        }
    }

    private static string Join(string tag, string entry)
    {
        if (tag.Length == 0 || entry.Length == 0)
            return tag.Length == 0 ? entry : tag;

        // Names sometimes start with the component already.
        return entry.StartsWith(tag + " ", StringComparison.OrdinalIgnoreCase) ? entry : $"{tag} {entry}";
    }

    private static string ReplaceWord(string text, string word, string replacement) =>
        Regex.Replace(text, $@"(?<!\w){Regex.Escape(word)}(?!\w)", replacement,
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>"Clk max" → "max": in a row label the unit already says clock or multiplier.</summary>
    [GeneratedRegex(@"^(?:Clk|Mult)\s+(max|min|avg)$")]
    private static partial Regex Aggregate();

    [GeneratedRegex(@"\b[Cc]ore\s+#?(\d+)\b")]
    private static partial Regex CoreNumber();

    [GeneratedRegex(@"\b[Cc]ore\s+(?=\S)")]
    private static partial Regex CoreBeforeWord();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
