# Jellyfin AceStream Plugin

Watch AceStream channels inside Jellyfin. The plugin adds an **AceStream** channel to your library where you can browse the public channel index by category, add your own channels from an M3U playlist, and play everything through Jellyfin's normal player — no external apps.

## What you get

- **Browse** — category folders (Sport, TV, Movies, …) filled from the AceStream engine's search index.
- **Custom channels** — paste an M3U playlist with `acestream://` links and they appear in a "Custom" folder.
- **Smart playback** — the plugin waits for the P2P stream to actually deliver data, probes the real codecs, and hands Jellyfin a stream it can remux instead of re-encoding blind.

## Requirements

| Component | Version / Notes |
|-----------|-----------------|
| Jellyfin | 10.11.x |
| [AceStream engine](https://docs.acestream.net/products/) | Reachable over HTTP (default port `6878`) |
| [acexy](https://github.com/Javinator9889/acexy) | HTTP proxy in front of the engine (default port `8080`); playback goes through it |
| .NET runtime | Bundled with Jellyfin — nothing extra to install |

A typical setup runs all three (Jellyfin, engine, acexy) as Docker containers on the same network.

## Quick start

1. Download `Jellyfin.Plugin.AceStream.dll` and `meta.json` (or [build from source](#build-from-source)).
2. Copy both files into your Jellyfin config dir: `plugins/AceStream/`.
3. Restart Jellyfin. The log should show `Loaded plugin: AceStream`.
4. Open **Dashboard → Plugins → AceStream** and set:
   - **Engine URL** — e.g. `http://acestream-engine:6878`
   - **Proxy URL** — e.g. `http://acexy:8080`
5. Open **Home → Channels → AceStream** and browse a category. Done.

## Configuration

All settings live in **Dashboard → Plugins → AceStream**.

| Setting | Default | What it does |
|---------|---------|--------------|
| Engine URL | — | Base URL of the AceStream engine (search + stream readiness) |
| Proxy URL | — | Base URL of acexy; playback streams flow through it |
| Probe analyze duration | `5000` ms | How long ffprobe analyzes the stream to detect codecs |
| Codec cache TTL | `5` min | How long detected codecs are reused before re-probing |
| Readiness timeout | `30` s | Max time to wait for a cold P2P stream to start delivering before skipping the codec probe (`0` = skip the check) |
| Custom channels | empty | M3U playlist for the "Custom" folder (see below) |

Missing or wrong URLs never break browsing — folders just come back empty and a warning lands in the Jellyfin log.

## Custom channels (M3U)

Paste a standard M3U playlist; only `acestream://` entries are used, anything else is ignored:

```m3u
#EXTM3U
#EXTINF:-1 group-title="Sports",My Sports Channel
acestream://f23bf1ae6d6bef0a6eab9f5e29441c2e6526f24a
#EXTINF:-1,Another Channel
acestream://628c180b147a6c1b16f1c326bb59cf6c7d48bd1c
```

> **The 40-hex id must be the stream's *infohash*.** Public channel lists already use infohashes, so they work as-is. If you broadcast your own stream, beware: the engine's `get_content_id` value is **not** the infohash and will silently fail to play. Get the real infohash by opening the transport file once and reading the response:
>
> ```bash
> curl "http://<engine>:6878/ace/getstream?url=<URL-to-your.acelive>&format=json"
> # → response.infohash is the value to use
> ```

## How it works

```
Browse:  Jellyfin ──> plugin ──> engine /search ──> category folders
Play:    Jellyfin ──> plugin ──> readiness gate (engine session stat)
                          │            └─ waits until the P2P swarm delivers ("dl")
                          └──> ffprobe via acexy ──> real codecs ──> Jellyfin remuxes
```

P2P streams take 10–25 s to warm up from cold. The readiness gate watches the engine's own session status instead of guessing with a fixed timeout, so the codec probe runs as soon as the stream is actually alive — and a dead channel fails fast and clean.

## Build from source

```bash
dotnet publish src/Jellyfin.Plugin.AceStream -c Release -o out
# deploy out/Jellyfin.Plugin.AceStream.dll + meta.json to plugins/AceStream/
```

Run the tests:

```bash
dotnet test
# On a machine with only the .NET 10 runtime (project targets net9.0):
DOTNET_ROLL_FORWARD=Major dotnet test
```

## For contributors

The codebase is hexagonal: `Domain` and `Application` know nothing about Jellyfin or HTTP; adapters live in `Infrastructure/{Engine,Jellyfin,Proxy}` and are swapped via DI. `AceChannel` is the canonical entity and `Infohash` its identity. Development is strict TDD — see [CLAUDE.md](CLAUDE.md) for the full engineering rules.
