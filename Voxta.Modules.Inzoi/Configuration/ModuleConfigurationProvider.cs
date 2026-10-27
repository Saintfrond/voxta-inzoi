using Voxta.Abstractions.Registration;
using Voxta.Abstractions.Security;
using Voxta.Model.Shared.Forms;
using Voxta.Modules.Inzoi.Household;

namespace Voxta.Modules.Inzoi.Configuration;

public class ModuleConfigurationProvider : ModuleConfigurationProviderBase, IModuleConfigurationProvider
{
    public static string[] FieldsRequiringReload => [];

    public static readonly FormTextField Port = new()
    {
        Name = "Port",
        Label = "inZOI MCP port",
        Text = "The port of inZOI's built-in MCP server on this PC. Default 11212. Only change it if you start inZOI with -climcpport=<port>.",
        DefaultValue = "11212",
    };

    public static readonly FormTextField PollSeconds = new()
    {
        Name = "PollSeconds",
        Label = "Seconds between game reads",
        Text = "How often the household's state is read from the game while a chat is open. 5 is a good balance; minimum 2.",
        DefaultValue = "5",
    };

    public static readonly FormBooleanField IncludeRelationships = new()
    {
        Name = "IncludeRelationships",
        Label = "Include relationships",
        Text = "Tell characters about their Zoi's relationships (family, friends, romance). Read every half minute or so.",
        DefaultValue = true,
    };

    public static readonly FormChoicesField UserRole = new()
    {
        Name = "UserRole",
        Label = "Who are you to the Zois?",
        Text = "How the household sees you, the person chatting.",
        DefaultValue = Choices.UserIsWatcher,
        Choices =
        [
            new() { Value = Choices.UserIsWatcher, Label = "The player (the unseen presence who guides their lives)" },
            new() { Value = Choices.UserIsPossessedZoi, Label = "The Zoi I'm currently controlling" },
            new() { Value = Choices.UserIsCustom, Label = "Custom (write it below)" },
        ],
    };

    public static readonly FormMultilineField CustomUserRole = new()
    {
        Name = "CustomUserRole",
        Label = "Custom description of you",
        Text = "Used when 'Who are you to the Zois?' is Custom. {{ user }} is your name.",
        Required = false,
        DefaultValue = "",
        Rows = 3,
        Placeholder = "{{ user }} is a neighbour who often drops by for coffee.",
    };

    public static readonly FormMultilineField NameAliases = new()
    {
        Name = "NameAliases",
        Label = "Character → Zoi names",
        Text = "Characters are matched to Zois by name automatically (full name, then a unique first name). Add a line `Character Name = Zoi First Last` for any that don't match.",
        Required = false,
        DefaultValue = "",
        Rows = 4,
        Placeholder = "Mom = Paul Lez",
    };

    public static readonly FormBooleanField ShowRepliesInGame = new()
    {
        Name = "ShowRepliesInGame",
        Label = "Show replies as speech bubbles",
        Text = "Each reply from a character appears as a speech bubble above their Zoi's head. Long replies are shown a sentence or two at a time.",
        DefaultValue = true,
    };

    public static readonly FormBooleanField HideActionsInBubbles = new()
    {
        Name = "HideActionsInBubbles",
        Label = "Leave *actions* out of bubbles",
        Text = "Narration between asterisks (*smiles*) stays in the chat but isn't shown in the speech bubble.",
        DefaultValue = true,
    };

    public static readonly FormBooleanField ShowUserMessagesInGame = new()
    {
        Name = "ShowUserMessagesInGame",
        Label = "Show my messages in the game",
        Text = "Your own messages appear too: above the Zoi you control when you play as them, otherwise as a short message at the top of the screen.",
        DefaultValue = false,
    };

    public static readonly FormBooleanField SpeakingGestures = new()
    {
        Name = "SpeakingGestures",
        Label = "Zois gesture while speaking",
        Text = "While a character speaks, their Zoi plays inZOI's own conversation animations (friendly, normal or upset, standing or seated), so you can see who's talking. It briefly interrupts what the Zoi was doing.",
        DefaultValue = true,
    };

    public static readonly FormBooleanField SpeakingFaces = new()
    {
        Name = "SpeakingFaces",
        Label = "Facial expressions while speaking",
        Text = "The Zoi's face follows the reply: laughing at a joke, sad, angry, surprised, smitten. Otherwise it shows their current emotion.",
        DefaultValue = true,
    };

    public static readonly FormBooleanField SwitchToSpeaker = new()
    {
        Name = "SwitchToSpeaker",
        Label = "Switch control to the speaker",
        Text = "inZOI can't point the camera at a Zoi on its own, but switching control to them moves the camera there. On: you take control of whichever household Zoi is speaking. Best for one-on-one chats; in a group chat control jumps around.",
        DefaultValue = false,
    };

    public static readonly FormBooleanField AllowActions = new()
    {
        Name = "AllowActions",
        Label = "Characters can act in the game",
        Text = "Characters can make their Zoi do things in the game when they do them in the chat: kiss, hug or dance with another household member, cook, eat, sleep, watch TV and so on. Uses Voxta's action inference, so it needs an action inference service.",
        DefaultValue = true,
    };

    public static readonly FormBooleanField AllowExpressions = new()
    {
        Name = "AllowExpressions",
        Label = "Characters show emotions and gestures",
        Text = "When a reply has a strong reaction, the character's Zoi shows it with inZOI's own animations and a matching face: laughing, giggling, crying, being angry or scared, clapping, cheering, dancing, a cat-heart pose and more. Uses Voxta's action inference.",
        DefaultValue = true,
    };

    public static readonly FormMultilineField CustomAnimations = new()
    {
        Name = "CustomAnimations",
        Label = "Your own animations",
        Text = "Extra emotions and gestures the characters can choose, one per line: `name = AnimationId` for any game or mod animation (find ids with the Animation Player mod, F9), or `name = ai:3` for your AI emote in slot 3. Names you use here replace a built-in of the same name.",
        Required = false,
        DefaultValue = "",
        Rows = 5,
        Placeholder = "new year bow = Hanbok_NewYearBow\nmy victory dance = ai:1",
    };

    public static readonly FormChoicesField GameEvents = new()
    {
        Name = "GameEvents",
        Label = "Game events",
        Text = "What happens when something notable happens to a Zoi in the chat (a strong emotion, a need running low, a new job, a wedding...).",
        DefaultValue = Choices.EventsSilent,
        Choices =
        [
            new() { Value = Choices.EventsOff, Label = "Ignore them" },
            new() { Value = Choices.EventsSilent, Label = "Tell the character quietly (it may bring it up next reply)" },
            new() { Value = Choices.EventsReact, Label = "Let the character react right away" },
        ],
    };

    public static readonly FormTextField ReactCooldownSeconds = new()
    {
        Name = "ReactCooldownSeconds",
        Label = "Seconds between reactions",
        Text = "With 'react right away', a character speaks up on its own at most once per this many seconds.",
        DefaultValue = "90",
    };

    public Task<FormField[]> GetModuleConfigurationFieldsAsync(
        IAuthenticationContext auth,
        ISettingsSource settings,
        CancellationToken cancellationToken
    )
    {
        return Task.FromResult(FormBuilder.Build(
            FormTitleField.Create("Game connection"),
            Port,
            PollSeconds,
            IncludeRelationships,
            FormTitleField.Create("Household"),
            UserRole,
            CustomUserRole,
            NameAliases,
            FormTitleField.Create("In the game"),
            ShowRepliesInGame,
            HideActionsInBubbles,
            ShowUserMessagesInGame,
            SpeakingGestures,
            SpeakingFaces,
            SwitchToSpeaker,
            AllowActions,
            AllowExpressions,
            CustomAnimations,
            GameEvents,
            ReactCooldownSeconds
        ));
    }
}
