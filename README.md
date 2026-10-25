# Voxta × inZOI

Chat with the members of your inZOI household through [Voxta](https://voxta.ai). Each Zoi gets a
Voxta character that knows, live from your running game, how that Zoi is feeling, what they need,
what they are doing, their job, wishes and relationships. What they say to you appears as a speech
bubble above their head in the game.

> **Status: 0.1, experimental.** Built against Voxta 1.13 (alpha) and inZOI 0.11. Tested against
> recorded game data; it has not had much play time in the real game yet. Please report what breaks.

```
 inZOI (0.11+)                                  Voxta Server  ──  "inZOI" module
 ┌────────────────────────────────┐   MCP over    ┌──────────────────────────────────────┐
 │ built-in MCP server            │   localhost   │ reads the household every few seconds │
 │ localhost:11212                │ ◄──────────── │ "inzoi" augmentation:                │
 │  household, emotions, needs,   │   HTTP        │  • matches characters to Zois        │
 │  activities, jobs, wishes,     │               │  • keeps the chat context up to date │
 │  relationships                 │               │  • passes game events to characters  │
 │  speech bubbles, messages      │ ◄──────────── │  • shows replies as speech bubbles   │
 └────────────────────────────────┘               └──────────────────────────────────────┘
```

**No game mod needed.** Since version 0.11 (October 2026) inZOI serves an MCP interface on this PC
while it runs. The module talks to that directly.

## Setup

### 1. The Voxta module

In Voxta Server (1.13 or newer): **Add Modules → Module Registries**, add `Saintfrond/voxta-inzoi`,
then install **inZOI** from the list and add it.

The defaults work when inZOI and Voxta run on the same PC. Settings, under the module's
**Configure** page:

| Setting | What it does |
| --- | --- |
| inZOI MCP port | Port of inZOI's MCP server (default 11212). Change only if you start inZOI with `-climcpport=<port>`. |
| Seconds between game reads | How often the household is read while a chat is open (default 5). |
| Include relationships | Tell characters about family, friends and romance. |
| Who are you to the Zois? | The player, the Zoi you're controlling, or your own description. |
| Character → Zoi names | `Character Name = Zoi First Last` lines, for characters whose names don't match a Zoi. |
| Show replies as speech bubbles | Character replies appear above their Zoi, a sentence or two at a time. |
| Leave \*actions\* out of bubbles | Narration between asterisks stays in the chat only. |
| Show my messages in the game | Your messages appear too (above your Zoi, or as a message at the top of the screen). |
| Zois gesture while speaking | The speaking Zoi plays inZOI's own conversation animations (friendly, normal or upset; standing or seated). |
| Facial expressions while speaking | The Zoi's face follows the reply (laughing, sad, angry, surprised, smitten), otherwise their emotion. |
| Switch control to the speaker | Moves the camera to whoever speaks by taking control of them. Off by default. |
| Game events | Ignore events, tell characters quietly, or let them react right away. |
| Seconds between reactions | Limits how often a character speaks up on its own. |

### 2. The game

Just start inZOI (0.11 or newer) and load your household. The module finds the game on its own and
keeps retrying while the game is closed.

### 3. Characters

Make one Voxta character per Zoi you want to talk to, named like the Zoi (`Juve Lez`, or just
`Juve` if no one else in the household is called Juve), and turn on the **inZOI** augmentation
(`inzoi`) for it. Write the character's personality as you see that Zoi; the module adds the live
state on top. A group chat with several of them is a family conversation.

## Lip sync and camera

inZOI's interface has no lip sync and no way to point the camera at a Zoi. Instead, while a
character speaks (timed by Voxta's speech, or the length of the reply without text to speech),
their Zoi plays the conversation gestures the game itself uses when Zois talk, with a facial
expression that fits what they say. For the camera, **Switch control to the speaker** hands you
control of the speaking Zoi, which moves the camera to them.

## What the characters know

Read every few seconds while you play, and only sent to the chat when something changes in a way
they'd notice:

- city, day, time, season and weather; household name; today's festival or holiday
- for each Zoi in the chat: age, emotion, what they're doing right now, where they are, needs that
  are low, job and whether they're at work, current wishes, and their closer relationships
- a line about everyone else in the household

Events a character hears about (depending on the **Game events** setting): a strong new emotion, a
need running low, a new wish, a new job or promotion, a wedding, someone moving in or out.

## Troubleshooting

- **The context says the game isn't running:** is inZOI 0.11 or newer running on the same PC? Check
  that `http://localhost:11212/mcp` is reachable (another program on that port? start inZOI with
  `-climcpport=<port>` and set the same port in the module).
- **The character doesn't know their Zoi:** the Voxta log says which character didn't match and
  lists the household. Add a `Character → Zoi names` line.
- **After a game update:** the Voxta log names any detail inZOI stopped answering. Each detail is
  read separately, so an update usually costs one detail rather than the whole module.

## Development

```
Voxta.Modules.Inzoi/     the Voxta module (C#, .NET 10, Voxta.Sdk.Modules 1.13)
  Mcp/                   a small MCP client (streamable HTTP, JSON or SSE answers)
  Game/                  reading the household, finding events, the shared poller
  Household/             character ↔ Zoi matching, the context, speech bubbles
  ChatAugmentations/     the "inzoi" augmentation
```

**Voxta module:** `dotnet build`, then in Voxta **Add Modules → Add Custom Module** and point it
at `Voxta.Modules.Inzoi`. Change, build, **Apply**. Commit `packages.lock.json` with package changes.

**Releasing:** set the version in `Voxta.Modules.Inzoi/module.json` and commit. Then either click
**Actions → publish release → Run workflow**, or `git tag v0.1.0 && git push --tags`. A version like
`0.2.0-beta.1` makes a pre-release.

The module's release pipeline is Voxta's own ([voxta-module-template](https://github.com/voxta-ai/voxta-module-template)).

## License

MIT, see [LICENSE](LICENSE).

Not affiliated with or endorsed by KRAFTON or inZOI Studio. inZOI is a trademark of KRAFTON, Inc.
