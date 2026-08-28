# CS2-Translator

Cross-platform real-time chat translation tool for Counter-Strike 2.

CS2-Translator reads the CS2 console output, detects chat messages, and translates them automatically, so you can understand teammates and enemies without leaving the game.

Works on **Windows and Linux**, runs as a **single file**, and requires only official CS2 launch options.

## Features

- Real-time chat translation inside CS2
- Cross-platform (Windows & Linux)
- Single-file executable
- Google Translate integration
- Translation cache (same messages = no re-translation)
- Automatic language detection
- Team / dead / spectator chat is labelled
- Original message shown under the translation (optional)
- Works with any CS2 install location
- In-game workflow (no alt-tab needed)
- Debug logging (`-debug` flag)


## How it works

1. CS2 writes console output using `-condebug`
2. CS2-Translator parses chat messages
3. Messages appear immediately, and the translation fills in when it arrives

No mods. No game file changes.


## Installation

### 1) Download

Grab the latest release:
https://github.com/ParadoxLeon/CS2-Translator/releases

### 2) Set CS2 launch options

Add this in Steam:
```
-condebug
```

### 3) Start

1. Launch CS2
2. Start CS2-Translator
3. Configure your Settings

If the status bar says the folder is wrong, use **Browse...** in Settings to point at the
`Counter-Strike Global Offensive` folder (the one containing `game/csgo`).


## Settings

| Setting | What it does |
| --- | --- |
| CS2 installation path | Folder containing `game/csgo/console.log`. Validated as you type. |
| Translate into | Any Google Translate language code (`en`, `de`, `ru`, `tr`, `zh-CN`, ...). |
| Your player name | Messages from this name are shown as-is and never sent for translation. |
| Font sizes | Separate sizes for player names and translations. |
| Translate automatically | Turn off to list chat without translating it. |
| Show the original message | Displays the untranslated text under the translation. |
| Translate history on startup | Off by default. See the note below. |

Changing any setting takes effect immediately - no restart needed.


## Rate limiting

CS2-Translator uses the free Google Translate endpoint, which throttles per IP address.
To stay under that limit it:

- caches every translation, so repeated messages cost nothing
- spaces requests at least 350ms apart and sends one at a time
- retries with backoff, alternating between two Google endpoints
- pauses translation entirely after repeated refusals, backing off from 20s up to
  5 minutes, and shows the countdown in the status bar
- skips messages with no letters, and never translates your own messages

**"Translate history on startup" is off by default and should usually stay off.**
`console.log` is never cleared by CS2, so it grows across every session. Translating all
of it at launch means hundreds of requests in a few seconds, which is the fastest way to
get rate limited. With the option off, past chat is still listed (untranslated) and only
new messages are translated.


## Debug Mode

Start CS2-Translator with:
```
-debug
```

This writes a detailed log to the data folder. Use **Open data folder** in Settings to
find it. Logs rotate automatically at 5MB.


## Config & Data Location

### Linux
```
~/.config/CS2-Translator
```

### Windows (Roaming)
```
%APPDATA%/CS2-Translator
```

This folder contains:
- `config.json`
- `cache-<language>.json` translation cache
- `logs/` (if debug enabled)


## Supported Languages

CS2-Translator supports all Google Translate languages.  
Full list:
https://cloud.google.com/translate/docs/languages


## Building from source

Requires the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).

```bash
dotnet build CS2-Translator.sln
```

```bash
dotnet test CS2-Translator.sln
```

Publish a single-file build:

```bash
dotnet publish CS2.Translator.UI/CS2.Translator.UI.Desktop -c Release -r win-x64 --self-contained -p:PublishSingleFile=true
```

Replace `win-x64` with `linux-x64` for Linux.


## Update

To update:
1. Download the latest release
2. Delete the old version
3. Start the new one

No installer. No migration needed.


## Limitations

- Google Translate is rate-limited per IP; see the section above
- Some community servers use custom chat formats that may not be detected


## Roadmap

- Custom translation providers
- UI improvements
- Overlay
