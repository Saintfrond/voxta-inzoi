using System.Net.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Voxta.Modules.Inzoi.Mcp;

namespace Voxta.Modules.Inzoi.Game;

/// <summary>
/// The one link to the running game, shared by every chat. inZOI serves an MCP interface on
/// localhost while it runs; this polls it for the household's state and sends speech bubbles back.
///
/// It only polls while at least one chat holds a lease (<see cref="Acquire"/>), so an idle Voxta
/// doesn't knock on the game's door every few seconds. When the game isn't running, it tries again
/// at a slower pace until it is.
/// </summary>
public sealed class InzoiGame : IAsyncDisposable
{
    public const int DefaultPort = 11212;

    private static readonly Lock SharedGate = new();
    private static InzoiGame? _shared;

    /// <summary>The game for this process. Voxta may run a module's setup more than once.</summary>
    public static InzoiGame GetOrCreate()
    {
        lock (SharedGate)
        {
            if (_shared is null || _shared._disposed)
                _shared = new InzoiGame();
            return _shared;
        }
    }

    private InzoiGame()
    {
        _client = new McpClient(McpClient.EndpointFor(DefaultPort));
        _reader = new GameReader(_client.CallToolAsync);
        McpClient.ModuleVersion = ModuleInfo.Version;
    }

    /// <summary>When the game isn't there, look again this often.</summary>
    private static readonly TimeSpan OfflineRetryDelay = TimeSpan.FromSeconds(15);

    /// <summary>Job status, wishes and relationships change slowly; read them every this many polls.</summary>
    private const int SlowDetailsEvery = 4;

    private readonly McpClient _client;
    private readonly GameReader _reader;
    private readonly Lock _gate = new();
    private ILogger _logger = NullLogger.Instance;
    private bool _disposed;

    private int _leases;
    private CancellationTokenSource? _loopCts;
    private Task? _loop;
    private TimeSpan _interval = TimeSpan.FromSeconds(5);
    private bool _includeRelationships = true;
    private readonly SemaphoreSlim _wake = new(0, int.MaxValue);
    private string? _lastProblems;

    /// <summary>The most recent snapshot. Kept after the game closes, so a chat can still say what it last saw.</summary>
    public GameSnapshot? State { get; private set; }

    /// <summary>What happened in the household lately, for the characters' context.</summary>
    public HappeningsLog Happenings { get; } = new();

    public bool IsConnected { get; private set; }

    public string? ServerInfo => _client.ServerInfo;

    /// <summary>Raised on the polling thread after every successful read. Handlers must not block it.</summary>
    public event Action<GameSnapshot>? StateChanged;

    public event Action<GameEvent>? EventReceived;

    public event Action<bool>? ConnectionChanged;

    public void AttachLogger(ILoggerFactory loggerFactory)
    {
        if (_logger is not NullLogger) return;
        _logger = loggerFactory.CreateLogger<InzoiGame>();
    }

    /// <summary>
    /// Starts polling with these settings until the returned lease is disposed. Settings from the
    /// most recent chat win; they are the same module settings for everyone anyway.
    /// </summary>
    public IAsyncDisposable Acquire(int port, TimeSpan interval, bool includeRelationships)
    {
        lock (_gate)
        {
            _client.SetEndpoint(McpClient.EndpointFor(port));
            _interval = interval < TimeSpan.FromSeconds(2) ? TimeSpan.FromSeconds(2) : interval;
            _includeRelationships = includeRelationships;
            _leases++;
            if (_loop is null || _loop.IsCompleted)
            {
                _loopCts = new CancellationTokenSource();
                var token = _loopCts.Token;
                _loop = Task.Run(() => PollLoopAsync(token));
                _logger.LogInformation("Watching inZOI on {Endpoint}", _client.Endpoint);
            }
        }

        return new Lease(this);
    }

    private void Release()
    {
        CancellationTokenSource? cts = null;
        lock (_gate)
        {
            if (--_leases > 0) return;
            _leases = 0;
            cts = _loopCts;
            _loopCts = null;
            _loop = null;
        }

        cts?.Cancel();
        _logger.LogInformation("No chat uses inZOI any more; stopped watching the game");
    }

    /// <summary>Read the game now rather than at the next tick, e.g. when a chat starts.</summary>
    public void RefreshSoon() => _wake.Release();

    private async Task PollLoopAsync(CancellationToken cancellationToken)
    {
        var count = 0;
        while (!cancellationToken.IsCancellationRequested)
        {
            var delay = _interval;
            try
            {
                var slow = count++ % SlowDetailsEvery == 0 || State is null || !State.InGame;
                var snapshot = await _reader.ReadAsync(State, slow, _includeRelationships, cancellationToken);
                SetConnected(true);
                LogProblems();

                var previous = State;
                var events = EventDetector.Compare(previous, snapshot);
                if (snapshot.InGame) await RecordHappeningsAsync(snapshot, events, cancellationToken);

                State = snapshot;
                Raise(StateChanged, snapshot);
                foreach (var gameEvent in events)
                {
                    _logger.LogDebug("inZOI event {Kind}: {Text}", gameEvent.Kind, gameEvent.Text);
                    Raise(EventReceived, gameEvent);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exc) when (exc is HttpRequestException or TaskCanceledException or IOException)
            {
                // The game isn't running (or is still starting up). Ordinary; try again later.
                if (IsConnected) _logger.LogInformation("inZOI is no longer reachable ({Reason})", exc.Message);
                _client.Reset();
                SetConnected(false);
                delay = OfflineRetryDelay;
                count = 0;
            }
            catch (Exception exc)
            {
                _logger.LogWarning(exc, "Reading the inZOI household failed");
                delay = OfflineRetryDelay;
            }

            try
            {
                await _wake.WaitAsync(delay, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    /// <summary>Adds this read's social interactions and events to the household's recent history.</summary>
    private async Task RecordHappeningsAsync(GameSnapshot snapshot, List<GameEvent> events, CancellationToken cancellationToken)
    {
        Happenings.SetHousehold(snapshot.HouseholdId);
        var time = snapshot.World?.Time;

        foreach (var (actor, targetId, targetName, interaction) in HappeningsLog.SocialNow(snapshot))
        {
            if (!Happenings.IsNewSocial(actor.Id, targetId, interaction)) continue;
            var what = await DescribeInteractionAsync(actor.Id, targetId, interaction, cancellationToken);
            if (what is null) continue;
            var text = $"{actor.FullName} → {targetName}: {what}";
            _logger.LogDebug("inZOI social: {Text} ({Interaction})", text, interaction);
            Happenings.Add(new Happening(DateTimeOffset.UtcNow, time, [actor.Id, targetId], text));
        }

        foreach (var gameEvent in events.Where(e => e.Kind != "new_wish"))
            Happenings.Add(new Happening(DateTimeOffset.UtcNow, time, gameEvent.ZoiId is { } id ? [id, .. gameEvent.OtherIds] : gameEvent.OtherIds, gameEvent.Text));
    }

    private readonly Dictionary<string, string?> _interactionNames = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The game's own name for a social interaction ("Nitpick", "Give Heartfelt Hug"), from the menu the
    /// actor would see for that target. Interactions that aren't in that menu (the listener's side of a
    /// talk, a reaction) fall back to readable words from their id, or are left out when that says nothing.
    /// </summary>
    private async Task<string?> DescribeInteractionAsync(long actorId, long targetId, string interactionId, CancellationToken cancellationToken)
    {
        lock (_interactionNames)
        {
            if (_interactionNames.TryGetValue(interactionId, out var known)) return known;
        }

        var offers = await ListInteractionsAsync(actorId, targetId, cancellationToken);
        lock (_interactionNames)
        {
            foreach (var offer in offers)
            {
                if (!string.IsNullOrWhiteSpace(offer.DisplayName))
                    _interactionNames.TryAdd(offer.BagId, offer.DisplayName.Trim());
            }

            if (_interactionNames.TryGetValue(interactionId, out var found)) return found;
            var words = Household.Phrases.SocialWords(interactionId);
            _interactionNames[interactionId] = words;
            return words;
        }
    }

    private void LogProblems()
    {
        var problems = _reader.Problems.Count == 0 ? null : string.Join("; ", _reader.Problems.Distinct());
        if (problems == _lastProblems) return;
        _lastProblems = problems;
        if (problems is not null)
            _logger.LogWarning("Some inZOI details could not be read (after a game update?): {Problems}", problems);
    }

    private void SetConnected(bool connected)
    {
        if (IsConnected == connected) return;
        IsConnected = connected;
        if (connected) _logger.LogInformation("Connected to inZOI ({Server})", _client.ServerInfo ?? "MCP");
        Raise(ConnectionChanged, connected);
    }

    // ---- Into the game ----

    /// <summary>A speech bubble above the Zoi's head. Returns false when the game isn't there.</summary>
    public async Task<bool> ShowSpeechBubbleAsync(long zoiId, string text, double seconds, CancellationToken cancellationToken)
    {
        return await TryCallAsync("entity_show_speech_bubble", new
        {
            text,
            entity_id = Id(zoiId),
            duration = seconds,
        }, cancellationToken);
    }

    /// <summary>A short system message at the top of the screen, for things no Zoi says.</summary>
    public Task<bool> ShowToastAsync(string message, double seconds, CancellationToken cancellationToken) =>
        TryCallAsync("toast_show", new { message, lifetime = seconds }, cancellationToken);

    /// <summary>Plays one of the game's character animations on a Zoi. Interrupts what they are doing for its length.</summary>
    public Task<bool> PlayMotionAsync(long zoiId, string animId, CancellationToken cancellationToken) =>
        TryCallAsync("motion_play", new { anim_id = animId, entity_id = Id(zoiId) }, cancellationToken);

    /// <summary>
    /// Plays an animation and says why not when the game refuses it (wrong age, gender or posture,
    /// unknown id). For trying a list of candidates, where a refusal is expected and not worth a warning.
    /// </summary>
    public async Task<string?> TryPlayMotionAsync(long zoiId, string animId, CancellationToken cancellationToken)
    {
        if (!IsConnected) return "the game isn't running";
        try
        {
            await _client.CallToolAsync("motion_play", new { anim_id = animId, entity_id = Id(zoiId) }, cancellationToken);
            return null;
        }
        catch (McpToolException exc)
        {
            return exc.Message;
        }
        catch (Exception exc) when (exc is HttpRequestException or TaskCanceledException or IOException)
        {
            return "the game didn't answer";
        }
    }

    public Task<bool> StopMotionAsync(long zoiId, string animId, CancellationToken cancellationToken) =>
        TryCallAsync("motion_stop", new { anim_id = animId, entity_id = Id(zoiId) }, cancellationToken);

    public Task<bool> SetFaceAsync(long zoiId, string expression, CancellationToken cancellationToken) =>
        TryCallAsync("facial_set", new { Expression = expression, entity_id = Id(zoiId) }, cancellationToken);

    /// <summary>Hands the player's control (and with it the camera) to this Zoi.</summary>
    public Task<bool> PossessAsync(long zoiId, CancellationToken cancellationToken) =>
        TryCallAsync("character_possess", new { character_id = Id(zoiId) }, cancellationToken);

    private readonly Dictionary<string, double> _motionLengths = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>How long an animation runs, in seconds. Null when the game doesn't know it for this Zoi.</summary>
    public async Task<double?> GetMotionLengthAsync(long zoiId, string animId, CancellationToken cancellationToken)
    {
        lock (_motionLengths)
        {
            if (_motionLengths.TryGetValue(animId, out var known)) return known;
        }

        if (!IsConnected) return null;
        try
        {
            var info = await _client.CallToolAsync("motion_get_info", new { anim_id = animId, entity_id = Id(zoiId) }, cancellationToken);
            var length = GameReader.Double(info, "total_length");
            if (length is not > 0) return null;
            lock (_motionLengths) _motionLengths[animId] = length.Value;
            return length;
        }
        catch (Exception exc) when (exc is McpToolException or HttpRequestException or TaskCanceledException or IOException)
        {
            return null;
        }
    }

    private Dictionary<string, string>? _faceByLabel;

    /// <summary>
    /// The facial expression the game uses for an emotion. Emotion and expression ids differ (Enchanting 2
    /// is "Lovestruck", whose face is "Sensual"); they share the game's word for it.
    /// </summary>
    public async Task<string?> FaceForEmotionAsync(EmotionInfo? emotion, CancellationToken cancellationToken)
    {
        if (emotion is null) return null;
        if (_faceByLabel is null && IsConnected)
        {
            try
            {
                var list = await _client.CallToolAsync("facial_list", null, cancellationToken);
                var faces = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var face in GameReader.Get(list, "expressions") as System.Text.Json.Nodes.JsonArray ?? [])
                {
                    if (GameReader.Str(face, "id") is { Length: > 0 } id && GameReader.Str(face, "l10n") is { Length: > 0 } label)
                        faces.TryAdd(label, id);
                }

                _faceByLabel = faces;
            }
            catch (Exception exc) when (exc is McpToolException or HttpRequestException or TaskCanceledException or IOException)
            {
                return null;
            }
        }

        if (emotion.Label is { } l && _faceByLabel?.TryGetValue(l, out var byLabel) == true) return byLabel;
        return emotion.Type;
    }

    private static string Id(long zoiId) => zoiId.ToString(System.Globalization.CultureInfo.InvariantCulture);

    // ---- Interactions: making a Zoi do things ----

    /// <summary>What the actor could do with the target (a Zoi or an object) right now, as the game's own menu would offer it.</summary>
    public async Task<IReadOnlyList<Offer>> ListInteractionsAsync(long actorId, long targetId, CancellationToken cancellationToken)
    {
        if (!IsConnected) return [];
        try
        {
            var list = await _client.CallToolAsync("interaction_list_available", new { entity_id = Id(actorId), target_entity_id = Id(targetId) }, cancellationToken);
            return (GameReader.Get(list, "interactions") as System.Text.Json.Nodes.JsonArray ?? [])
                .Select(i => new Offer(
                    GameReader.Str(i, "interaction_bag_id") ?? "",
                    GameReader.Str(i, "display_name") ?? "",
                    GameReader.Bool(i, "enabled") ?? true,
                    GameReader.Str(i, "hint")))
                .Where(o => o.BagId.Length > 0)
                .ToArray();
        }
        catch (Exception exc) when (exc is McpToolException or HttpRequestException or TaskCanceledException or IOException)
        {
            _logger.LogDebug("Could not list inZOI interactions for {Actor} → {Target}: {Reason}", actorId, targetId, exc.Message);
            return [];
        }
    }

    /// <summary>Queues an interaction: the Zoi walks over and does it. Returns null on success, else the game's reason.</summary>
    public async Task<string?> ExecuteInteractionAsync(long actorId, string bagId, long? targetId, CancellationToken cancellationToken)
    {
        if (!IsConnected) return "the game isn't running";
        try
        {
            await _client.CallToolAsync("interaction_execute", targetId is { } t
                ? new { entity_id = Id(actorId), interaction_bag_id = bagId, target_entity_id = Id(t), insert_front = true }
                : (object)new { entity_id = Id(actorId), interaction_bag_id = bagId, insert_front = true }, cancellationToken);
            return null;
        }
        catch (McpToolException exc)
        {
            return exc.Message.StartsWith(exc.Tool + ": ", StringComparison.Ordinal) ? exc.Message[(exc.Tool.Length + 2)..] : exc.Message;
        }
        catch (Exception exc) when (exc is HttpRequestException or TaskCanceledException or IOException)
        {
            return "the game didn't answer";
        }
    }

    public sealed record GameObject(long Id, string TableId);

    private (DateTimeOffset At, IReadOnlyList<GameObject> Objects)? _objects;

    /// <summary>The objects on the lot the player is on (furniture, appliances), cached for a minute.</summary>
    public async Task<IReadOnlyList<GameObject>> GetObjectsAsync(CancellationToken cancellationToken)
    {
        if (_objects is { } cached && DateTimeOffset.UtcNow - cached.At < TimeSpan.FromMinutes(1)) return cached.Objects;
        if (!IsConnected) return [];
        try
        {
            var scan = await _client.CallToolAsync("build_scan_objects", new { Limit = 500 }, cancellationToken);
            var objects = (GameReader.Get(scan, "objects") as System.Text.Json.Nodes.JsonArray ?? [])
                .Select(o => (Id: GameReader.Long(o, "entity_id"), Table: GameReader.Str(o, "table_id")))
                .Where(o => o.Id is not null && !string.IsNullOrWhiteSpace(o.Table))
                .Select(o => new GameObject(o.Id!.Value, o.Table!))
                .ToArray();
            _objects = (DateTimeOffset.UtcNow, objects);
            return objects;
        }
        catch (Exception exc) when (exc is McpToolException or HttpRequestException or TaskCanceledException or IOException)
        {
            _logger.LogDebug("Could not list the objects on the lot: {Reason}", exc.Message);
            return [];
        }
    }

    private async Task<bool> TryCallAsync(string tool, object arguments, CancellationToken cancellationToken)
    {
        if (!IsConnected) return false;
        try
        {
            await _client.CallToolAsync(tool, arguments, cancellationToken);
            return true;
        }
        catch (McpToolException exc)
        {
            _logger.LogWarning("inZOI refused {Tool}: {Reason}", tool, exc.Message);
            return false;
        }
        catch (Exception exc) when (exc is HttpRequestException or TaskCanceledException or IOException)
        {
            _logger.LogDebug(exc, "Could not reach inZOI for {Tool}; it probably just closed", tool);
            return false;
        }
    }

    private void Raise<T>(Action<T>? handler, T value)
    {
        if (handler is null) return;
        foreach (var single in handler.GetInvocationList().Cast<Action<T>>())
        {
            try
            {
                single(value);
            }
            catch (Exception exc)
            {
                // One chat's bug must not cut the game off from the others.
                _logger.LogWarning(exc, "An inZOI listener failed");
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        _disposed = true;
        CancellationTokenSource? cts;
        Task? loop;
        lock (_gate)
        {
            cts = _loopCts;
            loop = _loop;
            _loopCts = null;
            _loop = null;
            _leases = 0;
        }

        if (cts is not null) await cts.CancelAsync();
        if (loop is not null)
        {
            try
            {
                await loop;
            }
            catch
            {
                // Stopping anyway.
            }
        }

        _client.Dispose();
    }

    private sealed class Lease(InzoiGame game) : IAsyncDisposable
    {
        private int _released;

        public ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0) game.Release();
            return ValueTask.CompletedTask;
        }
    }
}
