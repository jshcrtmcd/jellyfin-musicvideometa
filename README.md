# Music Video Metadata for Jellyfin

A Jellyfin plugin for **Music Videos** libraries. It works out each video's artist, title and year from its folder and file name, matches the artist to your **Music** library so the video links to the right artist, and writes the result into the video file and an `.nfo` next to it.

Requires Jellyfin **12.4** or newer.

## What it does

- **Artist from the folder.** The first folder under the library root is the primary artist:

  ```
  Music Videos/
    Drake/
      Drake - Hotline Bling (Official Video) [uxpDa-c-4Mc].mp4
      Drake x Future - Jumpman.webm
  ```

- **Artist spelling from your Music library.** The artist is matched to your Music library ignoring case, accents, punctuation and a leading "The" (`Beyonce` → `Beyoncé`, `DrakeVEVO` → `Drake`), and the video gets the library's exact spelling. That exact name is what makes Jellyfin link the video to the artist. Artists not in your Music library are checked against audiometa.io and MusicBrainz for spelling, and otherwise keep the folder name.
- **Title from the file name**, with downloader leftovers removed: YouTube IDs, `(Official Music Video)`, `[4K Remastered]`, `(Lyrics)`, `| Official Video`, full-width characters yt-dlp puts in place of `| : / ?`, and the artist's own name. Useful parts like `(Live)`, `(Remix)` or `(Acoustic)` are kept.
- **Featured artists** from `feat.`, `ft.`, `(with …)`, `A x B` and `A, B` are added as extra artists, so the video also shows up under them.
- **Year, album and genre** from the matching song in your Music library first, then audiometa.io, then MusicBrainz. A year in the file name, like `(2015)`, wins over all of them.
- **Artist folders get the artist's photo**: from the Music library artist first, then audiometa.io, then TheAudioDB. Folders that already have an image are left alone. 
- **Videos get the album or single cover** when one is found (Music library first, then audiometa.io); otherwise they keep their own thumbnail. Using the artist photo for videos without a cover is an option.
- **Writes tags into the file** (title, artist, album artist, year, album, genre) with Jellyfin's ffmpeg. Streams are copied, not re-encoded. The original file is replaced and **no backup is kept**. Works on mp4, m4v, mov, mkv and webm.
- **Writes an `.nfo`** next to each video (`<musicvideo>` with title, artists, album, year, genres). Existing `.nfo` files are updated in place, so other fields are kept.

## Install

1. In Jellyfin, go to **Dashboard → Plugins → Repositories → +** and add:

   ```
   https://raw.githubusercontent.com/jshcrtmcd/jellyfin-musicvideometa/main/manifest.json
   ```

2. Go to **Catalog**, install **Music Video Metadata**, and restart Jellyfin.
3. Open **Plugins → Music Video Metadata** to check the settings.

Jellyfin needs **write access** to the music video files and folders. Retagged videos and updated `.nfo` files are overwritten in place, so they keep their owner, group and permissions. New `.nfo` files get the video's permissions, and its owner too when Jellyfin is allowed to change owners (for example when it runs as root); otherwise they're owned by the user Jellyfin runs as.

## Running it

- **After each library scan**, new and changed videos are processed automatically (can be turned off).
- **Dashboard → Scheduled Tasks → Music Video Metadata**:
  - **Fix new music videos**: processes everything not done yet.
  - **Redo all music videos**: processes every video again, for example after renaming folders or adding artists to the Music library.

Each video is logged with the artists, title, year and where the data came from, and flagged if its artist isn't in the Music library. Turn on **Dry run** first to see what would change without touching anything.

Videos are remembered by path, size and modified time, so a scan only touches new or changed files. Locked items are skipped.

### Library settings

For **Identify** and normal refreshes, enable it on the Music Videos library under **Dashboard → Libraries → (Music Videos) → Manage library**:

- **Metadata downloaders (Music videos)**: check **Music Video Metadata** and move it to the top.
- **Image fetchers (Music videos)** and **(Folders)**: check **Music Video Metadata** and move it to the top.

## Settings

| Setting | Default | |
|---|---|---|
| Write tags into the video files | On | No backup is kept. |
| Write .nfo files | On | |
| Use the artist's photo for artist folders | On | |
| Use the album or single cover for videos | On | |
| Also use the artist's photo for every video | Off | Only for videos without a cover. |
| Process new videos after each library scan | On | |
| Dry run | Off | Only logs what would change. |
| audiometa.io API key | empty | Empty uses the key saved in the [Audiometa plugin](https://github.com/jshcrtmcd/audiometa-jellyfin-plugin). |
| Use MusicBrainz | On | No key needed; one request per second. |
| Use TheAudioDB | On | For artist photos. |
| TheAudioDB API key | `123` | TheAudioDB's free public key. |
| Cache hours | 24 | How long online lookups stay in memory. |

## Building

Requires the .NET 10 SDK.

```
dotnet build Jellyfin.Plugin.MusicVideoMeta/Jellyfin.Plugin.MusicVideoMeta.csproj -c Release
dotnet test tests/Jellyfin.Plugin.MusicVideoMeta.Tests/Jellyfin.Plugin.MusicVideoMeta.Tests.csproj
```

Copy `Jellyfin.Plugin.MusicVideoMeta.dll` into a folder under your server's `plugins` directory to install by hand.

Releases: run the **Release** workflow from the Actions tab and enter a version (or push a tag like `v1.0.1`). It builds the plugin, attaches the zip to a GitHub release and adds the version to `manifest.json`.

## License

GPL-3.0
