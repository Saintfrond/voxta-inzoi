using System.Globalization;
using Microsoft.Extensions.Logging;
using Voxta.Abstractions.Registration;
using Voxta.Abstractions.Services;
using Voxta.Abstractions.Services.ChatAugmentations;
using Voxta.Modules.Inzoi.Configuration;
using Voxta.Modules.Inzoi.Game;

namespace Voxta.Modules.Inzoi.ChatAugmentations;

/// <summary>
/// Offers the "inzoi" augmentation to chats. Planning is cheap; the game is only read once a chat
/// that turned the key on actually starts.
/// </summary>
public class ChatAugmentationsService(
    ILogger<ChatAugmentationsService> logger,
    ILoggerFactory loggerFactory,
    InzoiGame game
) : ServiceBase(logger), IChatAugmentationsService
{
    private readonly InzoiGame _game = AttachLogging(game, loggerFactory);

    private static InzoiGame AttachLogging(InzoiGame game, ILoggerFactory loggerFactory)
    {
        game.AttachLogger(loggerFactory);
        return game;
    }

    public Task<AugmentationPlanItem[]> PlanAsync(
        IChatAugmentationPlanContext context,
        CancellationToken cancellationToken
    )
    {
        var settings = ReadSettings();
        var augmentation = InzoiConstants.Augmentations.Get(InzoiConstants.AugmentationKey);
        return Task.FromResult<AugmentationPlanItem[]>([
            new AugmentationPlanItem
            {
                Key = augmentation.Key,
                Label = augmentation.Label,
                Description = augmentation.Description,
                // Only where a character or chat asked for it: a household has no business in every chat.
                Activation = AugmentationActivation.OptIn,
                Suggested = false,
                IncludeInCatchAll = false,
                ActivateAsync = async (session, ct) =>
                {
                    var instance = new InzoiChatAugmentationInstance(
                        session,
                        _game,
                        settings,
                        loggerFactory.CreateLogger<InzoiChatAugmentationInstance>()
                    );
                    await instance.StartAsync(ct);
                    return instance;
                },
            },
        ]);
    }

    private InzoiSettings ReadSettings()
    {
        var portText = ModuleConfiguration.GetRequired(ModuleConfigurationProvider.Port);
        if (!int.TryParse(portText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var port) || port is < 1 or > 65535)
        {
            logger.LogWarning("'{Port}' is not a valid port for inZOI, using {Default}", portText, InzoiGame.DefaultPort);
            port = InzoiGame.DefaultPort;
        }

        var pollText = ModuleConfiguration.GetRequired(ModuleConfigurationProvider.PollSeconds);
        if (!double.TryParse(pollText, NumberStyles.Float, CultureInfo.InvariantCulture, out var poll) || poll < 2)
            poll = 5;

        var cooldownText = ModuleConfiguration.GetRequired(ModuleConfigurationProvider.ReactCooldownSeconds);
        if (!int.TryParse(cooldownText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var cooldown) || cooldown < 0)
            cooldown = 90;

        return new InzoiSettings(
            Port: port,
            PollInterval: TimeSpan.FromSeconds(poll),
            IncludeRelationships: ModuleConfiguration.GetRequired(ModuleConfigurationProvider.IncludeRelationships),
            UserRole: ModuleConfiguration.GetRequired(ModuleConfigurationProvider.UserRole),
            CustomUserRole: ModuleConfiguration.GetRequired(ModuleConfigurationProvider.CustomUserRole),
            NameAliases: ModuleConfiguration.GetRequired(ModuleConfigurationProvider.NameAliases),
            ShowRepliesInGame: ModuleConfiguration.GetRequired(ModuleConfigurationProvider.ShowRepliesInGame),
            HideActionsInBubbles: ModuleConfiguration.GetRequired(ModuleConfigurationProvider.HideActionsInBubbles),
            ShowUserMessagesInGame: ModuleConfiguration.GetRequired(ModuleConfigurationProvider.ShowUserMessagesInGame),
            SpeakingGestures: ModuleConfiguration.GetRequired(ModuleConfigurationProvider.SpeakingGestures),
            SpeakingFaces: ModuleConfiguration.GetRequired(ModuleConfigurationProvider.SpeakingFaces),
            SwitchToSpeaker: ModuleConfiguration.GetRequired(ModuleConfigurationProvider.SwitchToSpeaker),
            GameEvents: ModuleConfiguration.GetRequired(ModuleConfigurationProvider.GameEvents),
            ReactCooldown: TimeSpan.FromSeconds(cooldown)
        );
    }
}

/// <summary>The module settings as one chat sees them, read once when it starts.</summary>
public sealed record InzoiSettings(
    int Port,
    TimeSpan PollInterval,
    bool IncludeRelationships,
    string UserRole,
    string? CustomUserRole,
    string? NameAliases,
    bool ShowRepliesInGame,
    bool HideActionsInBubbles,
    bool ShowUserMessagesInGame,
    bool SpeakingGestures,
    bool SpeakingFaces,
    bool SwitchToSpeaker,
    string GameEvents,
    TimeSpan ReactCooldown
);
