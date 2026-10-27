namespace Voxta.Modules.Inzoi.Game;

/// <summary>Something that happened in the game, kept so the characters know what led up to the moment.</summary>
public sealed record Happening(DateTimeOffset At, string? GameTime, long[] ZoiIds, string Text);

/// <summary>
/// The household's recent history: social interactions between Zois (who did what to whom), feelings
/// changing, relationships taking a hit, jobs, weddings. A Zoi's mood alone ("sad") says little; the
/// argument right before it is what a character needs to know to react like a person would.
/// </summary>
public sealed class HappeningsLog
{
    public const int MaxEntries = 40;

    /// <summary>The same interaction seen again within this time is the same interaction, not a new one.</summary>
    private static readonly TimeSpan SameInteractionWindow = TimeSpan.FromSeconds(90);

    private readonly Lock _gate = new();
    private readonly List<Happening> _entries = [];
    private readonly Dictionary<(long, long, string), DateTimeOffset> _seenSocial = new();
    private long? _householdId;

    public IReadOnlyList<Happening> Recent(TimeSpan maxAge, int max)
    {
        var since = DateTimeOffset.UtcNow - maxAge;
        lock (_gate)
            return _entries.Where(e => e.At >= since).TakeLast(max).ToArray();
    }

    public void Add(Happening happening)
    {
        lock (_gate)
        {
            _entries.Add(happening);
            if (_entries.Count > MaxEntries) _entries.RemoveRange(0, _entries.Count - MaxEntries);
        }
    }

    /// <summary>Forgets everything when the player switches to another household.</summary>
    public void SetHousehold(long? householdId)
    {
        lock (_gate)
        {
            if (householdId == _householdId) return;
            _householdId = householdId;
            _entries.Clear();
            _seenSocial.Clear();
        }
    }

    /// <summary>
    /// True the first time this pair is seen doing this interaction (in either direction: the one
    /// being talked to often shows the same interaction aimed back), false while it's still going.
    /// </summary>
    public bool IsNewSocial(long actor, long target, string interactionId)
    {
        var key = actor < target ? (actor, target, interactionId) : (target, actor, interactionId);
        var now = DateTimeOffset.UtcNow;
        lock (_gate)
        {
            var isNew = !_seenSocial.TryGetValue(key, out var last) || now - last > SameInteractionWindow;
            _seenSocial[key] = now;
            if (_seenSocial.Count > 500)
            {
                foreach (var old in _seenSocial.Where(p => now - p.Value > SameInteractionWindow).Select(p => p.Key).ToList())
                    _seenSocial.Remove(old);
            }

            return isNew;
        }
    }

    /// <summary>
    /// Social interactions running right now between a household Zoi and another character: the
    /// actor, the target and the game's interaction id. Objects (a sofa, the fridge) aren't social.
    /// </summary>
    public static IEnumerable<(ZoiInfo Actor, long TargetId, string? TargetName, string InteractionId)> SocialNow(GameSnapshot snapshot)
    {
        foreach (var zoi in snapshot.Zois)
        {
            if (zoi.Activity is not { TargetId: { } target } activity || target == zoi.Id) continue;
            var housemate = snapshot.Zois.FirstOrDefault(z => z.Id == target);
            var known = zoi.Relationships.FirstOrDefault(r => r.Id == target);
            if (housemate is null && known is null) continue;
            yield return (zoi, target, housemate?.FullName ?? known!.Name, activity.InteractionId);
        }
    }
}
