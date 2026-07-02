using LoupixDeck.PluginSdk;

namespace LoupixDeck.Plugin.LibreHardwareMonitor;

/// <summary>
/// Display command that renders a single LibreHardwareMonitor sensor reading onto a touch button.
/// The sensor is chosen via the plugin's dynamic menu, which stores the sensor's stable id as the
/// command parameter.
/// </summary>
internal sealed class LibreSensorCommand(LibreHardwareMonitorService service) : IDisplayCommand
{
    public CommandDescriptor Descriptor { get; } = new()
    {
        // Stable public API — never rename after release.
        CommandName = "LibreHardwareMonitor.Sensor",
        DisplayName = "LibreHardwareMonitor Sensor",
        Group = "LibreHardwareMonitor",
        ParameterTemplate = "({Sensor})",
        Parameters = [new CommandParameter("Sensor", typeof(string))],
        // Surfaced per sensor through the dynamic menu.
        HiddenFromMenu = true
    };

    public ButtonTargets SupportedTargets => ButtonTargets.TouchButton;

    public TimeSpan UpdateInterval => TimeSpan.FromSeconds(2);

    public string GetText(CommandContext ctx)
    {
        if (!service.IsAvailable)
            return "N/A";

        var parameters = ctx.Parameters;
        if (parameters is not { Length: >= 1 } || string.IsNullOrWhiteSpace(parameters[0]))
            return "?";

        if (!LibreSensorRef.TryParse(parameters[0], out string identifier))
            return "?";

        LibreSensor? sensor = service.Sensors.FirstOrDefault(s => s.Identifier == identifier);
        if (sensor is null)
            return "?";

        // The web server already formats Value with the right unit and locale.
        return string.IsNullOrWhiteSpace(sensor.ValueText) ? "?" : sensor.ValueText;
    }

    public Task Execute(CommandContext ctx) => Task.CompletedTask;
}
