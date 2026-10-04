using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.NetworkInformation;
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

    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(5);

    private readonly HttpClient _http = new() { Timeout = RequestTimeout };

    private CancellationTokenSource? _cts;
    private Task? _pollTask;

    // Null when the configured URL is not an absolute http(s) URL.
    private volatile Uri? _dataUrl = new("http://localhost:8085/data.json");
    // Base64 of "user:password" for optional HTTP Basic auth; null when no username is set.
    private volatile string? _basicAuth;
    private volatile IReadOnlyList<LibreSensor> _sensors = Array.Empty<LibreSensor>();
    private volatile bool _isAvailable;

    private volatile LibreDiagnostics _status = new(NotStarted, []);
    private volatile LibreDiagnostics? _lastError;

    // Set once a fetch attempt has finished, so the status describes a real outcome.
    private volatile bool _attempted;

    public IReadOnlyList<LibreSensor> Sensors => _sensors;
    public bool IsAvailable => _isAvailable;

    /// <summary>What the service is doing right now, as a format string and its arguments so the
    /// plugin can translate it.</summary>
    public LibreDiagnostics Status => _status;

    /// <summary>The most recent problem, kept after the status has moved on; null if none yet.</summary>
    public LibreDiagnostics? LastError => _lastError;

    private void SetStatus(string format, params object[] args) => _status = new LibreDiagnostics(format, args);

    /// <summary>Receives a line per error event; the plugin decides whether it reaches a log.</summary>
    public Action<string>? Log { get; set; }

    private void SetError(string format, params object[] args)
    {
        LibreDiagnostics error = new(format, args);
        _lastError = error;
        Log?.Invoke($"LibreHardwareMonitorService: {error.Text}");
    }

    /// <summary>
    /// Sets the LibreHardwareMonitor web-server endpoint and optional HTTP Basic credentials.
    /// Safe to call at any time; the next request uses the new values. Auth is only sent when
    /// <paramref name="username"/> is non-empty (LHM's web-server authentication is optional).
    /// </summary>
    public void Configure(string? baseUrl, string? username = null, string? password = null)
    {
        string root = string.IsNullOrWhiteSpace(baseUrl) ? "http://localhost:8085" : baseUrl.Trim();
        // "localhost:8085" parses as an absolute URI with the scheme "localhost", so check the scheme.
        _dataUrl = Uri.TryCreate(root.TrimEnd('/') + "/data.json", UriKind.Absolute, out Uri? url)
                   && (url.Scheme == Uri.UriSchemeHttp || url.Scheme == Uri.UriSchemeHttps)
            ? url
            : null;
        _basicAuth = string.IsNullOrEmpty(username)
            ? null
            : Convert.ToBase64String(Encoding.UTF8.GetBytes($"{username}:{password}"));
    }

    /// <summary>Fetches the sensor tree once, independent of the poll loop (used by "Test Connection").
    /// Records the outcome in <see cref="Status"/> and <see cref="LastError"/>; true on success.</summary>
    public Task<bool> ProbeAsync() => AttemptAsync(CancellationToken.None);

    /// <summary>
    /// Why the web server cannot be read right now (an English key of the strings files), or null
    /// when it can. Never waits: it answers from the latest attempt. Right after <see cref="Start"/>
    /// no attempt has finished yet (a refused connection to localhost takes about 4 s on Windows);
    /// then an invalid URL and a local URL whose port nobody listens on are reported at once, and
    /// anything else counts as no problem rather than a false one.
    /// </summary>
    public string? CurrentProblem()
    {
        if (!_attempted)
        {
            return _dataUrl switch
            {
                null => BadUrl,
                { IsLoopback: true } url when !IsListening(url.Port) => NotReachable,
                _ => null
            };
        }

        if (_isAvailable)
            return _sensors.Count > 0 ? null : NoSensors;

        return _status.Format;
    }

    private static bool IsListening(int port)
    {
        try
        {
            foreach (IPEndPoint endPoint in IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners())
            {
                if (endPoint.Port == port)
                    return true;
            }

            return false;
        }
        catch (Exception)
        {
            // Cannot tell; let the request decide.
            return true;
        }
    }

    public void Start()
    {
        if (_pollTask != null)
            return;

        SetStatus("Connecting to the web server.");
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
        SetStatus(NotStarted);
        _attempted = false;
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
            bool ok;
            try
            {
                ok = await AttemptAsync(token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                return;
            }

            try { await Task.Delay(ok ? PollDelay : ReconnectDelay, token).ConfigureAwait(false); }
            catch (OperationCanceledException) { return; }
        }
    }

    /// <summary>
    /// Fetches the sensor tree once and publishes it, or records why that failed and clears the
    /// sensors, so tiles show the unavailable state until the web server answers again. Throws only
    /// when <paramref name="token"/> is cancelled.
    /// </summary>
    private async Task<bool> AttemptAsync(CancellationToken token)
    {
        try
        {
            IReadOnlyList<LibreSensor> sensors = await FetchAsync(token).ConfigureAwait(false);
            _sensors = sensors;
            _isAvailable = true;
            if (sensors.Count > 0)
                SetStatus("Connected — {0} sensor(s)", sensors.Count);
            else
                SetStatus(NoSensors);
            _attempted = true;
            return true;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Web server off, wrong port or URL, credentials, or a transient error.
            Fail(ex);
            _isAvailable = false;
            _sensors = Array.Empty<LibreSensor>();
            _attempted = true;
            return false;
        }
    }

    /// <summary>Records the reason of a failed request as status, and its details as last error.</summary>
    private void Fail(Exception ex)
    {
        switch (ex)
        {
            case HttpRequestException { StatusCode: HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden } http:
                SetStatus(_basicAuth is null ? LoginRequired : LoginRejected);
                SetError("HTTP {0} ({1}).", (int)http.StatusCode!.Value, http.StatusCode.Value);
                break;
            case HttpRequestException { StatusCode: { } status }:
                SetStatus(HttpError);
                SetError("HTTP {0} ({1}).", (int)status, status);
                break;
            case HttpRequestException http:
                SetStatus(NotReachable);
                SetError("Connection failed ({0}).", http.InnerException?.Message ?? http.Message);
                break;
            // HttpClient reports its timeout as a cancellation the caller did not ask for.
            case OperationCanceledException:
                SetStatus(NoAnswer);
                SetError("No answer within {0} s.", (int)RequestTimeout.TotalSeconds);
                break;
            case UriFormatException:
                SetStatus(BadUrl);
                break;
            case JsonException json:
                SetStatus(NotLibreData);
                SetError("Unexpected answer ({0}).", json.Message);
                break;
            default:
                SetStatus("Reading failed — retrying.");
                SetError("Reading failed ({0}: {1}).", ex.GetType().Name, ex.Message);
                break;
        }
    }

    // Why the web server cannot be read; English keys of the plugin's strings files.
    private const string NotStarted = "Not started";
    private const string NotReachable =
        "Not reachable — is LibreHardwareMonitor running with Options → 'Run web server' turned on?";
    private const string NoAnswer = "The web server does not answer — retrying.";
    private const string LoginRequired =
        "The web server requires a login — enter its username and password in the plugin settings.";
    private const string LoginRejected = "The web server rejected the username or password.";
    private const string HttpError = "The web server answered with an error — check the web server URL.";
    private const string BadUrl = "The web server URL is not valid — use e.g. http://localhost:8085.";
    private const string NotLibreData =
        "The answer is not LibreHardwareMonitor sensor data — check the web server URL.";
    private const string NoSensors = "Connected, but LibreHardwareMonitor reports no sensors.";

    private async Task<IReadOnlyList<LibreSensor>> FetchAsync(CancellationToken token)
    {
        Uri dataUrl = _dataUrl ?? throw new UriFormatException("The web server URL is not a valid http(s) URL.");
        using var request = new HttpRequestMessage(HttpMethod.Get, dataUrl);
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

        // LibreHardwareMonitor's root node always has children (the computer); JSON without them is
        // some other server's answer.
        if (root?.Children is null)
            throw new JsonException("No sensor tree in the answer.");

        List<LibreSensor> list = new();
        WalkNode(root, hardwareId: string.Empty, hardwareName: string.Empty, list, new Dictionary<string, int>());
        return list;
    }

    /// <summary>
    /// Recursively flattens the data.json tree. A node is a sensor when it carries a
    /// <c>SensorId</c>; the nearest ancestor node with a <c>HardwareId</c> supplies the hardware.
    /// <paramref name="seen"/> counts identifiers, so a repeated one gets a distinct key.
    /// </summary>
    private static void WalkNode(DataNode node, string hardwareId, string hardwareName, List<LibreSensor> acc,
        Dictionary<string, int> seen)
    {
        string text = node.Text ?? string.Empty;
        if (node.HardwareId != null)
        {
            hardwareId = node.HardwareId;
            hardwareName = text;
        }

        if (!string.IsNullOrEmpty(node.SensorId))
        {
            string id = node.SensorId!;
            int repeat = seen.GetValueOrDefault(id);
            seen[id] = repeat + 1;

            string valueText = node.Value ?? string.Empty;
            (double value, string unit) = SensorValue.Parse(valueText);
            acc.Add(new LibreSensor(
                id,
                repeat == 0 ? id : $"{id}#{repeat}",
                text,
                node.Type ?? string.Empty,
                hardwareId,
                hardwareName,
                valueText,
                value,
                unit));
        }

        if (node.Children != null)
        {
            foreach (DataNode child in node.Children)
                WalkNode(child, hardwareId, hardwareName, acc, seen);
        }
    }

    /// <summary>One node of LibreHardwareMonitor's <c>/data.json</c> tree. Only the string fields the
    /// plugin needs are mapped; everything else (Min/Max/RawValue/ImageURL/…) is ignored — those vary
    /// in type across LHM versions (RawValue may be a number or a unit-formatted string, and a raw
    /// rate is in B/s while Value is in KB/s or MB/s), so the number is always parsed from Value,
    /// together with the unit it goes with.</summary>
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

/// <summary>A status message of <see cref="LibreHardwareMonitorService"/>: an English format string and
/// its arguments, translated by the plugin when shown.</summary>
public sealed record LibreDiagnostics(string Format, object[] Args)
{
    public string Text => string.Format(CultureInfo.InvariantCulture, Format, Args);
}
