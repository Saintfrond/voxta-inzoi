using Voxta.Abstractions.Registration;

namespace Voxta.Modules.Inzoi;

public static class InzoiConstants
{
    /// <summary>What user settings are saved against. Never change it once published.</summary>
    public const string ServiceName = "Inzoi";

    /// <summary>The key a character or chat turns on to get the household in its chat.</summary>
    public const string AugmentationKey = "inzoi";

    /// <summary>Everything this module puts in a chat's context is replaced together, under this key.</summary>
    public const string ContextKey = ServiceName;

    public static readonly AugmentationKeyDescriptor[] Augmentations =
    [
        new(
            AugmentationKey,
            "inZOI",
            "Gives the character its Zoi's live state from a running inZOI game (emotion, needs, job, wishes, what they are doing, relationships) and shows its replies as speech bubbles in the game."
        ),
    ];
}
