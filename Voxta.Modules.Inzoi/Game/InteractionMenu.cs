using Voxta.Modules.Inzoi.Household;

namespace Voxta.Modules.Inzoi.Game;

/// <summary>One entry of the game's own menu, as interaction.list_available reports it.</summary>
public sealed record Offer(string BagId, string DisplayName, bool Enabled, string? Hint = null);

/// <summary>
/// Everything a Zoi could click right now: every object at home, and the other Zoi if there is one.
/// Read from the game, not from a list of our own, so whatever inZOI offers (and only that) can be
/// done. Menus are cached briefly per Zoi and kind of object; two identical sofas offer the same.
/// </summary>
public sealed class InteractionMenu(InzoiGame game)
{
    private static readonly TimeSpan CacheTime = TimeSpan.FromSeconds(60);

    /// <summary>Things that are only there to look at: asking them for a menu costs time and finds nothing.</summary>
    private static readonly string[] Decor =
    [
        "Landscape", "Lighting", "Window", "Door", "Wall", "Tile", "Roof", "GableRoof", "HalfGableRoof", "Fence", "Blind",
        "Frame", "BuiltIn", "Rug", "FireAlarm", "FireExtinguisher", "Deck", "Stair", "Railing", "Curtain", "ToiletRollHolder",
        "TowelRack", "Clock", "StandingMobile", "UmbrellaStand", "Slipper", "KnifeBlock", "DecoWallShelf", "WallShelf", "Hanger",
        "DoorBell", "MailBox", "Washstand02", "U_Sink", "TVTable", "LowTable", "DrawerTable", "Art_",
    ];

    private readonly Lock _gate = new();
    private readonly Dictionary<(long Actor, string Table), (DateTimeOffset At, IReadOnlyList<Offer> Offers)> _cache = new();

    public static bool IsDecor(string tableId) =>
        Decor.Any(d => tableId.StartsWith(d, StringComparison.OrdinalIgnoreCase) || tableId.StartsWith("D_" + d, StringComparison.OrdinalIgnoreCase));

    public async Task<List<MenuMatcher.Entry>> BuildAsync(ZoiInfo actor, ZoiInfo? with, CancellationToken cancellationToken)
    {
        var entries = new List<MenuMatcher.Entry>();

        if (with is not null)
        {
            foreach (var offer in await game.ListInteractionsAsync(actor.Id, with.Id, cancellationToken))
                entries.Add(new MenuMatcher.Entry(offer, with.Id, with.FirstName, Social: true));
        }

        var objects = await game.GetObjectsAsync(cancellationToken);
        foreach (var kind in objects.Where(o => !IsDecor(o.TableId)).GroupBy(o => o.TableId))
        {
            var obj = kind.First();
            foreach (var offer in await OffersAsync(actor.Id, obj, cancellationToken))
                entries.Add(new MenuMatcher.Entry(offer, obj.Id, MenuMatcher.ObjectWords(obj.TableId), Social: false));
        }

        return entries;
    }

    private async Task<IReadOnlyList<Offer>> OffersAsync(long actorId, InzoiGame.GameObject obj, CancellationToken cancellationToken)
    {
        var key = (actorId, obj.TableId);
        lock (_gate)
        {
            if (_cache.TryGetValue(key, out var cached) && DateTimeOffset.UtcNow - cached.At < CacheTime) return cached.Offers;
        }

        var offers = await game.ListInteractionsAsync(actorId, obj.Id, cancellationToken);
        lock (_gate) _cache[key] = (DateTimeOffset.UtcNow, offers);
        return offers;
    }

    /// <summary>After doing something, the menus change (the dishes are done): read them fresh next time.</summary>
    public void Forget()
    {
        lock (_gate) _cache.Clear();
    }
}
