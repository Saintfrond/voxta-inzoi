using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Voxta.Abstractions.Chats.Objects.Characters;
using Voxta.Abstractions.Chats.Sessions;
using Voxta.Abstractions.Model;
using Voxta.Abstractions.Scripting.ActionScripts;
using Voxta.Abstractions.Services.ChatAugmentations;
using Voxta.Model.Shared;
using Voxta.Modules.Inzoi.Game;
using Voxta.Modules.Inzoi.Household;

namespace Voxta.Modules.Inzoi.ChatAugmentations;

/// <summary>
/// One chat with the household. It keeps the chat's context in step with the game, passes notable
/// game events to the characters, and shows what they say as speech bubbles above their Zois.
///
/// Characters are matched to Zois by name. Everyone in the chat is known from the start; characters
/// a scenario enables later are picked up the first time they speak.
/// </summary>
public sealed class InzoiChatAugmentationInstance : IChatScriptEventsAugmentation
{
    /// <summary>Needs and activities change every few seconds; the context is rebuilt at most this often.</summary>
    private static readonly TimeSpan ContextDebounce = TimeSpan.FromSeconds(3);

    private readonly IChatSessionChatAugmentationApi _session;
    private readonly InzoiGame _game;
    private readonly InzoiSettings _settings;
    private readonly ILogger _logger;
    private readonly ZoiMatcher _matcher;
    private readonly GameActions _actions;
    private string? _registeredActionTargets;
    private readonly CancellationTokenSource _cts;
    private IAsyncDisposable? _lease;

    /// <summary>Names of the characters seen in this chat.</summary>
    private readonly ConcurrentDictionary<string, byte> _characters = new(StringComparer.OrdinalIgnoreCase);

    private readonly SemaphoreSlim _publishLock = new(1, 1);
    private int _publishScheduled;
    private string? _lastContext;
    private bool _warnedNoMatch;

    private volatile bool _generating;
    private volatile bool _userTyping;
    private DateTimeOffset _lastReaction = DateTimeOffset.MinValue;

    /// <summary>The speech bubbles of the reply being shown; a new reply cancels the rest of the previous one.</summary>
    private CancellationTokenSource? _bubbles;

    public InzoiChatAugmentationInstance(
        IChatSessionChatAugmentationApi session,
        InzoiGame game,
        InzoiSettings settings,
        ILogger<InzoiChatAugmentationInstance> logger
    )
    {
        _session = session;
        _game = game;
        _settings = settings;
        _logger = logger;
        _matcher = new ZoiMatcher(settings.NameAliases);
        _actions = new GameActions(game, logger);
        _cts = CancellationTokenSource.CreateLinkedTokenSource(session.Abort);
        // Everyone in the chat from the start, so every Zoi's state is there for the first reply.
        // Characters a scenario brings in later are picked up when they first speak.
        _characters.TryAdd(session.MainCharacter.Name, 0);
        foreach (var character in session.Chat.GetCharacters())
        {
            if (character.Enabled)
                _characters.TryAdd(character.Name, 0);
        }

        _game.StateChanged += OnStateChanged;
        _game.ConnectionChanged += OnConnectionChanged;
        _game.EventReceived += OnGameEvent;
    }

    public ServiceTypes[] GetRequiredServiceTypes() => [];

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _lease = _game.Acquire(_settings.Port, _settings.PollInterval, _settings.IncludeRelationships);
        // A fresh read rather than one that may be minutes old; the context follows when it lands.
        _game.RefreshSoon();
        await PublishContextAsync(cancellationToken);
    }

    // ---- Game → chat ----

    private void OnStateChanged(GameSnapshot state)
    {
        ScheduleActionRegistration(state);
        WarnIfUnmatched(state);
        ScheduleContextUpdate();
    }

    private void OnConnectionChanged(bool connected) => ScheduleContextUpdate();

    private void WarnIfUnmatched(GameSnapshot state)
    {
        if (_warnedNoMatch || !state.InGame || state.Zois.Length == 0) return;
        _warnedNoMatch = true;
        if (Match(_session.MainCharacter.Name, state) is null)
            _logger.LogWarning(
                "No Zoi in the household matches the character '{Character}'. Household: {Zois}. Add a line under 'Character → Zoi names' in the module settings.",
                _session.MainCharacter.Name,
                string.Join(", ", state.Zois.Select(z => z.FullName)));
    }

    private void ScheduleContextUpdate()
    {
        if (Interlocked.Exchange(ref _publishScheduled, 1) == 1) return;
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(ContextDebounce, _cts.Token);
                Interlocked.Exchange(ref _publishScheduled, 0);
                await PublishContextAsync(_cts.Token);
            }
            catch (OperationCanceledException)
            {
                // The chat ended.
            }
            catch (Exception exc)
            {
                Interlocked.Exchange(ref _publishScheduled, 0);
                _logger.LogWarning(exc, "Could not update the chat with the household's state");
            }
        });
    }

    /// <summary>Replaces this module's context in the chat with the latest household state.</summary>
    private async Task PublishContextAsync(CancellationToken cancellationToken)
    {
        await _publishLock.WaitAsync(cancellationToken);
        try
        {
            var state = _game.State;
            var text = HouseholdContextBuilder.Build(
                state,
                _game.IsConnected,
                Participants(state),
                new HouseholdContextBuilder.Options(_settings.UserRole, _settings.CustomUserRole)
            );

            // Most reads change nothing the characters would notice (a need moving from 63 to 61).
            if (text == _lastContext) return;
            _lastContext = text;

            await _session.SetContexts(
                InzoiConstants.ContextKey,
                [new ContextDefinition { Name = "The inZOI household", Text = text }],
                cancellationToken
            );
            _logger.LogDebug("Updated the household context:\n{Context}", text);
        }
        finally
        {
            _publishLock.Release();
        }
    }

    private void OnGameEvent(GameEvent gameEvent)
    {
        if (_settings.GameEvents == Choices.EventsOff) return;
        if (string.IsNullOrWhiteSpace(gameEvent.Text)) return;

        // Events about the Zois in this chat, plus anything big enough to concern the whole household.
        var participants = Participants(_game.State);
        var concernsChat = gameEvent.ZoiId is { } id && participants.ContainsKey(id);
        if (!concernsChat && gameEvent.Importance < 2) return;

        _ = Task.Run(async () =>
        {
            try
            {
                await _session.SendSecretAsync($"[In the game: {gameEvent.Text}]", _cts.Token);

                if (_settings.GameEvents != Choices.EventsReact) return;
                if (gameEvent.Importance < 1) return;
                if (_generating || _userTyping) return;
                var now = DateTimeOffset.UtcNow;
                if (now - _lastReaction < _settings.ReactCooldown) return;
                _lastReaction = now;

                // The context should describe the moment being reacted to, not the one before it.
                await PublishContextAsync(_cts.Token);
                await _session.TriggerReplyAsync(_cts.Token);
            }
            catch (OperationCanceledException)
            {
                // The chat ended.
            }
            catch (Exception exc)
            {
                _logger.LogWarning(exc, "Could not pass a game event to the chat");
            }
        });
    }

    // ---- Chat → game ----

    public async Task OnChatScriptEvent(
        IActionScriptEvent e,
        ChatMessageData? message,
        ICharacterOrUserData? character,
        CancellationToken cancellationToken
    )
    {
        switch (e)
        {
            case GeneratingActionScriptEvent generating:
                _generating = true;
                if (_characters.TryAdd(generating.Character.Name, 0))
                {
                    // A character from a group chat speaking for the first time: describe their Zoi too.
                    _logger.LogInformation("'{Character}' joined the household chat", generating.Character.Name);
                    await PublishContextAsync(cancellationToken);
                }
                break;

            case GeneratingCompleteActionScriptEvent complete:
                _generating = false;
                var speaker = Match(complete.Character.Name, _game.State);
                if (_settings.ShowRepliesInGame)
                    ShowBubbles(speaker?.Id, complete.Message.Value);
                if (speaker is not null)
                    ExpectSpeech(complete.Character.Name, speaker.Id, complete.Message.Value);
                break;

            case SpeechStartActionScriptEvent speech:
                StartPendingPerformance(speech.Character.Name);
                break;

            case UserMessageReceivedActionScriptEvent userMessage:
                _userTyping = false;
                if (_settings.ShowUserMessagesInGame)
                    await ShowUserMessageAsync(userMessage.Message.Value, cancellationToken);
                break;

            case TypingStartScriptEvent:
                _userTyping = true;
                break;

            case ActionInferenceSelectedActionScriptEvent selected
                when selected.Action is ActionCatalog.InteractAction or ActionCatalog.ActivityAction:
                RunGameAction(selected.Character?.Name, selected.Action, selected.Arguments);
                break;

            case TypingEndScriptEvent:
                _userTyping = false;
                break;
        }
    }

    /// <summary>Shows a reply above the Zoi a bubble at a time, in the background so the chat isn't held up.</summary>
    private void ShowBubbles(long? zoiId, string? text)
    {
        if (zoiId is null || !_game.IsConnected) return;
        var bubbles = Bubbles.Split(text, _settings.HideActionsInBubbles);
        if (bubbles.Count == 0) return;

        var cts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);
        var previous = Interlocked.Exchange(ref _bubbles, cts);
        previous?.Cancel();

        _ = Task.Run(async () =>
        {
            try
            {
                foreach (var bubble in bubbles)
                {
                    if (!await _game.ShowSpeechBubbleAsync(zoiId.Value, bubble.Text, bubble.Seconds, cts.Token)) return;
                    await Task.Delay(TimeSpan.FromSeconds(bubble.Seconds), cts.Token);
                }
            }
            catch (OperationCanceledException)
            {
                // A newer reply took over, or the chat ended.
            }
            catch (Exception exc)
            {
                _logger.LogWarning(exc, "Could not show a reply in inZOI");
            }
        });
    }

    // ---- Game actions (kiss, cook, sleep...) ----

    /// <summary>
    /// Tells Voxta which in-game actions the characters can take. The household members are the
    /// choices for "who", so this is redone when the household changes.
    /// </summary>
    private void ScheduleActionRegistration(GameSnapshot state)
    {
        if (!_settings.AllowActions || !state.InGame || state.Zois.Length == 0) return;
        var targets = string.Join("|", state.Zois.Select(z => z.FirstName).OrderBy(n => n, StringComparer.OrdinalIgnoreCase));
        if (targets == _registeredActionTargets) return;
        _registeredActionTargets = targets;

        _ = Task.Run(async () =>
        {
            try
            {
                await _session.SetActions(InzoiConstants.ContextKey, GameActions.Definitions(state.Zois.Select(z => z.FirstName)), _cts.Token);
                _logger.LogInformation("In-game actions available to the characters (household: {Zois})", targets.Replace("|", ", "));
            }
            catch (OperationCanceledException)
            {
                // The chat ended.
            }
            catch (Exception exc)
            {
                _registeredActionTargets = null;
                _logger.LogWarning(exc, "Could not register the in-game actions");
            }
        });
    }

    private void RunGameAction(string? characterName, string action, Dictionary<string, object>? arguments)
    {
        if (!_settings.AllowActions || characterName is null) return;
        var state = _game.State;
        var actor = Match(characterName, state);
        if (state is null || actor is null || !_game.IsConnected)
        {
            _logger.LogInformation("'{Character}' chose {Action}, but has no Zoi in a running game", characterName, action);
            return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                var args = arguments?.ToDictionary(a => a.Key, a => (object?)a.Value, StringComparer.OrdinalIgnoreCase) ?? new Dictionary<string, object?>();
                var outcome = await _actions.RunAsync(actor, state, action, args, _matcher, _cts.Token);
                // The character should know whether it worked, so it doesn't describe a kiss that never happened next time.
                await _session.SendSecretAsync($"[In the game: {outcome}]", _cts.Token);
            }
            catch (OperationCanceledException)
            {
                // The chat ended.
            }
            catch (Exception exc)
            {
                _logger.LogWarning(exc, "Could not run the in-game action {Action}", action);
            }
        });
    }

    // ---- The Zoi performing the reply ----

    /// <summary>A reply that is ready and about to be spoken (or read, without text to speech).</summary>
    private sealed record PendingSpeech(string Character, long ZoiId, string? Text, DateTimeOffset At)
    {
        public int Started;
    }

    private PendingSpeech? _pendingSpeech;

    /// <summary>Without text to speech there's no speechStart; after this long, the reply being ready is the moment.</summary>
    private static readonly TimeSpan NoSpeechFallback = TimeSpan.FromSeconds(1.5);

    /// <summary>The performance running now: a newer reply cancels it.</summary>
    private CancellationTokenSource? _performance;

    private void ExpectSpeech(string character, long zoiId, string? text)
    {
        if (!_settings.SpeakingGestures && !_settings.SpeakingFaces && !_settings.SwitchToSpeaker) return;
        var pending = new PendingSpeech(character, zoiId, text, DateTimeOffset.UtcNow);
        _pendingSpeech = pending;
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(NoSpeechFallback, _cts.Token);
                if (ReferenceEquals(_pendingSpeech, pending)) StartPerformance(pending);
            }
            catch (OperationCanceledException)
            {
                // The chat ended.
            }
        });
    }

    private void StartPendingPerformance(string character)
    {
        var pending = _pendingSpeech;
        if (pending is null || !string.Equals(pending.Character, character, StringComparison.OrdinalIgnoreCase)) return;
        if (DateTimeOffset.UtcNow - pending.At > TimeSpan.FromMinutes(2)) return;
        StartPerformance(pending);
    }

    private void StartPerformance(PendingSpeech pending)
    {
        // Speech start and the no-speech fallback can both arrive; the first one runs it.
        if (Interlocked.Exchange(ref pending.Started, 1) == 1) return;
        if (!_game.IsConnected) return;

        var cts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);
        Interlocked.Exchange(ref _performance, cts)?.Cancel();
        _ = Task.Run(() => PerformAsync(pending, cts.Token));
    }

    /// <summary>
    /// Makes the Zoi look like the one talking for about as long as the reply takes to say: the camera
    /// (by taking control of them, if wanted), a face that fits what they say, and inZOI's own
    /// conversation gestures, one after another.
    /// </summary>
    private async Task PerformAsync(PendingSpeech speech, CancellationToken cancellationToken)
    {
        string? lastAnimation = null;
        var faceChanged = false;
        try
        {
            var zoi = _game.State?.Zois.FirstOrDefault(z => z.Id == speech.ZoiId);
            if (zoi is null) return;

            if (_settings.SwitchToSpeaker && _game.State?.PossessedId != zoi.Id)
                await _game.PossessAsync(zoi.Id, cancellationToken);

            var replyFace = _settings.SpeakingFaces ? Performance.FaceFor(speech.Text) : null;
            var face = replyFace ?? (_settings.SpeakingFaces ? await _game.FaceForEmotionAsync(zoi.Emotion, cancellationToken) : null);
            if (face is not null)
                faceChanged = await _game.SetFaceAsync(zoi.Id, face, cancellationToken);

            var until = DateTimeOffset.UtcNow + Performance.SpeakingTime(speech.Text);
            if (!_settings.SpeakingGestures)
            {
                var rest = until - DateTimeOffset.UtcNow;
                if (rest > TimeSpan.Zero) await Task.Delay(rest, cancellationToken);
                return;
            }

            var animations = Performance.TalkAnimations(Performance.Tone(replyFace, zoi.Emotion), Performance.LooksSeated(zoi.Activity));
            var next = Random.Shared.Next(animations.Count);
            while (DateTimeOffset.UtcNow < until)
            {
                var animation = animations[next++ % animations.Count];
                if (!await _game.PlayMotionAsync(zoi.Id, animation, cancellationToken)) return;
                lastAnimation = animation;

                var length = await _game.GetMotionLengthAsync(zoi.Id, animation, cancellationToken) ?? 4;
                var left = until - DateTimeOffset.UtcNow;
                // Let a gesture finish when most of it would still be cut off; it looks better than freezing mid-way.
                var wait = TimeSpan.FromSeconds(Math.Max(1, Math.Min(length, Math.Max(left.TotalSeconds, length * 0.6))));
                await Task.Delay(wait, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            // A newer reply took over (it sets its own face and gestures), or the chat ended.
            return;
        }
        catch (Exception exc)
        {
            _logger.LogWarning(exc, "Could not animate the speaking Zoi in inZOI");
        }

        // Back to what the Zoi was doing, with the face their emotion gives them.
        try
        {
            if (lastAnimation is not null)
                await _game.StopMotionAsync(speech.ZoiId, lastAnimation, CancellationToken.None);
            if (faceChanged)
            {
                var zoi = _game.State?.Zois.FirstOrDefault(z => z.Id == speech.ZoiId);
                if (await _game.FaceForEmotionAsync(zoi?.Emotion, CancellationToken.None) is { } restFace)
                    await _game.SetFaceAsync(speech.ZoiId, restFace, CancellationToken.None);
            }
        }
        catch (Exception exc)
        {
            _logger.LogDebug(exc, "Could not reset the Zoi after speaking");
        }
    }

    private async Task ShowUserMessageAsync(string? text, CancellationToken cancellationToken)
    {
        if (!_game.IsConnected || string.IsNullOrWhiteSpace(text)) return;
        if (_settings.UserRole == Choices.UserIsPossessedZoi && _game.State?.PossessedId is { } possessed)
        {
            ShowBubbles(possessed, text);
            return;
        }

        var first = Bubbles.Split(text, _settings.HideActionsInBubbles).FirstOrDefault();
        if (first is not null)
            await _game.ShowToastAsync($"{_session.User.Name}: {first.Text}", first.Seconds, cancellationToken);
    }

    // ---- Helpers ----

    private ZoiInfo? Match(string characterName, GameSnapshot? state) =>
        state is null ? null : _matcher.Match(characterName, state.Zois);

    /// <summary>Zoi id → character name, for the characters seen in this chat that have a Zoi.</summary>
    private Dictionary<long, string> Participants(GameSnapshot? state)
    {
        var result = new Dictionary<long, string>();
        if (state is null) return result;
        foreach (var name in _characters.Keys)
        {
            var zoi = Match(name, state);
            if (zoi is not null) result.TryAdd(zoi.Id, name);
        }

        return result;
    }

    public async ValueTask DisposeAsync()
    {
        _game.StateChanged -= OnStateChanged;
        _game.ConnectionChanged -= OnConnectionChanged;
        _game.EventReceived -= OnGameEvent;
        if (_lease is not null) await _lease.DisposeAsync();
        // Cancelled, not disposed: a debounce, event or bubble task may still be about to read the token.
        await _cts.CancelAsync();
    }
}
