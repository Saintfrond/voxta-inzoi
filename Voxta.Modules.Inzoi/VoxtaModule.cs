using Microsoft.Extensions.DependencyInjection;
using Voxta.Abstractions.Modules;
using Voxta.Abstractions.Registration;
using Voxta.Modules.Inzoi.ChatAugmentations;
using Voxta.Modules.Inzoi.Configuration;
using Voxta.Modules.Inzoi.Game;

namespace Voxta.Modules.Inzoi;

public class VoxtaModule : IVoxtaModule
{
    public void Configure(IVoxtaModuleBuilder builder)
    {
        builder.Register(new()
        {
            ServiceName = InzoiConstants.ServiceName,
            Label = "inZOI",
            Version = ModuleInfo.Version,
            Flags = [ModuleFlag.Experimental, ModuleFlag.SupportsExplicitContent],
            Supports = new()
            {
                ChatAugmentations = new(ServiceDefinitionCategoryScore.Medium),
            },
            Pricing = ServiceDefinitionPricing.Free,
            Hosting = ServiceDefinitionHosting.Builtin,
            Notes = "Chat with your inZOI household. Needs inZOI 0.11 or newer running on this PC (it serves its MCP interface on localhost:11212 by itself). Turn on the 'inzoi' augmentation on a character named like one of your Zois.",
            HelpLink = "https://github.com/Saintfrond/voxta-inzoi",
            Augmentations = InzoiConstants.Augmentations,
            Logo = ModuleLogo.EmbeddedResource(typeof(VoxtaModule), "Assets.logo.svg"),
            ModuleConfigurationProviderType = typeof(ModuleConfigurationProvider),
            ModuleConfigurationFieldsRequiringReload = ModuleConfigurationProvider.FieldsRequiringReload,
        });

        // One link to the game for the whole server, shared by every chat. It only polls the game
        // while at least one chat uses it. Registered through a factory so the container disposes
        // it when Voxta unloads or updates the module.
        var game = InzoiGame.GetOrCreate();
        builder.Services.AddSingleton(_ => game);
        builder.AddChatAugmentationsService<ChatAugmentationsService>(InzoiConstants.ServiceName);
    }
}
