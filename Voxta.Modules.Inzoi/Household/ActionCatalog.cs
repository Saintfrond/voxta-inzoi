namespace Voxta.Modules.Inzoi.Household;

/// <summary>
/// The things a character can make their Zoi do in the game, and how to find the matching inZOI
/// interaction. inZOI's own lists are long (about 90 things to do with a spouse alone) and change
/// with relationship and mood, so the characters get a short list of plain choices, and each choice
/// is matched against what the game offers right now by the interaction's own display name.
/// </summary>
public static class ActionCatalog
{
    public const string InteractAction = "inzoi_interact";
    public const string ActivityAction = "inzoi_activity";

    /// <summary>A choice the character sees, and the words to look for in inZOI's interaction names, best first.</summary>
    public sealed record Social(string Choice, string[] Names);

    /// <summary>A choice, the objects it can happen at (by table id), and the interaction names to look for there.</summary>
    /// <param name="MaxObjects">How many kinds of object to ask about; repairs look through many appliances for the broken one.</param>
    /// <param name="SelfFallback">An interaction the Zoi can do on their own when no object offers it (mopping with a flat mop).</param>
    public sealed record Activity(
        string Choice,
        string[] Objects,
        string[] Names,
        string[]? AvoidObjects = null,
        string[]? AvoidNames = null,
        int MaxObjects = 5,
        string? SelfFallback = null);

    public static readonly Social[] SocialChoices =
    [
        new("kiss", ["Kiss", "Peck", "Scoop Up and Kiss", "Sit and Kiss", "Kiss at the Table"]),
        new("hug", ["Give Heartfelt Hug", "Hug and Spin", "Hug"]),
        new("hold hands", ["Hold Hands and Talk", "Hold Hands"]),
        new("dance together", ["Dance Together", "Dance as Couple", "Share a Dance in Each Other's Arms", "Dance"]),
        new("show affection", ["Show Affection", "Power of Love"]),
        new("compliment", ["Compliment", "Praise Their Taste", "Praise"]),
        new("tell a joke", ["Tell Light Joke", "Share Inside Joke", "Make a Mood-Lifting Joke", "Joke"]),
        new("tease", ["Tease"]),
        new("cheer up", ["Cheer On", "You Can Do It", "Make a Mood-Lifting Joke"]),
        new("high five", ["High Five"]),
        new("ask about their day", ["Ask About Day", "Talk About Life"]),
        new("talk about memories", ["Recall First Meeting", "Talk About Memories"]),
        new("gossip", ["Gossip"]),
        new("ask on a date", ["Ask on a Date", "Suggest Spending Time Together"]),
        new("take a selfie together", ["Take a Selfie Together", "Take a Photo for Them"]),
        new("serenade", ["Serenade"]),
        new("give a massage", ["Give Massage"]),
        new("cuddle on the sofa", ["Put Arm Around Shoulder", "Offer Lap to Rest Head", "Sit Together"]),
        new("play games together", ["Play Games Together", "Play Chess"]),
    ];

    /// <summary>Social choices that happen at an object (sofa, TV, chess table) rather than face to face.</summary>
    public static readonly Dictionary<string, string[]> SocialAtObjects = new(StringComparer.OrdinalIgnoreCase)
    {
        ["give a massage"] = ["Sofa"],
        ["cuddle on the sofa"] = ["Sofa"],
        ["play games together"] = ["TV", "Chess", "Computer"],
    };

    public static readonly Activity[] ActivityChoices =
    [
        new("cook", ["KitchenRange", "Stove", "Oven", "Fridge"], ["Cook"]),
        new("eat", ["Fridge", "Microwave"], ["Eat"]),
        new("drink water", ["Fridge", "WaterPurifier", "SinkBowl", "Sink"], ["Drink Water", "Drink"]),
        new("sleep", ["Bed"], ["Sleep"], AvoidObjects: ["KidBed"], AvoidNames: ["Keep Sleeping"]),
        new("take a nap", ["Sofa", "Bed"], ["Sit and Doze", "Doze", "Nap"]),
        new("take a bath", ["Bath", "Shower"], ["Take a Bath", "Take a Shower", "Shower", "Bath"], AvoidNames: ["Clean"]),
        new("watch TV", ["TV"], ["Watch TV Drama", "Watch Talk Show", "Watch Reality Show", "Watch"]),
        new("play video games", ["TV", "Computer"], ["Play Games", "Play Game", "Game"], AvoidNames: ["Together"]),
        new("use the computer", ["Computer"], ["Browse", "Surf", "Use Computer", "Use"]),
        new("do yoga", ["YogaMat"], ["Do Yoga Warm-Up", "Do Warrior Pose", "Yoga"]),
        new("work out", ["Dumbbell", "Treadmill", "YogaMat", "Bench"], ["Lift", "Exercise", "Work Out", "Push-ups", "Squats"]),
        new("meditate", ["YogaMat"], ["Meditate"]),
        new("play chess", ["Chess"], ["Practice Chess", "Play Chess"]),
        new("relax on the sofa", ["Sofa"], ["Sit and Relax", "Sit Down"], AvoidNames: ["Together"]),
        new("lie down on the sofa", ["Sofa"], ["Lie Down"]),
        new("listen to music", ["Audio", "Speaker", "Radio", "Stereo"], ["Listen", "Music", "Turn On", "Play"]),
        new("brush teeth", ["SinkBowl", "Washstand", "Sink"], ["Brush Teeth"]),
        new("go to the toilet", ["Toilet"], ["Use"], AvoidNames: ["Flush"]),

        // Chores. The game only offers most of them when there is something to do (dirty dishes, a full bin, a broken appliance).
        new("clean the floor", ["Mop", "Vaccum", "Vacuum", "DEV_DustDecal", "Dust", "Dirt"], ["Use (Clean", "Use Vacuum Cleaner", "Use Flat Mop", "Clean"], SelfFallback: "Self_Mopping_Home"),
        new("vacuum", ["Vaccum", "Vacuum"], ["Use (Clean", "Use Vacuum Cleaner", "Use Premium Vacuum Cleaner", "Use"]),
        new("do the laundry", ["WashingMachine", "Dryer", "AirDresser"], ["Do Laundry"], AvoidNames: ["Repair"]),
        new("wash the dishes", ["SinkBowl", "Sink", "Dishwasher", "D_Sink", "U_Sink"], ["Wash the Dishes", "Insert Dishes", "Clear Off the Sink"], MaxObjects: 8),
        new("take out the trash", ["TrashCan"], ["Empty Trash Can", "Throw Away Trash"], MaxObjects: 6),
        new("clean the bathroom", ["Bath", "Shower", "Toilet", "Washstand"], ["Clean the Bathtub", "Clean"], AvoidNames: ["Boredom"], MaxObjects: 8),
        new("clean the kitchen", ["D_Sink", "D_CornerSink", "Sink", "Dishwasher", "KitchenRange"], ["Clear Off the Sink", "Clean"], AvoidNames: ["Boredom"], MaxObjects: 8),
        new("tidy up", ["Bookshelf", "Bookcase", "Closet", "DecoFloorShelf"], ["Tidy Up", "Clean"], MaxObjects: 6),
        new("water the plants", ["GrowFlower", "GrowTree", "OutPot", "Pot", "Plant"], ["Water"], AvoidNames: ["Share Feelings", "Lazily"], MaxObjects: 10),
        new("repair something", ["KitchenRange", "Fridge", "WashingMachine", "Dryer", "Dishwasher", "Toilet", "Bath", "Shower", "Washstand", "SinkBowl", "TV", "Computer", "Laptop", "Microwave", "CoffeeMachine", "Audio"], ["Repair"], AvoidObjects: ["TVTable"], MaxObjects: 16),
    ];

    public static Social? FindSocial(string? choice) =>
        SocialChoices.FirstOrDefault(s => s.Choice.Equals(choice?.Trim(), StringComparison.OrdinalIgnoreCase));

    public static Activity? FindActivity(string? choice) =>
        ActivityChoices.FirstOrDefault(a => a.Choice.Equals(choice?.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>True when the game lists a matching interaction but greys it out: there's nothing to do right now (clean dishes, an empty bin).</summary>
    public static bool OfferedButNotNeeded(IEnumerable<Offer> offers, string[] names, string[]? avoid = null) =>
        Best(offers.Select(o => o with { Enabled = true }), names, avoid) is not null;

    /// <summary>One thing the game offers, as interaction.list_available reports it.</summary>
    public sealed record Offer(string BagId, string DisplayName, bool Enabled);

    /// <summary>
    /// The best of the game's offers for these names: an exact display name first, then one that
    /// starts with the name, then one that contains it, earlier names before later ones.
    /// </summary>
    public static Offer? Best(IEnumerable<Offer> offers, string[] names, string[]? avoid = null)
    {
        var usable = offers
            .Where(o => o.Enabled && !string.IsNullOrWhiteSpace(o.DisplayName))
            .Where(o => avoid is null || !avoid.Any(a => o.DisplayName.Contains(a, StringComparison.OrdinalIgnoreCase)))
            .ToArray();
        Offer? best = null;
        var bestScore = int.MaxValue;
        foreach (var offer in usable)
        {
            for (var i = 0; i < names.Length; i++)
            {
                var name = names[i];
                var display = offer.DisplayName.TrimEnd('!', '.');
                var rank = display.Equals(name, StringComparison.OrdinalIgnoreCase) ? 0
                    : display.StartsWith(name, StringComparison.OrdinalIgnoreCase) ? 1
                    : display.Contains(name, StringComparison.OrdinalIgnoreCase) ? 2
                    : -1;
                if (rank < 0) continue;
                var score = i * 3 + rank;
                if (score < bestScore)
                {
                    bestScore = score;
                    best = offer;
                }

                break;
            }
        }

        return best;
    }

    /// <summary>Objects worth asking about for an activity, most likely first.</summary>
    public static IEnumerable<T> Candidates<T>(IEnumerable<T> objects, Func<T, string> tableId, string[] wanted, string[]? avoid = null)
    {
        return objects
            .Select(o => (Object: o, Id: tableId(o)))
            .Where(x => !string.IsNullOrWhiteSpace(x.Id))
            .Where(x => avoid is null || !avoid.Any(a => x.Id.StartsWith(a, StringComparison.OrdinalIgnoreCase)))
            .Select(x => (x.Object, x.Id, Rank: Array.FindIndex(wanted, w => x.Id.StartsWith(w, StringComparison.OrdinalIgnoreCase) || x.Id.StartsWith("D_" + w, StringComparison.OrdinalIgnoreCase) || x.Id.StartsWith("U_" + w, StringComparison.OrdinalIgnoreCase))))
            .Where(x => x.Rank >= 0)
            .OrderBy(x => x.Rank)
            // One of each kind is enough: two identical sofas offer the same things.
            .DistinctBy(x => x.Id)
            .Select(x => x.Object);
    }
}
