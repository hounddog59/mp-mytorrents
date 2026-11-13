# MyTorrents for MediaPortal 1

MyTorrents finds and downloads TV episodes and movies for your MediaPortal library, and files them where
MediaPortal expects them. It is a 2026 revival of the old MP-MyTorrents plugin (originally by Sohvaperuna /
edalex86, last release 0.80 in 2018), updated for **MediaPortal 1.39 x64**.

The aim is to be easy and friendly for the average person: plain words, sensible defaults, and help where
it's needed.

## What it does

- **Setup assistant.** The first time you run it, it installs and sets up qBittorrent (recommended) and
  Jackett, picks some reliable search sites, and starts everything with Windows.
- **Your torrent program.** It works with qBittorrent (recommended), BiglyBT, Deluge, Transmission or uTorrent,
  with step-by-step help for turning on each one's remote control and a "Check it now" button.
- **Search.** Jackett (all your indexers in one go) plus your own sites. Results are sorted, and fakes,
  samples and foreign-language dubs are filtered out.
- **Auto grabber for TV.** It follows your shows in MP-TVSeries and gets new episodes as they air. The
  episode must really belong to that show, and it picks the quality, size and sound you want.
- **Wanted movies.** Movies from your Trakt lists and Moving Pictures are found and fetched.
- **When a download finishes.** It renames the files, copies them into your TV or Movies folders, and tells
  MP-TVSeries / Moving Pictures to add them. It cleans up after seeding, but never deletes anything inside
  your library.
- **Stuck downloads.** Downloads with no seeds are replaced by another copy. Ones that are nearly finished
  (90% or more) are protected, and can be used straight away if the file plays.
- **Drop folder.** Put a movie or episode in it and MyTorrents files it for you. It won't replace something
  you already have.
- **Works with MediaPortal closed.** A small background helper keeps going, and there's a web page for
  settings and progress at `http://<your PC>:9118`.
- **Settings in three places, always the same.** MediaPortal, *MyTorrents Configuration* (desktop icon) and
  the web page all use one settings file, so they always agree.
- **A daily check-up** that tells you in plain words if anything needs your attention.
- **Any skin.** MyTorrents adds its own screens to skins that don't have them.

See [CHANGELOG.md](CHANGELOG.md) for everything that changed, version by version.

## Install

1. Close MediaPortal.
2. Double-click the `MyTorrents.x.x.x.mpe1` file (MediaPortal Extension Installer).
3. On the last page, leave **Open MyTorrents Configuration** ticked. The setup assistant walks you through
   the rest.

You can change settings at any time, either in **MyTorrents Configuration** (desktop icon, no need to start
MediaPortal) or in MediaPortal under *My Torrents*.

Updating: just install the new `.mpe1` over the old one. Your settings are kept, and the installer stops and
restarts the background helper by itself.

## Requirements

- MediaPortal 1.39 x64 (1.34 or newer should work)
- MP-TVSeries for TV shows (the community 4.8 version is recommended) and Moving Pictures for movies
- A torrent program. Setup installs qBittorrent if you don't have one.

## Torrent programs

| Program | Notes |
|---|---|
| **qBittorrent** (recommended) | Fully tested. Setup installs it, turns on its Web UI and starts it minimised with Windows. |
| BiglyBT | Through its *Web Remote* plugin (Tools → Plugins → Installation Wizard). The replacement for Vuze/Azureus. |
| Deluge | Web UI, default password `deluge`. |
| Transmission | Remote access turned on in its settings. |
| uTorrent | Works, but not recommended (adverts, and old versions have known security problems). |

## Adding torrent sites yourself

In MyTorrents Configuration → **Search sites**:

- **Jackett:** add sites in Jackett (*Open Jackett* → *Add indexer*) and tick the ones to use.
  The recommended ones are already ticked.
- **My sites:** type a name and the site's search address, with `%search%` where the words go. (Search the
  site in your browser, copy the address, and replace your search words with `%search%`.) Then click
  *Test site*. Sites with a "checking your browser" page only work through Jackett.

## Skins

- **Any skin works.** At start-up MyTorrents adds its universal screens to every skin that has no MyTorrents
  screens of its own, at 1920×1080 or 1280×720 to match the skin.
- Skins with their own MyTorrents screens (TITANIUS, Frames, Titan…) are left alone.
- The universal files carry a marker comment. Delete them from a skin to get fresh copies on the next start.
  Files without the marker (a skin author's own) are never changed.
- The designs live in `Skin\tools\make_universal.py`. `Skin\tools\preview.py` renders a preview PNG without
  MediaPortal.

## Building

1. Open `MyTorrents.sln` in Visual Studio 2019 or newer, or run `dotnet build MyTorrents.sln -c Release`.
   Output goes to `bin\Release`.
2. Run `python Installer\build_mpe1.py` to make the `.mpe1` package.

MediaPortal reference DLLs are in `lib\MediaPortal`. `lib\MediaPortal\MP-TVSeries.dll` is **not** included,
because a built MP-TVSeries has API keys inside. Copy it from
`C:\Program Files\Team MediaPortal\MediaPortal\plugins\Windows\` before building.

## Layout

- `src\MyTorrents` – the MediaPortal plugin (screens, auto grabber, watch list, finished downloads,
  web page, check-up)
- `src\MyTorrentsConfig` – the stand-alone *MyTorrents Configuration.exe*, the setup assistant and the
  background helper
- `src\TorrentEngine` – shared torrent-program interface and helpers
- `src\QBittorrentEngine`, `src\TransmissionEngine` (BiglyBT / Transmission), `src\DelugeEngine`,
  `src\UTorrentEngine`, `src\AzureusEngine` – one per torrent program
- `src\TorrentSearch` – Jackett and your own search sites
- `src\RSSReader` – RSS/Atom feeds
- `Skin` – the universal screens and the tools that make them
- `Installer` – the `.mpe1` package builder

## The rule for every feature

Easy and friendly for the average person: plain words, no jargon, sensible defaults, and help where it's
needed. Anything you can set or do in MediaPortal can also be done in MyTorrents Configuration, and the other
way round. Both use the same setting or the same file, so they always agree.

## Credits and licence

- The original MP-MyTorrents was written by Sohvaperuna and edalex86.
- The 2026 revival is by Mark Willmer (hounddog59), developed with Claude (Anthropic).
- Licensed under the GNU General Public License v3. See [LICENSE](LICENSE).
