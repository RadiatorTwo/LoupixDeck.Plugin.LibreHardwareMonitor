using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LoupixDeck.Plugin.LibreHardwareMonitor;

/// <summary>
/// Reads sensor data from a running LibreHardwareMonitor instance via its built-in HTTP web server
/// (Options → "Run web server", default port 8085), fetching <c>/data.json</c>. This is the
/// analogue of Argus' shared-memory reader: an external app publishes the data and this service
/// polls it. Current LibreHardwareMonitor no longer exposes a WMI provider, so the web server is the
/// supported interface. When it isn't running/reachable the service reports
/// <see cref="IsAvailable"/> == false and keeps retrying.
/// </summary>
public sealed class LibreHardwareMonitorService : IDisposable
{
    // Poll cadence when reachable; slower retry when the web server is down.
    private static readonly TimeSpan PollDelay = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan ReconnectDelay = TimeSpan.FromSeconds(3);

    // data.json can contain "NaN" (a named floating-point literal), which the default reader rejects.
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals
    };

    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(5) };

    private CancellationTokenSource? _cts;
    private Task? _pollTask;

    private volatile string _dataUrl = "http://localhost:8085/data.json";
    // Base64 of "user:password" for optional HTTP Basic auth; null when no username is set.
    private volatile string? _basicAuth;
    private volatile IReadOnlyList<LibreSensor> _sensors = Array.Empty<LibreSensor>();
    private volatile bool _isAvailable;

    public IReadOnlyList<LibreSensor> Sensors => _sensors;
    public bool IsAvailable => _isAvailable;

    /// <summary>
    /// Sets the LibreHardwareMonitor web-server endpoint and optional HTTP Basic credentials.
    /// Safe to call at any time; the next request uses the new values. Auth is only sent when
    /// <paramref name="username"/> is non-empty (LHM's web-server authentication is optional).
    /// </summary>
    public void Configure(string? baseUrl, string? username = null, string? password = null)
    {
        string root = string.IsNullOrWhiteSpace(baseUrl) ? "http://localhost:8085" : baseUrl.Trim();
        _dataUrl = root.TrimEnd('/') + "/data.json";
        _basicAuth = string.IsNullOrEmpty(username)
            ? null
            : Convert.ToBase64String(Encoding.UTF8.GetBytes($"{username}:{password}"));
    }

    /// <summary>Fetches the sensor tree once, independent of the poll loop (used by "Test Connection").
    /// Returns the sensor count on success; throws on failure.</summary>
    public async Task<int> ProbeAsync()
    {
        IReadOnlyList<LibreSensor> sensors = await FetchAsync(CancellationToken.None).ConfigureAwait(false);
        _sensors = sensors;
        _isAvailable = true;
        return sensors.Count;
    }

    public void Start()
    {
        if (_pollTask != null)
            return;

        _cts = new CancellationTokenSource();
        CancellationToken token = _cts.Token;
        _pollTask = Task.Run(() => PollLoop(token), token);
    }

    public void Stop()
    {
        try { _cts?.Cancel(); } catch { }
        try { _pollTask?.Wait(TimeSpan.FromSeconds(2)); } catch { }
        _pollTask = null;
        _cts?.Dispose();
        _cts = null;
        _isAvailable = false;
        _sensors = Array.Empty<LibreSensor>();
    }

    public void Dispose()
    {
        Stop();
        _http.Dispose();
    }

    private async Task PollLoop(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            bool ok = false;
            try
            {
                IReadOnlyList<LibreSensor> sensors = await FetchAsync(token).ConfigureAwait(false);
                _sensors = sensors;
                _isAvailable = true;
                ok = true;
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                // Web server off, wrong port, or a transient error.
                Console.WriteLine($"LibreHardwareMonitorService: fetch failed, will retry ({ex.Message}).");
            }

            if (!ok)
            {
                _isAvailable = false;
                _sensors = Array.Empty<LibreSensor>();
            }

            try { await Task.Delay(ok ? PollDelay : ReconnectDelay, token).ConfigureAwait(false); }
            catch (OperationCanceledException) { return; }
        }
    }

    private async Task<IReadOnlyList<LibreSensor>> FetchAsync(CancellationToken token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, _dataUrl);
        string? auth = _basicAuth;
        if (auth != null)
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", auth);

        using HttpResponseMessage response = await _http
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        await using Stream stream = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
        DataNode? root = await JsonSerializer
            .DeserializeAsync<DataNode>(stream, JsonOptions, token)
            .ConfigureAwait(false);

        List<LibreSensor> list = new();
        if (root != null)
            WalkNode(root, hardwareName: string.Empty, list);
        return list;
    }

    /// <summary>
    /// Recursively flattens the data.json tree. A node is a sensor when it carries a
    /// <c>SensorId</c>; the nearest ancestor node with a <c>HardwareId</c> supplies the hardware name.
    /// </summary>
    private static void WalkNode(DataNode node, string hardwareName, List<LibreSensor> acc)
    {
        string text = node.Text ?? string.Empty;
        string hardware = node.HardwareId != null ? text : hardwareName;

        if (!string.IsNullOrEmpty(node.SensorId))
        {
            acc.Add(new LibreSensor(
                node.SensorId!,
                text,
                node.Type ?? string.Empty,
                hardware,
                node.Value ?? string.Empty));
        }

        if (node.Children != null)
        {
            foreach (DataNode child in node.Children)
                WalkNode(child, hardware, acc);
        }
    }

    /// <summary>One node of LibreHardwareMonitor's <c>/data.json</c> tree. Only the string fields the
    /// plugin needs are mapped; everything else (Min/Max/RawValue/ImageURL/…) is ignored — those vary
    /// in type across LHM versions (RawValue may be a number or a unit-formatted string), so we never
    /// deserialize them.</summary>
    private sealed class DataNode
    {
        public string? Text { get; set; }
        public string? SensorId { get; set; }
        public string? HardwareId { get; set; }
        public string? Type { get; set; }
        public string? Value { get; set; }
        public List<DataNode>? Children { get; set; }
    }
}
