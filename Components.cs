using System.Text.RegularExpressions;

namespace LoupixDeck.Plugin.LibreHardwareMonitor;

/// <summary>
/// Sorts LibreHardwareMonitor hardware into the components the menu and the pages use. Decided by
/// the identifier's first segment ("/amdcpu/0", "/gpu-nvidia/0", "/nvme/1", "/lpc/nct6797d/0" …),
/// never by the hardware's display name, which is a product name or a localized adapter name.
/// </summary>
internal static partial class Components
{
    public const string Cpu = "CPU";
    public const string Gpu = "GPU";
    public const string Memory = "Memory";
    public const string Storage = "Storage";
    public const string Mainboard = "Mainboard";
    public const string Network = "Network";
    public const string Other = "Other";

    public static readonly string[] Order = [Cpu, Gpu, Memory, Storage, Mainboard, Network, Other];

    public static string Of(LibreSensor sensor) => Of(sensor.HardwareId.Length > 0 ? sensor.HardwareId : sensor.Identifier);

    public static string Of(string identifier)
    {
        string kind = FirstSegment(identifier);
        return kind switch
        {
            _ when kind.EndsWith("cpu", StringComparison.Ordinal) => Cpu,
            "gpu-nvidia" or "gpu-amd" or "gpu-intel" => Gpu,
            "ram" or "vram" or "memory" => Memory,
            "nvme" or "hdd" or "ssd" or "ata" or "scsi" or "storage" => Storage,
            "lpc" or "motherboard" or "mainboard" or "ec" => Mainboard,
            "nic" or "network" => Network,
            _ => Other
        };
    }

    /// <summary>
    /// Windows lists every filter bound to an adapter as an adapter of its own
    /// ("Ethernet-QoS Packet Scheduler-0000"); these repeat the real adapter's numbers.
    /// </summary>
    public static bool IsFilterAdapter(LibreSensor sensor) =>
        Of(sensor) == Network && FilterSuffix().IsMatch(sensor.HardwareName);

    /// <summary>
    /// The GPU the pages show: a discrete card (NVIDIA, AMD) before an Intel one, else the first GPU.
    /// </summary>
    public static string? PrimaryGpu(IReadOnlyList<LibreSensor> sensors)
    {
        string? first = null;
        foreach (LibreSensor sensor in sensors)
        {
            if (Of(sensor) != Gpu)
                continue;

            first ??= sensor.HardwareId;
            if (!FirstSegment(sensor.HardwareId).Equals("gpu-intel", StringComparison.Ordinal))
                return sensor.HardwareId;
        }

        return first;
    }

    /// <summary>
    /// The trailing index of an identifier ("/nvme/1/throughput/54" → 54), used to pair a fan's
    /// speed with its control and as the menu's last-resort name suffix.
    /// </summary>
    public static int Index(LibreSensor sensor)
    {
        string id = sensor.Identifier;
        int slash = id.LastIndexOf('/');
        return slash >= 0 && int.TryParse(id.AsSpan(slash + 1), out int index) ? index : 0;
    }

    private static string FirstSegment(string identifier)
    {
        string trimmed = identifier.TrimStart('/');
        int slash = trimmed.IndexOf('/');
        return (slash < 0 ? trimmed : trimmed[..slash]).ToLowerInvariant();
    }

    [GeneratedRegex(@"-\d{4}$")]
    private static partial Regex FilterSuffix();
}
