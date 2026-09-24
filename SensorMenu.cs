using System.Text.RegularExpressions;
using LoupixDeck.PluginSdk;

namespace LoupixDeck.Plugin.LibreHardwareMonitor;

/// <summary>
/// Builds the <c>LibreHardwareMonitor.Sensor</c> part of the editor menu: sensors are sorted by
/// component (CPU, GPU, Memory, Storage, Mainboard, Network, Other), then by device where a component
/// has several (drives, adapters, DIMMs), then by quantity (Temperature, Clock, Load, …), and every
/// entry gets a name that is unique within its submenu.
///
/// <para>Only the presentation is new. Each entry still stores the sensor reference
/// (<see cref="LibreSensorRef"/>), so saved buttons keep loading. Filter drivers Windows lists as
/// adapters of their own are left out; buttons that use one still resolve.</para>
/// </summary>
internal static partial class SensorMenu
{
    /// <summary>A quantity inside a component. <paramref name="Interleave"/> lists the entries by
    /// sensor index first, so a fan's speed and its control sit next to each other.</summary>
    private sealed record Section(string Name, int Rank, bool Interleave = false);

    private static readonly string[] SectionOrder =
    [
        "Temperature", "Clock", "Load", "Memory", "Power", "Fans", "Voltage", "Current", "Multiplier",
        "Transfer rate", "Data", "Level"
    ];

    /// <summary>Words that repeat the component and are dropped from the sensor names
    /// ("CPU Total" under CPU → "Total", "Memory Used" under Memory → "Used").</summary>
    private static readonly Dictionary<string, string[]> ComponentWords = new()
    {
        [Components.Cpu] = ["CPU"],
        [Components.Gpu] = ["GPU"],
        [Components.Memory] = ["Memory"]
    };

    /// <summary>Trailing words that repeat the quantity ("Composite Temperature", "Upload Speed").</summary>
    private static readonly Dictionary<string, string[]> SectionSuffixes = new()
    {
        ["Temperature"] = ["Temperature"],
        ["Transfer rate"] = ["Speed", "Rate"]
    };

    private sealed record Entry(LibreSensor Sensor, int TypeRank, string BaseName);

    public static List<MenuNode> Build(IReadOnlyList<LibreSensor> sensors)
    {
        List<SensorName> named = Name(sensors);

        List<MenuNode> components = [];
        foreach (string component in Components.Order)
        {
            List<SensorName> ofComponent = named.Where(n => n.Component == component).ToList();
            List<IGrouping<string?, SensorName>> devices = ofComponent.GroupBy(n => n.Hardware).ToList();

            List<MenuNode> children = [];
            foreach (IGrouping<string?, SensorName> device in devices)
            {
                List<MenuNode> sections = SectionNodes(device.ToList());
                if (device.Key is null)
                    children.AddRange(sections);
                else
                    children.Add(new MenuNode { Name = device.Key, Children = sections });
            }

            if (children.Count > 0)
                components.Add(new MenuNode { Name = component, Children = children });
        }

        return components;
    }

    private static List<MenuNode> SectionNodes(List<SensorName> names)
    {
        List<IGrouping<string, SensorName>> sections = names.GroupBy(n => n.Section).ToList();

        List<MenuNode> children = [];
        foreach (IGrouping<string, SensorName> section in sections)
        {
            List<MenuNode> nodes = section.Select(n => new MenuNode
            {
                Name = n.MenuName,
                CommandName = LibreSensorCommand.CommandName,
                Parameters = new Dictionary<string, string> { { "Sensor", LibreSensorRef.Format(n.Sensor) } }
            }).ToList();

            if (sections.Count == 1 || nodes.Count == 1)
                children.AddRange(nodes);  // no extra level for a lone quantity or a lone entry
            else
                children.Add(new MenuNode { Name = section.Key, Children = nodes });
        }

        return children;
    }

    /// <summary>
    /// Names every sensor the menu offers, in menu order. Tile labels are derived from these names
    /// so the menu and the tile call a sensor the same thing.
    /// </summary>
    public static List<SensorName> Name(IReadOnlyList<LibreSensor> sensors)
    {
        List<SensorName> named = [];
        foreach (string component in Components.Order)
        {
            List<LibreSensor> ofComponent = sensors
                .Where(s => Components.Of(s) == component && !Components.IsFilterAdapter(s))
                .ToList();

            // A device level only where the component has several devices (drives, adapters).
            List<IGrouping<string, LibreSensor>> devices = ofComponent.GroupBy(s => s.HardwareId).ToList();
            Dictionary<string, string?> deviceNames = DeviceNames(devices);

            foreach (IGrouping<string, LibreSensor> device in devices)
                named.AddRange(DeviceEntries(component, deviceNames[device.Key], device.ToList()));
        }

        return named;
    }

    /// <summary>Display names of the devices, unique within the component; null when there is
    /// only one device.</summary>
    private static Dictionary<string, string?> DeviceNames(List<IGrouping<string, LibreSensor>> devices)
    {
        Dictionary<string, string?> names = [];
        if (devices.Count == 1)
        {
            names[devices[0].Key] = null;
            return names;
        }

        Dictionary<string, int> used = new(StringComparer.OrdinalIgnoreCase);
        foreach (IGrouping<string, LibreSensor> device in devices)
        {
            string name = device.First().HardwareName.Trim();
            if (name.Length == 0)
                name = device.Key;

            int count = used.GetValueOrDefault(name) + 1;
            used[name] = count;
            names[device.Key] = count == 1 ? name : $"{name} {count}";
        }

        return names;
    }

    private static List<SensorName> DeviceEntries(string component, string? hardware, List<LibreSensor> sensors)
    {
        Dictionary<Section, List<Entry>> bySection = [];
        foreach (LibreSensor sensor in sensors)
        {
            Section section = SectionFor(component, sensor.SensorType);
            if (!bySection.TryGetValue(section, out List<Entry>? entries))
                bySection[section] = entries = [];

            entries.Add(new Entry(sensor, TypeRank(sensor.SensorType), BaseName(sensor, component, section)));
        }

        List<KeyValuePair<Section, List<Entry>>> sections = bySection
            .OrderBy(pair => pair.Key.Rank)
            .ThenBy(pair => pair.Key.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        List<SensorName> named = [];
        foreach ((Section section, List<Entry> entries) in sections)
        {
            List<SensorName> names = SectionNames(component, hardware, section, entries);

            // A submenu with one entry is noise: the entry takes the submenu's place and name
            // (unless it is the device's only quantity, which gets no submenu either).
            if (sections.Count > 1 && names.Count == 1)
                names[0] = names[0] with { MenuName = section.Name };

            named.AddRange(names);
        }

        return named;
    }

    /// <summary>The quantity a sensor type belongs to; memory readings of the RAM and the GPU form
    /// one "Memory" quantity, the CPU's factors are its multipliers.</summary>
    private static Section SectionFor(string component, string type)
    {
        string name = (component, type) switch
        {
            (Components.Memory, "Load" or "Data" or "SmallData") => "Memory",
            (Components.Gpu, "Data" or "SmallData") => "Memory",
            (Components.Cpu, "Factor") => "Multiplier",
            (_, "Fan" or "Control") => "Fans",
            (_, "Throughput") => "Transfer rate",
            (_, "Data" or "SmallData") => "Data",
            _ => type
        };

        int rank = Array.IndexOf(SectionOrder, name);
        return new Section(name, rank >= 0 ? rank : SectionOrder.Length, Interleave: name == "Fans");
    }

    /// <summary>Order of types inside a quantity: speed before control, percent before size.</summary>
    private static int TypeRank(string type) => type switch
    {
        "Fan" or "Load" => 0,
        "Control" or "Data" => 1,
        "SmallData" => 2,
        _ => 0
    };

    private static List<SensorName> SectionNames(string component, string? hardware, Section section,
        List<Entry> entries)
    {
        List<Entry> ordered = section.Interleave
            ? entries.OrderBy(e => Components.Index(e.Sensor)).ThenBy(e => e.TypeRank).ToList()
            : entries.OrderBy(e => e.TypeRank).ToList();

        // Name the unit only where it tells entries apart: "Pump Fan (RPM)" beside
        // "Pump Fan (%)", but plain "Core #1" among clock-only readings.
        bool showUnit = ordered.Select(e => UnitTag(e.Sensor)).Where(u => u.Length > 0).Distinct().Count() > 1;

        List<string> tags = ordered.Select(e => showUnit ? UnitTag(e.Sensor) : "").ToList();
        List<string> baseNames = ordered.Select(e => e.BaseName).ToList();

        // Readings named identically ("D3D Copy" six times) get a running number per type.
        foreach (IGrouping<(string, string), int> clash in Enumerable.Range(0, ordered.Count)
                     .GroupBy(i => (Compose(baseNames[i], tags[i]).ToUpperInvariant(), ordered[i].Sensor.SensorType))
                     .Where(g => g.Count() > 1))
        {
            int number = 1;
            foreach (int i in clash)
                baseNames[i] = $"{ordered[i].BaseName} {number++}";
        }

        List<string> names = Enumerable.Range(0, ordered.Count).Select(i => Compose(baseNames[i], tags[i])).ToList();

        // Last resort for anything still ambiguous across types: the sensor's index.
        foreach (IGrouping<string, int> clash in Enumerable.Range(0, ordered.Count)
                     .GroupBy(i => names[i], StringComparer.OrdinalIgnoreCase)
                     .Where(g => g.Count() > 1))
        {
            foreach (int i in clash)
            {
                int index = Components.Index(ordered[i].Sensor);
                names[i] = $"{names[i]} #{index}";
                baseNames[i] = $"{baseNames[i]} #{index}";
            }
        }

        return Enumerable.Range(0, ordered.Count)
            .Select(i => new SensorName(ordered[i].Sensor, component, hardware, section.Name, baseNames[i], names[i]))
            .ToList();
    }

    private static string Compose(string name, string unitTag) =>
        unitTag.Length == 0 ? name : $"{name} ({unitTag})";

    /// <summary>
    /// The entry name before units and numbering: the sensor's name with the component word and
    /// the quantity removed, or the quantity's name where nothing else is left.
    /// </summary>
    private static string BaseName(LibreSensor sensor, string component, Section section)
    {
        string label = sensor.Name.Trim();

        if (ComponentWords.TryGetValue(component, out string[]? words))
        {
            foreach (string word in words)
                label = Regex.Replace(label, $@"\b{Regex.Escape(word)}\b", "", RegexOptions.IgnoreCase);
        }

        label = Whitespace().Replace(label, " ").Trim();

        // "Load Total" under Load → "Total"; "Memory Free" under Memory → "Free".
        // Kept where only a number would be left ("Temperature #1").
        if (label.StartsWith(section.Name + " ", StringComparison.OrdinalIgnoreCase)
            && label[(section.Name.Length + 1)..].Any(char.IsLetter))
            label = label[(section.Name.Length + 1)..];

        // "Composite Temperature" → "Composite"; kept where only a number would be left.
        if (SectionSuffixes.TryGetValue(section.Name, out string[]? suffixes))
        {
            foreach (string suffix in suffixes)
            {
                string stripped = Regex.Replace(label, $@"\s+{Regex.Escape(suffix)}$", "", RegexOptions.IgnoreCase);
                if (stripped.Any(char.IsLetter))
                    label = stripped;
            }
        }

        if (label.Length == 0)
            return sensor.SensorType == "Load" ? "Load" : section.Name;

        return label;
    }

    /// <summary>
    /// The unit that tells readings of one quantity apart. Taken from the sensor type where
    /// LibreHardwareMonitor scales the unit with the value (KB/s → MB/s), so a name does not change
    /// while the menu is open.
    /// </summary>
    public static string UnitTag(LibreSensor sensor) => sensor.SensorType switch
    {
        "Fan" => "RPM",
        "Load" or "Control" or "Level" or "Humidity" => "%",
        "Data" => "GB",
        "SmallData" => "MB",
        "Throughput" => "B/s",
        _ => sensor.Unit.Trim()
    };

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}

/// <summary>
/// How the menu names a sensor. <paramref name="Hardware"/> is the device level (null where the
/// component has one device); <paramref name="Name"/> is the entry name without the unit that sets
/// it apart from its neighbours ("Pump Fan"); <paramref name="MenuName"/> is what the menu shows
/// ("Pump Fan (RPM)", or the quantity's name when the entry replaces a one-entry submenu).
/// </summary>
internal sealed record SensorName(LibreSensor Sensor, string Component, string? Hardware, string Section,
    string Name, string MenuName);
