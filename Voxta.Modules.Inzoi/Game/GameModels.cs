namespace Voxta.Modules.Inzoi.Game;

// What this module knows about the running game, read from inZOI's MCP server. Everything is
// optional: a game patch that drops one field should cost one detail, not the whole picture.

public sealed record GameSnapshot
{
    /// <summary>inZOI's own phase: lobby, loading, in_game, map_view, ... Only in_game has a household.</summary>
    public string Phase { get; init; } = "unknown";

    public bool InGame => Phase is "in_game" or "map_view";

    public long? HouseholdId { get; init; }
    public string? HouseholdName { get; init; }

    /// <summary>The Zoi the player is controlling right now.</summary>
    public long? PossessedId { get; init; }

    public WorldInfo? World { get; init; }
    public ZoiInfo[] Zois { get; init; } = [];
    public DateTimeOffset ReadAt { get; init; } = DateTimeOffset.UtcNow;

    public ZoiInfo? Possessed => Zois.FirstOrDefault(z => z.Id == PossessedId);
}

public sealed record WorldInfo
{
    public string? City { get; init; }
    public int? Hour { get; init; }
    public int? Minute { get; init; }
    public string? DayOfWeek { get; init; }
    public string? Season { get; init; }
    public string? Weather { get; init; }
    public int? Temperature { get; init; }
    public bool? IsNight { get; init; }
    public string? CalendarEvent { get; init; }
    public bool? Paused { get; init; }

    public string? Time => Hour is { } h && Minute is { } m ? $"{h:00}:{m:00}" : null;
}

public sealed record ZoiInfo
{
    public required long Id { get; init; }
    public string FirstName { get; init; } = "";
    public string LastName { get; init; } = "";
    public string? Gender { get; init; }

    /// <summary>Readable age group, e.g. "Young Adult".</summary>
    public string? AgeGroup { get; init; }

    /// <summary>Where the Zoi is, e.g. "Junghwa Parkville".</summary>
    public string? Site { get; init; }

    public bool IsMarried { get; init; }
    public long? SpouseId { get; init; }
    public string? JobName { get; init; }
    public string? JobRank { get; init; }
    public bool IsPossessed { get; init; }

    public EmotionInfo? Emotion { get; init; }

    /// <summary>Need id (Hunger, Sleep, ...) → 0..100, where 0 is empty and 100 is full.</summary>
    public Dictionary<string, double> Needs { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    public ActivityInfo? Activity { get; init; }
    public WorkInfo? Work { get; init; }

    /// <summary>inZOI hope ids the Zoi currently wishes for, e.g. "Trait_Romantic_ExpressAffection".</summary>
    public string[] Hopes { get; init; } = [];

    public RelationshipInfo[] Relationships { get; init; } = [];

    public string FullName => string.IsNullOrWhiteSpace(LastName) ? FirstName : $"{FirstName} {LastName}";
}

public sealed record EmotionInfo
{
    public required string Type { get; init; }
    public int Level { get; init; } = 1;

    /// <summary>The game's word for this emotion at this level, e.g. "Lovestruck" for Enchanting 2.</summary>
    public string? Label { get; init; }

    /// <summary>positive, negative or neutral.</summary>
    public string? Tendency { get; init; }
}

public sealed record ActivityInfo
{
    /// <summary>The game's interaction id, e.g. "Object_Dumbbell01".</summary>
    public required string InteractionId { get; init; }

    /// <summary>e.g. "Exercise", "PlayLoopAnim".</summary>
    public string? ActionType { get; init; }

    public long? TargetId { get; init; }
}

public sealed record WorkInfo
{
    public bool HasJob { get; init; }
    public bool IsWorkingTime { get; init; }
    public bool IsCommuteTime { get; init; }
    public bool IsAtWork { get; init; }
}

public sealed record RelationshipInfo
{
    public required long Id { get; init; }
    public string Name { get; init; } = "";
    public bool IsFamily { get; init; }

    /// <summary>-100..100</summary>
    public double Friendship { get; init; }

    public int FriendshipLevel { get; init; }

    /// <summary>-100..100</summary>
    public double Romance { get; init; }

    public int RomanceLevel { get; init; }
}

public sealed record GameEvent
{
    public long? ZoiId { get; init; }

    /// <summary>emotion_changed, need_critical, new_wish, job_changed, married, joined_household, left_household</summary>
    public string Kind { get; init; } = "";

    /// <summary>Already worded for a reader, e.g. "Juve Lez is now Angry (was Content)."</summary>
    public string Text { get; init; } = "";

    /// <summary>0 trivia, 1 notable, 2 important.</summary>
    public int Importance { get; init; }
}
