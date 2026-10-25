using System.Globalization;
using System.Text.Json.Nodes;

namespace Voxta.Modules.Inzoi.Game;

/// <summary>Calls one inZOI tool (MCP name, e.g. "household_get_members") and returns its data.</summary>
public delegate Task<JsonNode?> ToolCaller(string tool, object? arguments, CancellationToken cancellationToken);

/// <summary>
/// Reads a household snapshot from the game, one tool call per detail. A detail that fails (a tool
/// renamed by a patch, a Zoi that just left) is left out rather than failing the snapshot; only the
/// game being unreachable fails the whole read.
///
/// Quick details (emotion, needs, activity) are read every time. Slow ones (job status, wishes,
/// relationships) only when asked, and carried over from the previous snapshot otherwise.
/// </summary>
public sealed class GameReader(ToolCaller call)
{
    private Dictionary<string, (string Tendency, Dictionary<int, string> Labels)>? _emotions;

    /// <summary>Tool failures since the last read, for the log. Cleared by each read.</summary>
    public List<string> Problems { get; } = [];

    public async Task<GameSnapshot> ReadAsync(GameSnapshot? previous, bool includeSlowDetails, bool includeRelationships, CancellationToken cancellationToken)
    {
        Problems.Clear();

        // Unreachable game → exception, on purpose: the caller treats that as "disconnected".
        var state = await call("game_get_state", null, cancellationToken);
        var phase = Str(state, "phase") ?? "unknown";
        var snapshot = new GameSnapshot
        {
            Phase = phase,
            PossessedId = Long(state, "possessed_entity_id") is > 0 and var p ? p : null,
            World = new WorldInfo { City = CityName(Str(state, "map_id")) },
        };
        if (!snapshot.InGame) return snapshot;

        await EnsureEmotionCatalogAsync(cancellationToken);

        var environment = await TryAsync("environment_get_state", null, cancellationToken);
        var simulation = includeSlowDetails ? await TryAsync("environment_get_simulation", null, cancellationToken) : null;
        snapshot = snapshot with
        {
            World = snapshot.World! with
            {
                Hour = Int(environment, "hour"),
                Minute = Int(environment, "minute"),
                DayOfWeek = Str(environment, "day_of_week"),
                Season = Str(environment, "season"),
                Weather = Str(environment, "weather"),
                Temperature = Int(environment, "temperature"),
                IsNight = Bool(environment, "is_night_time"),
                CalendarEvent = Str(environment, "calendar_event") is { Length: > 0 } e ? e : null,
                Paused = simulation is null ? previous?.World?.Paused : Bool(simulation, "is_paused"),
            },
        };

        var household = await TryAsync("household_get_members", null, cancellationToken);
        var members = Get(household, "members") as JsonArray ?? [];
        var zois = new List<ZoiInfo>();
        foreach (var member in members)
        {
            var id = Long(member, "entity_id");
            if (id is null) continue;
            var before = previous?.Zois.FirstOrDefault(z => z.Id == id);
            zois.Add(await ReadZoiAsync(member!, id.Value, snapshot.PossessedId, before, includeSlowDetails, includeRelationships, cancellationToken));
        }

        return snapshot with
        {
            HouseholdId = Long(household, "household_id"),
            HouseholdName = Str(household, "name"),
            Zois = zois.ToArray(),
        };
    }

    private async Task<ZoiInfo> ReadZoiAsync(JsonNode member, long id, long? possessedId, ZoiInfo? before, bool slow, bool relationships, CancellationToken cancellationToken)
    {
        var who = new { entity_id = id.ToString(CultureInfo.InvariantCulture) };

        var emotion = await TryAsync("emotion_get_current", who, cancellationToken);
        var stats = await TryAsync("stat_get_all", who, cancellationToken);
        var interaction = await TryAsync("interaction_get_current", who, cancellationToken);

        var zoi = new ZoiInfo
        {
            Id = id,
            FirstName = Str(member, "first_name") ?? "",
            LastName = Str(member, "last_name") ?? "",
            Gender = Str(member, "gender_text") ?? Str(member, "gender"),
            AgeGroup = Str(member, "age_group_text") ?? Str(member, "age_group"),
            Site = Str(member, "current_site"),
            IsMarried = Bool(member, "is_married") ?? false,
            SpouseId = Long(member, "spouse_id") is > 0 and var s ? s : null,
            JobName = Bool(member, "has_career") == false ? null : Str(member, "job_name"),
            JobRank = Bool(member, "has_career") == false ? null : Str(member, "job_rank_name"),
            IsPossessed = id == possessedId,
            Emotion = ParseEmotion(emotion) ?? before?.Emotion,
            Needs = ParseNeeds(stats) ?? before?.Needs ?? new(StringComparer.OrdinalIgnoreCase),
            Activity = ParseActivity(interaction),
            Work = before?.Work,
            Hopes = before?.Hopes ?? [],
            Relationships = before?.Relationships ?? [],
        };

        if (slow)
        {
            var work = await TryAsync("career_get_work_status", who, cancellationToken);
            var hopes = await TryAsync("hope_list", who, cancellationToken);
            zoi = zoi with
            {
                Work = work is null ? zoi.Work : new WorkInfo
                {
                    HasJob = Bool(work, "has_job") ?? false,
                    IsWorkingTime = Bool(work, "is_working_time") ?? false,
                    IsCommuteTime = Bool(work, "is_commute_time") ?? false,
                    IsAtWork = Bool(work, "is_at_work") ?? false,
                },
                Hopes = hopes is null
                    ? zoi.Hopes
                    : (Get(hopes, "selected") as JsonArray ?? [])
                        .Select(h => Str(h, "hope_id"))
                        .Where(h => !string.IsNullOrWhiteSpace(h))
                        .Select(h => h!)
                        .ToArray(),
            };

            if (relationships)
            {
                var list = await TryAsync("relationships_list", new { owner_id = who.entity_id, min_value = 10 }, cancellationToken);
                if (list is not null)
                    zoi = zoi with { Relationships = ParseRelationships(list) };
            }
        }

        return zoi;
    }

    private EmotionInfo? ParseEmotion(JsonNode? node)
    {
        var type = Str(node, "emotion_type");
        if (string.IsNullOrWhiteSpace(type)) return null;
        var level = Int(node, "level") ?? 1;
        string? label = null;
        string? tendency = null;
        if (_emotions is not null && _emotions.TryGetValue(type, out var entry))
        {
            tendency = entry.Tendency;
            label = entry.Labels.TryGetValue(level, out var l) ? l : entry.Labels.Values.LastOrDefault();
        }

        return new EmotionInfo { Type = type, Level = level, Label = label, Tendency = tendency };
    }

    internal static Dictionary<string, double>? ParseNeeds(JsonNode? node)
    {
        if (Get(node, "stats") is not JsonObject stats) return null;
        var needs = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, value) in stats)
        {
            var pct = Double(value, "pct");
            if (pct is null && Double(value, "value") is { } v)
            {
                var min = Double(value, "min") ?? -100;
                var max = Double(value, "max") ?? 100;
                pct = max > min ? (v - min) / (max - min) * 100 : null;
            }

            if (pct is { } p) needs[name] = Math.Clamp(p, 0, 100);
        }

        return needs;
    }

    internal static ActivityInfo? ParseActivity(JsonNode? node)
    {
        if (Bool(node, "playing") == false) return null;
        var id = Str(node, "interaction_bag_id");
        if (string.IsNullOrWhiteSpace(id) || id == "None") return null;
        return new ActivityInfo
        {
            InteractionId = id,
            ActionType = Str(node, "action_type"),
            TargetId = Long(node, "target_entity_id"),
        };
    }

    internal static RelationshipInfo[] ParseRelationships(JsonNode node)
    {
        var result = new List<RelationshipInfo>();
        foreach (var r in Get(node, "relationships") as JsonArray ?? [])
        {
            var id = Long(r, "target_id");
            if (id is null) continue;
            var stats = Get(r, "stats");
            var first = Str(r, "first_name") ?? "";
            var last = Str(r, "last_name") ?? "";
            result.Add(new RelationshipInfo
            {
                Id = id.Value,
                Name = string.IsNullOrWhiteSpace(last) ? first : $"{first} {last}",
                IsFamily = Bool(r, "is_family") ?? false,
                Friendship = Double(Get(stats, "friendship"), "value") ?? 0,
                FriendshipLevel = Int(Get(stats, "friendship"), "level") ?? 0,
                Romance = Double(Get(stats, "romance"), "value") ?? 0,
                RomanceLevel = Int(Get(stats, "romance"), "level") ?? 0,
            });
        }

        return result.ToArray();
    }

    private async Task EnsureEmotionCatalogAsync(CancellationToken cancellationToken)
    {
        if (_emotions is not null) return;
        var list = await TryAsync("emotion_list_all", null, cancellationToken);
        if (Get(list, "emotions") is not JsonArray emotions) return;

        var catalog = new Dictionary<string, (string, Dictionary<int, string>)>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in emotions)
        {
            var id = Str(e, "id");
            if (string.IsNullOrWhiteSpace(id)) continue;
            var labels = new Dictionary<int, string>();
            foreach (var level in Get(e, "levels") as JsonArray ?? [])
            {
                if (Int(level, "level") is { } n && Str(level, "l10n") is { Length: > 0 } text)
                    labels[n] = text;
            }

            catalog[id] = (Str(e, "tendency") ?? "neutral", labels);
        }

        _emotions = catalog;
    }

    private async Task<JsonNode?> TryAsync(string tool, object? arguments, CancellationToken cancellationToken)
    {
        try
        {
            return await call(tool, arguments, cancellationToken);
        }
        catch (Mcp.McpToolException exc)
        {
            Problems.Add(exc.Message);
            return null;
        }
    }

    /// <summary>"Gangnam_Map" → "Gangnam".</summary>
    internal static string? CityName(string? mapId)
    {
        if (string.IsNullOrWhiteSpace(mapId)) return null;
        var name = mapId.EndsWith("_Map", StringComparison.OrdinalIgnoreCase) ? mapId[..^4] : mapId;
        return name.Replace('_', ' ');
    }

    // ---- Tolerant JSON access: case-insensitive keys, numbers that may arrive as strings ----

    internal static JsonNode? Get(JsonNode? node, string key)
    {
        if (node is not JsonObject obj) return null;
        if (obj.TryGetPropertyValue(key, out var value)) return value;
        foreach (var (k, v) in obj)
        {
            if (string.Equals(k, key, StringComparison.OrdinalIgnoreCase)) return v;
        }

        return null;
    }

    internal static string? Str(JsonNode? node, string key) => Get(node, key) switch
    {
        JsonValue v when v.TryGetValue<string>(out var s) => s,
        JsonValue v => v.ToString(),
        _ => null,
    };

    internal static double? Double(JsonNode? node, string key) => Get(node, key) switch
    {
        JsonValue v when v.TryGetValue<double>(out var d) => d,
        JsonValue v when v.TryGetValue<string>(out var s) && double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) => d,
        _ => null,
    };

    internal static long? Long(JsonNode? node, string key) => Double(node, key) is { } d ? (long)d : null;

    internal static int? Int(JsonNode? node, string key) => Double(node, key) is { } d ? (int)d : null;

    internal static bool? Bool(JsonNode? node, string key) => Get(node, key) switch
    {
        JsonValue v when v.TryGetValue<bool>(out var b) => b,
        JsonValue v when v.TryGetValue<string>(out var s) && bool.TryParse(s, out var b) => b,
        _ => null,
    };
}
