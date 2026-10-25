using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Voxta.Modules.Inzoi.Mcp;

/// <summary>
/// A small MCP client for inZOI's game server (streamable HTTP, JSON-RPC 2.0). It only does what this
/// module needs: initialize a session, then call tools. inZOI answers with plain JSON or a short SSE
/// stream; both are accepted. The session is set up lazily and again whenever the game restarts.
/// </summary>
public sealed class McpClient : IDisposable
{
    public const string ProtocolVersion = "2024-11-05";

    private readonly HttpClient _http;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private Uri _endpoint;
    private string? _sessionId;
    private bool _initialized;
    private long _nextId;

    public McpClient(Uri endpoint, HttpMessageHandler? handler = null)
    {
        _endpoint = endpoint;
        _http = handler is null ? new HttpClient() : new HttpClient(handler);
        _http.Timeout = TimeSpan.FromSeconds(15);
    }

    public Uri Endpoint => _endpoint;

    /// <summary>The server's name and version from the last successful initialize, e.g. "inzoi-cli 1.0.0".</summary>
    public string? ServerInfo { get; private set; }

    public static Uri EndpointFor(int port) => new($"http://localhost:{port}/mcp");

    /// <summary>Points the client at another port. The next call starts a fresh session.</summary>
    public void SetEndpoint(Uri endpoint)
    {
        if (endpoint == _endpoint) return;
        _endpoint = endpoint;
        Reset();
    }

    /// <summary>Forget the session, e.g. after the game closed. The next call initializes again.</summary>
    public void Reset()
    {
        _initialized = false;
        _sessionId = null;
    }

    /// <summary>
    /// Calls a tool and returns its data: <c>structuredContent</c> when the server sends it, otherwise
    /// the text content parsed as JSON, or as a JSON string when it isn't JSON ("Dispatched: ...").
    /// Throws <see cref="McpToolException"/> when the tool reports an error, and
    /// <see cref="HttpRequestException"/> when the game can't be reached.
    /// </summary>
    public async Task<JsonNode?> CallToolAsync(string name, object? arguments, CancellationToken cancellationToken)
    {
        await EnsureInitializedAsync(cancellationToken);
        try
        {
            return await CallToolOnceAsync(name, arguments, cancellationToken);
        }
        catch (McpSessionExpiredException)
        {
            // The game restarted and doesn't know our session any more.
            Reset();
            await EnsureInitializedAsync(cancellationToken);
            return await CallToolOnceAsync(name, arguments, cancellationToken);
        }
    }

    private async Task<JsonNode?> CallToolOnceAsync(string name, object? arguments, CancellationToken cancellationToken)
    {
        var response = await SendAsync("tools/call", new JsonObject
        {
            ["name"] = name,
            ["arguments"] = arguments is null ? new JsonObject() : JsonSerializer.SerializeToNode(arguments),
        }, cancellationToken);

        return ReadToolResult(name, response);
    }

    /// <summary>Unpacks a tools/call result. Public for tests.</summary>
    public static JsonNode? ReadToolResult(string name, JsonNode? response)
    {
        if (response?["error"] is JsonObject error)
            throw new McpToolException(name, error["message"]?.ToString() ?? error.ToJsonString());

        var result = response?["result"];
        if (result is null) return null;

        var isError = result["isError"] is JsonValue flag && flag.TryGetValue<bool>(out var b) && b;
        var text = result["content"] is JsonArray content
            ? string.Join("\n", content
                .Where(c => c?["type"]?.ToString() == "text")
                .Select(c => c?["text"]?.ToString() ?? ""))
            : null;

        if (isError)
            throw new McpToolException(name, string.IsNullOrWhiteSpace(text) ? "the tool reported an error" : text);

        if (result["structuredContent"] is JsonObject structured)
            return structured.DeepClone();

        if (string.IsNullOrWhiteSpace(text)) return null;
        try
        {
            return JsonNode.Parse(text);
        }
        catch (JsonException)
        {
            return JsonValue.Create(text);
        }
    }

    private async Task EnsureInitializedAsync(CancellationToken cancellationToken)
    {
        if (_initialized) return;
        await _initLock.WaitAsync(cancellationToken);
        try
        {
            if (_initialized) return;
            _sessionId = null;
            var response = await SendAsync("initialize", new JsonObject
            {
                ["protocolVersion"] = ProtocolVersion,
                ["capabilities"] = new JsonObject(),
                ["clientInfo"] = new JsonObject
                {
                    ["name"] = "Voxta.Modules.Inzoi",
                    ["version"] = ModuleVersion,
                },
            }, cancellationToken);

            if (response?["error"] is JsonObject error)
                throw new HttpRequestException($"inZOI refused the MCP session: {error["message"]}");

            var info = response?["result"]?["serverInfo"];
            ServerInfo = info is null ? null : $"{info["name"]} {info["version"]}".Trim();

            await SendAsync("notifications/initialized", null, cancellationToken, notification: true);
            _initialized = true;
        }
        finally
        {
            _initLock.Release();
        }
    }

    /// <summary>Set by the module so the game sees which version is talking to it.</summary>
    public static string ModuleVersion { get; set; } = "0";

    private async Task<JsonNode?> SendAsync(string method, JsonNode? parameters, CancellationToken cancellationToken, bool notification = false)
    {
        var message = new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["method"] = method,
        };
        if (!notification) message["id"] = Interlocked.Increment(ref _nextId);
        if (parameters is not null) message["params"] = parameters;

        using var request = new HttpRequestMessage(HttpMethod.Post, _endpoint)
        {
            Content = new StringContent(message.ToJsonString(), Encoding.UTF8, "application/json"),
        };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        if (_sessionId is not null) request.Headers.TryAddWithoutValidation("Mcp-Session-Id", _sessionId);

        using var response = await _http.SendAsync(request, cancellationToken);

        if (response.Headers.TryGetValues("Mcp-Session-Id", out var ids) && ids.FirstOrDefault() is { Length: > 0 } id)
            _sessionId = id;

        if (_sessionId is not null && response.StatusCode is HttpStatusCode.NotFound)
            throw new McpSessionExpiredException();

        if (notification) return null;

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            if (_sessionId is not null && response.StatusCode == HttpStatusCode.BadRequest && body.Contains("session", StringComparison.OrdinalIgnoreCase))
                throw new McpSessionExpiredException();
            throw new HttpRequestException($"inZOI answered {(int)response.StatusCode} {response.ReasonPhrase} to {method}", null, response.StatusCode);
        }

        return ParseBody(body);
    }

    /// <summary>Plain JSON, or SSE where the JSON is spread over "data:" lines. Public for tests.</summary>
    public static JsonNode? ParseBody(string body)
    {
        var trimmed = body.Trim();
        if (trimmed.Length == 0) return null;
        if (trimmed[0] is '{' or '[') return JsonNode.Parse(trimmed);

        // SSE: one or more events. The response is the last event whose data is a JSON-RPC response.
        JsonNode? last = null;
        var data = new StringBuilder();
        foreach (var rawLine in trimmed.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');
            if (line.StartsWith("data:", StringComparison.Ordinal))
            {
                data.Append(line.AsSpan(5).TrimStart(' '));
            }
            else if (line.Length == 0 && data.Length > 0)
            {
                last = TryParse(data.ToString()) ?? last;
                data.Clear();
            }
        }

        if (data.Length > 0) last = TryParse(data.ToString()) ?? last;
        return last;
    }

    private static JsonNode? TryParse(string json)
    {
        try
        {
            var node = JsonNode.Parse(json);
            return node is JsonObject obj && (obj.ContainsKey("result") || obj.ContainsKey("error")) ? node : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public void Dispose()
    {
        _http.Dispose();
        _initLock.Dispose();
    }
}

public sealed class McpToolException(string tool, string message) : Exception($"{tool}: {message}")
{
    public string Tool { get; } = tool;
}

internal sealed class McpSessionExpiredException : Exception;
