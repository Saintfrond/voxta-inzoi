<div align="center">

# 🏡 How to use with Voxta

**Talk to your inZOI household, and let them talk back.**

![Voxta](https://img.shields.io/badge/Voxta-1.13%2B-6f42c1?style=for-the-badge)
![inZOI](https://img.shields.io/badge/inZOI-0.11%2B-1f9d8b?style=for-the-badge)
![No game mod needed](https://img.shields.io/badge/game%20mod-not%20needed-2ea44f?style=for-the-badge)
![Status](https://img.shields.io/badge/status-experimental-orange?style=for-the-badge)

<img src="docs/images/inzoi-in-game.png" alt="A Zoi answering in the game with a speech bubble" width="820">

<sub>A reply from Voxta, spoken and shown above the Zoi in the game.</sub>

</div>

---

## ✨ What it does

Since its latest version, inZOI has a built-in **MCP server**, so this module connects
Voxta straight to the running game.

- 💬 **Talk to one Zoi or the whole household.** They answer with voice and an in-game speech bubble.
- 🍿 **Or just watch.** Let them talk to each other while you listen in.
- 🧠 **They know their lives.** Each character knows their Zoi's live emotion, needs, job, wishes,
  what they're doing right now, and their relationships.
- 🎭 **They react in the game.** The module knows inZOI's actions and emotes, so a Zoi can laugh,
  cry, cook, clean or kiss when it happens in the chat.
- 🔄 **Install once.** New versions show up as updates inside Voxta.

---

## 📦 Installation

### 1. Add the module source

Go to **Settings → Module Sources** and click **Add source**.

<p align="center"><img src="docs/images/01-settings-module-sources.png" alt="Voxta settings, Module Sources" width="460"></p>

Paste the repository link and click **Add**. The source appears in the list.

```text
https://github.com/Saintfrond/voxta-inzoi
```

<p align="center"><img src="docs/images/02-add-source.png" alt="Adding the GitHub repository as a module source" width="460"></p>

### 2. Show pre-releases

The module is still in alpha. Open the **⋯** menu next to the new source and turn on
**Show pre-releases**.

<p align="center"><img src="docs/images/03-show-pre-releases.png" alt="The source's menu with Show pre-releases" width="480"></p>

### 3. Browse and install

Click **Browse**, then **Install** next to **inZOI**.

<p align="center"><img src="docs/images/04-browse-install.png" alt="The inZOI module in the source's list" width="720"></p>

> [!NOTE]
> Voxta warns that third-party modules aren't made by Voxta. Trusting the repo is your call;
> Voxta runs its own security checks too. Click **Install** again on the next screen.

### 4. Add and configure

Confirm with **Add and continue**.

<p align="center"><img src="docs/images/05-add-and-continue.png" alt="The inZOI module card with Install" width="460"></p>

This opens the module's settings page. The options explain themselves, so adjust them to your
liking and click **Save and install**.

> [!TIP]
> **That's it.** You don't need to do anything in inZOI itself: no mod and no copied files. The
> module connects to the game's MCP server whenever the game is running.

---

## 🎮 Using the module

### Create your characters

Create a Voxta character for each Zoi you want to talk to, **named exactly like the Zoi**. That
can be one Zoi or the whole household.

> [!IMPORTANT]
> Give every one of them the **inZOI** augmentation, which the module installed. Without it, the
> character knows nothing about the game.

**Already have characters you'd like to use?** You don't have to rename them. Map them to Zois in
the module's settings under **Character → Zoi names**, one line each:

```text
Character Name = Zoi First Last
```

<p align="center"><img src="docs/images/06-name-overrides.png" alt="The Character → Zoi names setting" width="600"></p>

### Keep the profiles simple

Voxta reads most of what a character needs from the game, so keep the profile and scenario light.
A short personality is enough to steer them, plus a line that tells them what they are:

```text
{{ char }} knows she's an inZOI Zoi and that {{ user }} is the watcher who guides her life.
She finds this mildly outrageous and enjoys giving {{ user }} a piece of her mind about the
choices made for her, but deep down she's glad someone is paying attention.
```

For a scenario, add the characters to their roles and describe the household in a sentence or two:

```text
{{ scenario_chars.juve }} and {{ scenario_chars.paula }} are two women living together in a
house. They live a calm life together, enjoying each other's company.
```

### Start playing

1. Start **inZOI** and load your household.
2. In Voxta, start a chat with your character or scenario.

---

## 🍿 Tip: let them talk to each other

Make a scenario with your household members and set its chat flow to **Story**. The Zois then
talk among themselves, and you can still jump in.

To talk over them, turn off **Pause While They Talk** in Voxta's **Preferences → Voice**.

<p align="center"><img src="docs/images/07-pause-while-they-talk.png" alt="Voxta voice preferences with Pause While They Talk" width="720"></p>

---

<details>
<summary><b>🛠️ Something not working?</b></summary>

<br>

- **The character doesn't know anything about the game.** Check that inZOI (0.11 or newer) is
  running and that the character has the **inZOI** augmentation.
- **The character doesn't know which Zoi they are.** The name doesn't match. Add a line under
  **Character → Zoi names**.
- **Nothing happens in the game.** Check that `http://localhost:11212/mcp` is reachable. If you
  start inZOI with `-climcpport=<port>`, set the same port in the module.

More detail is in the main [README](README.md).

</details>

<div align="center">
<sub>Not affiliated with or endorsed by KRAFTON or inZOI Studio. inZOI is a trademark of KRAFTON, Inc.</sub>
</div>
