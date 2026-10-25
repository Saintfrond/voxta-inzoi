using Voxta.Modules.Inzoi.Household;

namespace Voxta.Modules.Inzoi.Game;

/// <summary>
/// inZOI's MCP server can't push events, so they are found by comparing one snapshot with the next:
/// a new emotion, a need that just ran low, a new wish, a new job, a wedding, someone moving in or out.
/// </summary>
public static class EventDetector
{
    public static List<GameEvent> Compare(GameSnapshot? before, GameSnapshot after)
    {
        var events = new List<GameEvent>();
        // A different household (the player switched, or loaded another save) is not news about anyone.
        if (before is null || !before.InGame || !after.InGame || before.HouseholdId != after.HouseholdId)
            return events;

        foreach (var zoi in after.Zois)
        {
            var old = before.Zois.FirstOrDefault(z => z.Id == zoi.Id);
            if (old is null)
            {
                events.Add(new GameEvent { ZoiId = zoi.Id, Kind = "joined_household", Importance = 2, Text = $"{zoi.FullName} joined the household." });
                continue;
            }

            Emotion(events, old, zoi);
            Needs(events, old, zoi);

            foreach (var hope in zoi.Hopes.Except(old.Hopes))
                events.Add(new GameEvent { ZoiId = zoi.Id, Kind = "new_wish", Importance = 0, Text = $"{zoi.FullName} now wishes to {Phrases.Hope(hope)}." });

            if (zoi.JobName != old.JobName || zoi.JobRank != old.JobRank)
            {
                var text = string.IsNullOrWhiteSpace(zoi.JobName)
                    ? $"{zoi.FullName} no longer has a job (was {Phrases.Work(old with { Work = null })})."
                    : string.IsNullOrWhiteSpace(old.JobName)
                        ? $"{zoi.FullName} got a job: {Phrases.Work(zoi with { Work = null })}."
                        : zoi.JobName == old.JobName
                            ? $"{zoi.FullName} is now {zoi.JobRank} (was {old.JobRank})."
                            : $"{zoi.FullName} changed jobs: now {Phrases.Work(zoi with { Work = null })}.";
                events.Add(new GameEvent { ZoiId = zoi.Id, Kind = "job_changed", Importance = 1, Text = text });
            }

            if (zoi.IsMarried && !old.IsMarried)
            {
                var spouse = after.Zois.FirstOrDefault(z => z.Id == zoi.SpouseId)?.FullName
                             ?? zoi.Relationships.FirstOrDefault(r => r.Id == zoi.SpouseId)?.Name;
                events.Add(new GameEvent { ZoiId = zoi.Id, Kind = "married", Importance = 2, Text = spouse is null ? $"{zoi.FullName} got married." : $"{zoi.FullName} married {spouse}." });
            }
            else if (!zoi.IsMarried && old.IsMarried)
            {
                events.Add(new GameEvent { ZoiId = zoi.Id, Kind = "divorced", Importance = 2, Text = $"{zoi.FullName} is no longer married." });
            }
        }

        foreach (var old in before.Zois.Where(o => after.Zois.All(z => z.Id != o.Id)))
            events.Add(new GameEvent { ZoiId = old.Id, Kind = "left_household", Importance = 2, Text = $"{old.FullName} is no longer part of the household." });

        return events;
    }

    private static void Emotion(List<GameEvent> events, ZoiInfo old, ZoiInfo zoi)
    {
        if (zoi.Emotion is not { } now) return;
        var was = old.Emotion;
        if (was is not null && was.Type == now.Type && was.Level >= now.Level) return;
        // Waking up and falling asleep are visible enough without an event.
        if (now.Type is "Sleep" or "Normal") return;

        var importance = now.Level >= 3 ? 2 : now.Level >= 2 ? 1 : 0;
        var text = was is null || was.Type is "Normal"
            ? $"{zoi.FullName} is now {Phrases.Emotion(now)}."
            : $"{zoi.FullName} is now {Phrases.Emotion(now)} (was {Phrases.Emotion(was)}).";
        events.Add(new GameEvent { ZoiId = zoi.Id, Kind = "emotion_changed", Importance = importance, Text = text });
    }

    private static void Needs(List<GameEvent> events, ZoiInfo old, ZoiInfo zoi)
    {
        foreach (var (need, value) in zoi.Needs)
        {
            if (!Phrases.IsEventNeed(need)) continue;
            var level = Phrases.NeedLevel(value);
            var oldLevel = old.Needs.TryGetValue(need, out var v) ? Phrases.NeedLevel(v) : 0;
            if (level < 2 || level <= oldLevel) continue;
            events.Add(new GameEvent
            {
                ZoiId = zoi.Id,
                Kind = "need_critical",
                Importance = level >= 3 ? 2 : 1,
                Text = level >= 3
                    ? $"{zoi.FullName} desperately needs {Phrases.NeedObject(need)}."
                    : $"{zoi.FullName} badly needs {Phrases.NeedObject(need)}.",
            });
        }
    }
}
