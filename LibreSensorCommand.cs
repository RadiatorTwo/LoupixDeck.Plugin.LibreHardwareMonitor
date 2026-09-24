using LoupixDeck.Plugin.LibreHardwareMonitor.Rendering;
using LoupixDeck.Plugin.LibreHardwareMonitor.Rendering.Pixel;
using LoupixDeck.Plugin.LibreHardwareMonitor.Rendering.Tiles;
using LoupixDeck.Plugin.LibreHardwareMonitor.Telemetry;
using LoupixDeck.PluginSdk;

namespace LoupixDeck.Plugin.LibreHardwareMonitor;

/// <summary>
/// Display command that renders LibreHardwareMonitor readings onto a touch button as pixel tiles
/// (5×7 bitmap font, no anti-aliasing). One command carries one sensor; a button's command sequence
/// composes the tile dynamically — the first (rendering) command reads
/// <see cref="CommandContext.SequenceCommands"/> and draws one row per sibling command (up to four).
/// The command name and the "Sensor" parameter (the sensor identifier) are unchanged, so buttons
/// saved before the rework keep working.
/// </summary>
internal sealed class LibreSensorCommand(TelemetrySampler telemetry) : IAnimatedDisplayCommand, IDisplayImageCommand
{
    public const string CommandName = "LibreHardwareMonitor.Sensor";

    public CommandDescriptor Descriptor { get; } = new()
    {
        // Stable public API — never rename after release.
        CommandName = CommandName,
        DisplayName = "LibreHardwareMonitor Sensor",
        Group = "LibreHardwareMonitor",
        Icon = "\U000F0379",
        Description = "Live LibreHardwareMonitor sensor readout on a touch button",
        ParameterTemplate = "({Sensor})",
        Parameters = [new CommandParameter("Sensor", typeof(string))],
        // Surfaced per sensor through the dynamic menu.
        HiddenFromMenu = true
    };

    public ButtonTargets SupportedTargets => ButtonTargets.TouchButton;

    public int TargetFps => PixelTile.TargetFps;

    public TimeSpan UpdateInterval => TimeSpan.FromMilliseconds(500);

    public AnimationFrameInfo RenderAnimatedFrame(CommandContext ctx, IRenderCanvas canvas, AnimationFrameContext frame) =>
        PixelTile.Render(ctx, canvas, surface => Draw(ctx, surface, TileDrawing.BlinkOn(frame.Elapsed)));

    public bool RenderImage(CommandContext ctx, IRenderCanvas canvas)
    {
        PixelTile.Render(ctx, canvas, surface => Draw(ctx, surface, PixelTile.WallClockBlink()));
        return true;
    }

    private void Draw(CommandContext ctx, PixelSurface surface, bool blinkOn)
    {
        TelemetryFrame frame = telemetry.Frame;

        List<SensorRow> rows = SensorReferences(ctx)
            .Take(SensorTileLayout.MaxRows)
            .Select(sensorRef => LibreReadingBuilder.Build(sensorRef, frame.Sensors))
            .ToList();

        SensorTileLayout.Draw(surface, rows, frame, blinkOn);
    }

    /// <summary>
    /// The sensor references to render, in order. On a multi-command button the whole sequence is
    /// available: take the "Sensor" parameter of every sibling that is also a LibreHardwareMonitor.Sensor
    /// command (other commands in the sequence are ignored). A single-command button reports an empty
    /// sequence, so fall back to this command's own parameter.
    /// </summary>
    private static IEnumerable<string?> SensorReferences(CommandContext ctx)
    {
        if (ctx.SequenceCommands.Count > 0)
        {
            foreach (SequenceCommand command in ctx.SequenceCommands)
            {
                if (command.Name != CommandName)
                    continue;

                yield return command.Parameters is { Length: >= 1 } ? command.Parameters[0] : null;
            }

            yield break;
        }

        yield return ctx.Parameters is { Length: >= 1 } ? ctx.Parameters[0] : null;
    }

    public Task Execute(CommandContext ctx) => Task.CompletedTask;
}
