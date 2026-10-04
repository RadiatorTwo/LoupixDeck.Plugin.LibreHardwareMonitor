namespace LoupixDeck.Plugin.LibreHardwareMonitor.Telemetry;

/// <summary>
/// The derived metrics the component pages show (CPU temperature, GPU clock, RAM free …). Each is
/// picked out of the LibreHardwareMonitor snapshot by component (the identifier prefix, see
/// <see cref="Components"/>) and sensor type. Where a type holds several readings, the pick goes by
/// LibreHardwareMonitor's sensor names ("CPU Total", "Download Speed"): unlike Argus labels they
/// are English on every system, and position is only the fallback.
/// </summary>
internal static class PageMetrics
{
    public const string CpuTemp = "cpu.temp";
    public const string CpuClock = "cpu.clock";
    public const string CpuFan = "cpu.fan";
    public const string CpuLoad = "cpu.load";
    public const string CpuPower = "cpu.power";
    public const string GpuTemp = "gpu.temp";
    public const string GpuClock = "gpu.clock";
    public const string GpuFan = "gpu.fan";
    public const string GpuLoad = "gpu.load";
    public const string RamLoad = "ram.load";
    public const string RamUsed = "ram.used";
    public const string RamFree = "ram.free";
    public const string NetDown = "net.down";
    public const string NetUp = "net.up";
    public const string DiskTemp = "disk.temp";
    public const string DiskRead = "disk.read";
    public const string DiskWrite = "disk.write";
    public const string GpuPower = "gpu.power";
    public const string PowerTotal = "pwr.total";
    public const string VramLoad = "vram.load";
    public const string VramUsed = "vram.used";
    public const string VramFree = "vram.free";
    public const string BatteryLevel = "bat.level";

    /// <summary>One derived metric: how to read it from a snapshot and how to describe it.</summary>
    public sealed record Definition(
        string Id,
        Func<IReadOnlyList<LibreSensor>, double?> Read,
        Func<double, MetricInfo> Describe);

    public static IReadOnlyList<Definition> All { get; } =
    [
        // Tctl/Tdie on AMD, the package on Intel; the hottest real reading either way.
        new(CpuTemp,
            s => Max(Of(s, Components.Cpu, "Temperature").Where(x => !SensorMetrics.IsTemperatureLimit(x))),
            tj => new MetricInfo(MetricFormat.Temperature, 30, tj, ThresholdKind.CpuTemperature)),
        // The fastest core ("Core #n"); effective clocks and the bus speed are not core clocks.
        new(CpuClock,
            s => Max(Of(s, Components.Cpu, "Clock").Where(IsCoreClock))
                 ?? Named(Of(s, Components.Cpu, "Clock"), "Average"),
            _ => new MetricInfo(MetricFormat.ClockMhz, 800, 5800, Smooth: true, GrowToPeak: true)),
        new(CpuFan,
            s => CpuFanSpeed(s),
            _ => new MetricInfo(MetricFormat.Rpm, 0, 2500, ThresholdKind.CpuFanStall, GrowToPeak: true)),
        new(CpuLoad,
            s => Named(Of(s, Components.Cpu, "Load"), "Total") ?? First(Of(s, Components.Cpu, "Load")),
            _ => new MetricInfo(MetricFormat.Percent, 0, 100, Smooth: true)),
        new(CpuPower,
            s => CpuPackagePower(s),
            _ => new MetricInfo(MetricFormat.Watt, 0, 100, GrowToPeak: true)),

        new(GpuTemp,
            s => GpuCore(s, "Temperature"),
            _ => new MetricInfo(MetricFormat.Temperature, 30, 95, ThresholdKind.GpuTemperature)),
        new(GpuClock,
            s => GpuCore(s, "Clock"),
            _ => new MetricInfo(MetricFormat.ClockMhz, 200, 3200, Smooth: true, GrowToPeak: true)),
        new(GpuFan,
            s => First(OfGpu(s, "Fan")),
            _ => new MetricInfo(MetricFormat.Rpm, 0, 3300, ThresholdKind.GpuFanStall, GrowToPeak: true)),
        new(GpuLoad,
            s => GpuCore(s, "Load"),
            _ => new MetricInfo(MetricFormat.Percent, 0, 100, Smooth: true)),

        // The physical memory ("/ram"), not the virtual one ("/vram").
        new(RamLoad,
            s => First(PhysicalMemory(s, "Load")),
            _ => new MetricInfo(MetricFormat.Percent, 0, 100, ThresholdKind.RamLoad)),
        new(RamUsed,
            s => Named(PhysicalMemory(s, "Data"), "Used"),
            _ => new MetricInfo(MetricFormat.Megabytes, 0, 0)),
        new(RamFree,
            s => Named(PhysicalMemory(s, "Data"), "Available"),
            _ => new MetricInfo(MetricFormat.Megabytes, 0, 0)),

        // LibreHardwareMonitor lists upload before download, so position alone would swap them.
        new(NetDown,
            s => Directed(MainAdapter(s), "Download", "Upload", 1),
            _ => new MetricInfo(MetricFormat.BytesPerSecond, 0, 0)),
        new(NetUp,
            s => Directed(MainAdapter(s), "Upload", "Download", 0),
            _ => new MetricInfo(MetricFormat.BytesPerSecond, 0, 0)),

        // All drives: the hottest one, and the rates summed.
        new(DiskTemp,
            s => Max(Of(s, Components.Storage, "Temperature").Where(x => !SensorMetrics.IsTemperatureLimit(x))),
            _ => new MetricInfo(MetricFormat.Temperature, 20, 80, ThresholdKind.StorageTemperature)),
        new(DiskRead,
            s => DriveRates(s, "Read", "Write", 0),
            _ => new MetricInfo(MetricFormat.BytesPerSecond, 0, 0)),
        new(DiskWrite,
            s => DriveRates(s, "Write", "Read", 1),
            _ => new MetricInfo(MetricFormat.BytesPerSecond, 0, 0)),

        new(GpuPower,
            s => GpuBoardPower(s),
            _ => new MetricInfo(MetricFormat.Watt, 0, 300, GrowToPeak: true)),
        // CPU package plus GPU power; whichever LibreHardwareMonitor reports when one is missing.
        new(PowerTotal,
            s => CpuPackagePower(s) is { } cpu ? cpu + (GpuBoardPower(s) ?? 0) : GpuBoardPower(s),
            _ => new MetricInfo(MetricFormat.Watt, 0, 400, GrowToPeak: true)),

        // The primary GPU's memory: "GPU Memory Used/Free/Total" (SmallData, MB) and the "GPU Memory"
        // load in %. Cards without them report the Direct3D dedicated memory instead.
        new(VramLoad,
            s => VramUsedMb(s) is { } used && VramTotalMb(s) is > 0 and var total
                ? used * 100 / total
                : Exact(OfGpu(s, "Load"), "GPU Memory"),
            _ => new MetricInfo(MetricFormat.Percent, 0, 100)),
        new(VramUsed,
            s => VramUsedMb(s),
            _ => new MetricInfo(MetricFormat.Megabytes, 0, 0)),
        new(VramFree,
            s => Exact(OfGpu(s, "SmallData"), "GPU Memory Free")
                 ?? (VramUsedMb(s) is { } used && VramTotalMb(s) is { } total ? Math.Max(0, total - used) : null),
            _ => new MetricInfo(MetricFormat.Megabytes, 0, 0)),

        // The charge level of a battery ("/battery/…"). Absent on desktops, so the battery page is
        // skipped there.
        new(BatteryLevel,
            s => Named(Battery(s, "Level"), "Charge"),
            _ => new MetricInfo(MetricFormat.Percent, 0, 100))
    ];

    private static double? CpuPackagePower(IReadOnlyList<LibreSensor> sensors) =>
        Named(Of(sensors, Components.Cpu, "Power"), "Package") ?? First(Of(sensors, Components.Cpu, "Power"));

    /// <summary>The power of the whole card: "GPU Package" (NVIDIA and AMD), else the first GPU power.</summary>
    private static double? GpuBoardPower(IReadOnlyList<LibreSensor> sensors)
    {
        List<LibreSensor> power = OfGpu(sensors, "Power");
        return Named(power, "Package") ?? First(power);
    }

    private static double? VramUsedMb(IReadOnlyList<LibreSensor> sensors)
    {
        List<LibreSensor> sizes = OfGpu(sensors, "SmallData");
        return Exact(sizes, "GPU Memory Used") ?? Exact(sizes, "D3D Dedicated Memory Used");
    }

    private static double? VramTotalMb(IReadOnlyList<LibreSensor> sensors)
    {
        List<LibreSensor> sizes = OfGpu(sensors, "SmallData");
        return Exact(sizes, "GPU Memory Total") ?? Exact(sizes, "D3D Dedicated Memory Total");
    }

    private static List<LibreSensor> Battery(IReadOnlyList<LibreSensor> sensors, string type) =>
        sensors.Where(s => s.SensorType == type
                           && s.HardwareId.StartsWith("/battery", StringComparison.OrdinalIgnoreCase))
            .ToList();

    /// <summary>The reading named exactly <paramref name="name"/>: "GPU Memory" must not match
    /// "GPU Memory Controller".</summary>
    private static double? Exact(List<LibreSensor> sensors, string name) =>
        Value(sensors.FirstOrDefault(s => s.Name.Equals(name, StringComparison.OrdinalIgnoreCase)));

    private static List<LibreSensor> Of(IReadOnlyList<LibreSensor> sensors, string component, string type) =>
        sensors.Where(s => s.SensorType == type && Components.Of(s) == component).ToList();

    private static List<LibreSensor> OfGpu(IReadOnlyList<LibreSensor> sensors, string type)
    {
        string? gpu = Components.PrimaryGpu(sensors);
        return gpu is null ? [] : sensors.Where(s => s.HardwareId == gpu && s.SensorType == type).ToList();
    }

    /// <summary>The "GPU Core" reading of the primary GPU, else its first one of the type.</summary>
    private static double? GpuCore(IReadOnlyList<LibreSensor> sensors, string type)
    {
        List<LibreSensor> gpu = OfGpu(sensors, type);
        return Named(gpu, "Core") ?? First(gpu);
    }

    private static List<LibreSensor> PhysicalMemory(IReadOnlyList<LibreSensor> sensors, string type) =>
        Of(sensors, Components.Memory, type).Where(s => !SensorMetrics.IsVirtualMemory(s)
                                                        && !s.HardwareId.StartsWith("/memory/", StringComparison.OrdinalIgnoreCase))
            .ToList();

    private static bool IsCoreClock(LibreSensor sensor) =>
        sensor.Name.StartsWith("Core", StringComparison.OrdinalIgnoreCase)
        && !sensor.Name.StartsWith("Cores", StringComparison.OrdinalIgnoreCase)
        && !sensor.Name.Contains("Effective", StringComparison.OrdinalIgnoreCase);

    /// <summary>A mainboard fan named for the CPU, else the pump of a water cooler, else the first
    /// mainboard fan.</summary>
    private static double? CpuFanSpeed(IReadOnlyList<LibreSensor> sensors)
    {
        List<LibreSensor> fans = Of(sensors, Components.Mainboard, "Fan");
        return Named(fans, "CPU") ?? Named(fans, "Pump") ?? First(fans);
    }

    /// <summary>
    /// The rate sensors of the adapter that carried the most data: Windows lists virtual, Bluetooth
    /// and filter adapters too, and their order says nothing about which one is in use.
    /// </summary>
    private static List<LibreSensor> MainAdapter(IReadOnlyList<LibreSensor> sensors)
    {
        List<LibreSensor> network = sensors
            .Where(s => Components.Of(s) == Components.Network && !Components.IsFilterAdapter(s))
            .ToList();

        string? adapter = network
            .GroupBy(s => s.HardwareId)
            .Where(g => g.Any(s => s.SensorType == "Throughput"))
            .OrderByDescending(g => g.Where(s => s.SensorType == "Data").Sum(s => Value(s) ?? 0))
            .Select(g => g.Key)
            .FirstOrDefault();

        return network.Where(s => s.HardwareId == adapter && s.SensorType == "Throughput").ToList();
    }

    /// <summary>
    /// The first rate among <paramref name="rates"/> whose name says <paramref name="direction"/>;
    /// the one at <paramref name="fallbackOrdinal"/> when no rate names either direction.
    /// </summary>
    private static double? Directed(List<LibreSensor> rates, string direction, string opposite, int fallbackOrdinal)
    {
        LibreSensor? named = rates.FirstOrDefault(s => Names(s, direction));
        if (named is not null)
            return Value(named);

        return rates.Any(s => Names(s, opposite)) ? null : Value(rates.ElementAtOrDefault(fallbackOrdinal));
    }

    /// <summary>The rate of every drive in one direction, summed.</summary>
    private static double? DriveRates(IReadOnlyList<LibreSensor> sensors, string direction, string opposite,
        int fallbackOrdinal)
    {
        double? total = null;
        foreach (IGrouping<string, LibreSensor> drive in Of(sensors, Components.Storage, "Throughput")
                     .GroupBy(s => s.HardwareId))
        {
            if (Directed(drive.ToList(), direction, opposite, fallbackOrdinal) is { } rate)
                total = (total ?? 0) + rate;
        }

        return total;
    }

    private static bool Names(LibreSensor sensor, string word) =>
        sensor.Name.Contains(word, StringComparison.OrdinalIgnoreCase);

    private static double? Named(List<LibreSensor> sensors, string word) =>
        Value(sensors.FirstOrDefault(s => Names(s, word)));

    private static double? First(List<LibreSensor> sensors) => Value(sensors.FirstOrDefault());

    private static double? Max(IEnumerable<LibreSensor> sensors)
    {
        double? max = null;
        foreach (LibreSensor sensor in sensors)
        {
            if (Value(sensor) is { } value && (max is null || value > max))
                max = value;
        }

        return max;
    }

    /// <summary>The sensor's native value, or null when it has none (a sensor that reports "NaN").</summary>
    private static double? Value(LibreSensor? sensor)
    {
        if (sensor is null)
            return null;

        double value = SensorMetrics.NativeValue(sensor);
        return double.IsNaN(value) ? null : value;
    }
}
