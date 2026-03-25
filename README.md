# Jellyfin Preroll & Intros Plugin

Play videos from a dedicated Jellyfin library as **prerolls** before your TV shows and movies — like studio logos, channel bumpers, or custom intros. Works on **every client** (Fire TV, Roku, iOS, Android, web, etc.) because it uses Jellyfin's server-side intro injection API.

---

## Features

- **Universal client support** — intros are injected server-side, so Fire Stick, Roku, web, mobile, and every other Jellyfin client plays them automatically
- **Library-based** — point the plugin at any Jellyfin library; all videos in it become preroll candidates
- **Random selection** — plays 1–5 random prerolls before each item
- **Granular control** — enable/disable separately for TV shows and movies
- **Duration filter** — optionally skip preroll videos longer than a set number of seconds
- **Dashboard config** — everything is configured through the Jellyfin web UI

---

## Installation

### 1. Add the plugin repository to Jellyfin

1. Open your Jellyfin Dashboard
2. Go to **Administration → Plugins → Repositories**
3. Click **+** to add a new repository
4. Enter:
   - **Name:** `Preroll & Intros`
   - **URL:** `https://raw.githubusercontent.com/justhallwest-commits/jellyfin-preroll/main/manifest.json`
5. Click **Save**

### 2. Install the plugin

1. Go to **Administration → Plugins → Catalog**
2. Find **"Preroll & Intros"** in the list
3. Click **Install**
4. **Restart Jellyfin** when prompted

### 3. Create a preroll library

1. In Jellyfin, create a new library:
   - **Content type:** `Movies` or `Mixed Content`
   - **Name:** something like `Prerolls` or `Intros`
2. Add your preroll video files (studio logos, bumpers, intros, etc.) to this library's media folder
3. Let Jellyfin scan the library so the videos appear

### 4. Configure the plugin

1. Go to **Administration → Plugins → Preroll & Intros**
2. Select your preroll library from the dropdown
3. Set the number of prerolls to play (1–5)
4. Choose whether to play them before TV shows, movies, or both
5. Optionally set a max duration to skip longer videos
6. Click **Save**

That's it! The next time you play a TV show episode or movie, your preroll videos will play first.

---

## How It Works

The plugin implements Jellyfin's `IIntroProvider` interface. When any client requests playback of a TV episode or movie, the Jellyfin server asks all registered intro providers for items to prepend. This plugin responds with random video(s) from your configured preroll library.

Because this happens entirely on the server via the standard Jellyfin API, **no client modifications are needed**. Any client that supports normal Jellyfin playback will automatically play the prerolls.

---

## Building from Source

**Prerequisites:** .NET 6 SDK

```bash
git clone https://github.com/justhallwest-commits/jellyfin-preroll.git
cd jellyfin-preroll
dotnet build JellyfinPreroll/JellyfinPreroll.csproj --configuration Release
```

The compiled DLL will be in `JellyfinPreroll/bin/Release/net6.0/`.

To install manually, copy `JellyfinPreroll.dll` into your Jellyfin plugins directory:
- **Linux/Unraid:** `/config/plugins/JellyfinPreroll/`
- **Docker:** `/config/data/plugins/JellyfinPreroll/`
- **Windows:** `%APPDATA%\Jellyfin\Server\plugins\JellyfinPreroll\`

Then restart Jellyfin.

---

## Releasing a New Version

The included GitHub Actions workflow handles this automatically:

1. Update the version in `JellyfinPreroll.csproj`
2. Update the version and `sourceUrl` in `manifest.json`
3. Commit and tag:
   ```bash
   git tag v1.0.0
   git push origin main --tags
   ```
4. GitHub Actions will build the DLL, package it, and create a release

---

## Tips

- **Short videos work best** — 5–30 second clips are ideal for a studio-logo feel
- **Use the duration filter** if your library has a mix of short and long videos
- **Prerolls are per-play** — each time you hit play on an episode, a fresh random selection is made
- **Hide the preroll library** from your home screen if you don't want it cluttering your dashboard (Library Settings → Display → uncheck "Show in home screen")

---

## Compatibility

| Jellyfin Version | Status |
|-----------------|--------|
| 10.8.x          | ✅ Supported |
| 10.9.x          | ✅ Should work (same API) |
| 10.10.x         | ⚠️ May need rebuild against newer SDK |

---

## License

MIT
