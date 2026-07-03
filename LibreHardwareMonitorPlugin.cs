using LoupixDeck.PluginSdk;

namespace LoupixDeck.Plugin.LibreHardwareMonitor;

/// <summary>
/// Entry point of the LibreHardwareMonitor plugin. Reads a running LibreHardwareMonitor instance
/// through its built-in HTTP web server (Options → "Run web server", default port 8085) and exposes
/// one text display command plus a live sensor menu (touch buttons) — mirroring the Argus Monitor
/// plugin, but sourced from LibreHardwareMonitor's web-server data.
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

    private readonly LibreHardwareMonitorService _service = new();
    private List<IPluginCommand> _commands = [];
    private IPluginHost? _host;

    public override PluginMetadata Metadata { get; } = new()
    {
        Id = "librehardwaremonitor",
        Name = "LibreHardwareMonitor",
        Version = new Version(1, 0, 0),
        SdkVersion = new Version(1, 15, 0),
        Author = "RadiatorTwo",
        Description = "Display LibreHardwareMonitor sensor readings on touch buttons; chain several to compose a multi-sensor tile."
    };

    public override void Initialize(IPluginHost host)
    {
        _host = host;
        _commands = [new LibreSensorCommand(_service)];
        ApplySettings();
        _service.Start();
    }

    public override void Shutdown() => _service.Stop();

    public override IEnumerable<IPluginCommand> GetCommands() => _commands;

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
                            CommandName = "LibreHardwareMonitor.Sensor",
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
                          "Text is outlined for legibility."
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
        // Repaint bound touch buttons immediately so a transparency toggle is visible at once
        // (otherwise it would only apply on the command's next 2s poll).
        _host?.RequestButtonRefresh("LibreHardwareMonitor.Sensor");
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
