using LoupixDeck.Plugin.LibreHardwareMonitor.Rendering.Tiles;
using LoupixDeck.Plugin.LibreHardwareMonitor.Telemetry;
using LoupixDeck.PluginSdk;

namespace LoupixDeck.Plugin.LibreHardwareMonitor;

/// <summary>
/// Entry point of the LibreHardwareMonitor plugin. Reads a running LibreHardwareMonitor instance
/// through its built-in HTTP web server (Options → "Run web server", default port 8085), samples it
/// once a second into histories and alert states, and exposes two pixel-tile display commands
/// through a live menu: <c>LibreHardwareMonitor.Sensor</c> (one sensor per command; chain several
/// for a multi-row tile) and <c>LibreHardwareMonitor.Pages</c> (component pages, a key press shows
/// the next one) — the same tiles as the Argus Monitor plugin.
/// </summary>
public sealed class LibreHardwareMonitorPlugin : LoupixPlugin, IMenuContributor, IPluginSettingsPage
{
    private const string KeyUrl = "url";
    private const string KeyUsername = "username";
    private const string KeyPassword = "password";
    private const string DefaultUrl = "http://localhost:8085";

    /// <summary>Settings key: when true, buttons are drawn without an opaque background so the page
    /// wallpaper shows through. Read by the display command at render time.</summary>
    public const string TransparentBackgroundKey = "background.transparent";

    /// <summary>Settings key: the CPU's maximum junction temperature in °C. CPU warn/critical
    /// limits are TjMax − 15 / TjMax − 5.</summary>
    public const string CpuTjMaxKey = "thresholds.cpuTjMax";

    private const long DefaultTjMax = 100;

    private readonly LibreHardwareMonitorService _service = new();
    private TelemetrySampler? _telemetry;
    private List<IPluginCommand> _commands = [];
    private IPluginHost? _host;

    public override PluginMetadata Metadata { get; } = new()
    {
        Id = "librehardwaremonitor",
        Name = "LibreHardwareMonitor",
        Version = new Version(1, 0, 0),
        SdkVersion = new Version(1, 26, 0),
        Author = "RadiatorTwo",
        Description = "Display LibreHardwareMonitor sensor readings on touch buttons; chain several to compose a multi-sensor tile."
    };

    public override void Initialize(IPluginHost host)
    {
        _host = host;
        _telemetry = new TelemetrySampler(_service, ReadTjMax);
        _commands = [new LibreSensorCommand(_telemetry), new LibrePagesCommand(_telemetry)];
        ApplySettings();
        _service.Start();
        _telemetry.Start();
    }

    public override void Shutdown()
    {
        _telemetry?.Stop();
        _service.Stop();
    }

    private double ReadTjMax()
    {
        long tjMax = _host?.Settings.Get(CpuTjMaxKey, DefaultTjMax) ?? DefaultTjMax;
        return Math.Clamp(tjMax, 60, 125);
    }

    public override IEnumerable<IPluginCommand> GetCommands() => _commands;

    public override IReadOnlyList<CommandGroupDescriptor> GetCommandGroups() =>
    [
        new CommandGroupDescriptor
        {
            Group = "LibreHardwareMonitor",
            Description = "Hardware sensor readouts",
            Icon = "\U000F0379",
            Section = CommandGroupSection.Plugins
        }
    ];

    // ───────── IMenuContributor — dynamic sensor tree (touch buttons, text) ─────────

    public Task<IReadOnlyList<MenuNode>> GetMenuNodes(ButtonTargets target)
    {
        // Sensor readings are touch-button display content only.
        if (target != ButtonTargets.TouchButton)
            return Task.FromResult<IReadOnlyList<MenuNode>>([]);

        var groupChildren = new List<MenuNode>();
        IReadOnlyList<LibreSensor> sensors = _service.Sensors;

        if (!_service.IsAvailable || sensors.Count == 0)
        {
            groupChildren.Add(new MenuNode
            {
                Name = "Not reachable — enable 'Run web server' in LibreHardwareMonitor"
            });
        }
        else
        {
            groupChildren.Add(new MenuNode { Name = "Pages", Children = PageNodes() });

            // Group by hardware device (LHM provides real hardware names), then by sensor type.
            foreach (var hardwareGroup in sensors
                         .GroupBy(s => s.HardwareName)
                         .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase))
            {
                var typeChildren = new List<MenuNode>();
                foreach (var typeGroup in hardwareGroup
                             .GroupBy(s => s.SensorType)
                             .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase))
                {
                    var readings = new List<MenuNode>();
                    foreach (LibreSensor sensor in typeGroup.OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase))
                    {
                        string label = string.IsNullOrWhiteSpace(sensor.Name) ? sensor.Identifier : sensor.Name;
                        readings.Add(new MenuNode
                        {
                            Name = label,
                            CommandName = LibreSensorCommand.CommandName,
                            Parameters = new Dictionary<string, string>
                            {
                                { "Sensor", LibreSensorRef.Format(sensor) }
                            }
                        });
                    }

                    typeChildren.Add(new MenuNode { Name = typeGroup.Key, Children = readings });
                }

                string hardwareName = string.IsNullOrWhiteSpace(hardwareGroup.Key) ? "(unknown)" : hardwareGroup.Key;
                groupChildren.Add(new MenuNode { Name = hardwareName, Children = typeChildren });
            }
        }

        IReadOnlyList<MenuNode> result = [new MenuNode { Name = "LibreHardwareMonitor", Children = groupChildren }];
        return Task.FromResult(result);
    }

    /// <summary>The paging tile (every page, press for the next) and one fixed tile per page.</summary>
    private static List<MenuNode> PageNodes() =>
    [
        PagesNode("All pages (press to cycle)", ComponentPages.DefaultSelection),
        PagesNode("CPU page", ComponentPages.Cpu.Id),
        PagesNode("GPU page", ComponentPages.Gpu.Id),
        PagesNode("RAM page", ComponentPages.Ram.Id),
        PagesNode("Network page", ComponentPages.Net.Id),
        PagesNode("Disk page", ComponentPages.Disk.Id),
        PagesNode("CPU summary", ComponentPages.Summary.Id)
    ];

    private static MenuNode PagesNode(string name, string pages) => new()
    {
        Name = name,
        CommandName = LibrePagesCommand.CommandName,
        Parameters = new Dictionary<string, string> { { "Pages", pages } }
    };

    // ───────── IPluginSettingsPage — web-server URL ─────────

    public IReadOnlyList<PluginSettingDescriptor> SettingsSchema { get; } =
    [
        new PluginSettingDescriptor
        {
            Key = KeyUrl, Label = "Web server URL", Kind = PluginSettingKind.Text,
            DefaultValue = DefaultUrl,
            Description = "Base URL of the LibreHardwareMonitor web server " +
                          "(Options → 'Run web server'; default http://localhost:8085)."
        },
        new PluginSettingDescriptor
        {
            Key = KeyUsername, Label = "Username (optional)", Kind = PluginSettingKind.Text,
            DefaultValue = string.Empty,
            Description = "Only needed if the web server's HTTP authentication is enabled. Leave empty otherwise."
        },
        new PluginSettingDescriptor
        {
            Key = KeyPassword, Label = "Password (optional)", Kind = PluginSettingKind.Password,
            DefaultValue = string.Empty,
            Description = "Password for HTTP authentication. Leave empty if authentication is disabled."
        },
        new PluginSettingDescriptor
        {
            Key = TransparentBackgroundKey,
            Label = "Transparent background",
            Kind = PluginSettingKind.Toggle,
            DefaultValue = false,
            Description = "Draw buttons without an opaque background so the page wallpaper shows through. " +
                          "Text gets a 1-pixel shadow for legibility."
        },
        new PluginSettingDescriptor
        {
            Key = CpuTjMaxKey,
            Label = "CPU TjMax (°C)",
            Kind = PluginSettingKind.Number,
            DefaultValue = DefaultTjMax,
            Description = "Maximum junction temperature of your CPU, from the vendor's spec sheet " +
                          "(typically 95 for AMD Ryzen, 100–105 for Intel). CPU temperature turns amber " +
                          "at TjMax − 15 and red at TjMax − 5."
        }
    ];

    public IReadOnlyList<PluginSettingAction> SettingsActions => _settingsActions ??=
    [
        new PluginSettingAction
        {
            Label = "Test Connection",
            Invoke = async () =>
            {
                ApplySettings();
                try
                {
                    int count = await _service.ProbeAsync();
                    return $"Connected — {count} sensor(s)";
                }
                catch (Exception ex)
                {
                    return $"Failed: {ex.Message}";
                }
            }
        }
    ];

    private IReadOnlyList<PluginSettingAction>? _settingsActions;

    public void OnSettingsSaved()
    {
        ApplySettings();
        // Tiles redraw several times a second and pick up the new settings on their own; this
        // only covers a host that drives them through the slower poll path.
        _host?.RequestButtonRefresh(LibreSensorCommand.CommandName);
        _host?.RequestButtonRefresh(LibrePagesCommand.CommandName);
    }

    private void ApplySettings()
    {
        if (_host == null)
            return;

        string url = _host.Settings.Get(KeyUrl, DefaultUrl) ?? DefaultUrl;
        string username = _host.Settings.Get(KeyUsername, string.Empty) ?? string.Empty;
        string password = _host.Settings.Get(KeyPassword, string.Empty) ?? string.Empty;
        _service.Configure(url, username, password);
    }
}
