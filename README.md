# 🎬 Jellyfin Pre-Roll Videos Plugin

Plays videos from any Jellyfin library before your TV episodes or movies —
like a broadcast network ID, studio logo, or custom commercial break.

---

## ✨ Features

| Feature | Details |
|---|---|
| **Source library** | Pick any library as the pre-roll pool |
| **Target types** | TV episodes, movies, or both |
| **Random order** | Shuffle clips on every play |
| **Multiple clips** | Queue 1–10 pre-rolls per title |
| **Admin UI** | Full config page inside the Jellyfin dashboard |

---

## 📦 Install via Repository URL (recommended)

1. Push this repo to **GitHub** and create a `v1.0.0` tag (see [Build & Release](#-build--release) below).
   GitHub Actions will build the plugin and populate `manifest.json` automatically.

2. In Jellyfin: **Dashboard → Plugins → Repositories → ＋ Add**

3. Paste your manifest URL:
   ```
   https://raw.githubusercontent.com/YOUR_USERNAME/YOUR_REPO/main/manifest.json
   ```

4. **Dashboard → Plugins → Catalog** → search **Pre-Roll Videos** → Install.

5. Restart Jellyfin when prompted.

6. Go to **Dashboard → Plugins → Pre-Roll Videos** and configure:
   - Select your pre-roll source library
   - Toggle TV shows / movies
   - Set clip count and order

---

## 🔨 Build & Release

The GitHub Actions workflow (`.github/workflows/build.yml`) handles everything.

### First-time setup

```bash
# 1. Clone or fork this repo
git clone https://github.com/YOUR_USERNAME/YOUR_REPO
cd YOUR_REPO

# 2. Push to GitHub
git remote set-url origin https://github.com/YOUR_USERNAME/YOUR_REPO
git push -u origin main

# 3. Create a version tag — this triggers the build
git tag v1.0.0
git push origin v1.0.0
```

The workflow will:
1. Build the plugin DLL with .NET 8
2. Package it as `preroll_1.0.0.0.zip`
3. Create a GitHub Release with the zip attached
4. Update `manifest.json` with the download URL and MD5 checksum
5. Commit `manifest.json` back to `main`

Your repository URL is then permanently:
```
https://raw.githubusercontent.com/YOUR_USERNAME/YOUR_REPO/main/manifest.json
```

### Releasing updates

```bash
# Bump the version and push a new tag
git tag v1.1.0
git push origin v1.1.0
```

The workflow adds the new version to `manifest.json` while keeping older
versions — Jellyfin's built-in updater will offer the upgrade automatically.

---

## 🗂️ Project structure

```
├── .github/workflows/build.yml          # CI: build → release → manifest
├── Jellyfin.Plugin.PreRoll/
│   ├── Configuration/configPage.html    # Embedded admin UI
│   ├── Jellyfin.Plugin.PreRoll.csproj
│   ├── Plugin.cs                        # BasePlugin entry point
│   ├── PluginConfiguration.cs           # Persisted settings
│   ├── PreRollIntroProvider.cs          # IIntroProvider implementation
│   └── ServiceRegistrator.cs            # DI registration
├── scripts/update_manifest.py           # Called by CI to patch manifest.json
├── manifest.json                        # Jellyfin plugin repository index
└── README.md
```

---

## 🎛️ Configuration options

| Setting | Default | Description |
|---|---|---|
| Pre-Roll Source Library | *(none)* | The library to pull clips from |
| Play before TV episodes | ✅ | Inject pre-roll before episodes |
| Play before movies | ❌ | Inject pre-roll before movies |
| Random order | ✅ | Shuffle instead of playing in order |
| Clips per play | 1 | How many clips to queue (1–10) |

---

## 🛠️ Manual build (no CI)

```bash
cd Jellyfin.Plugin.PreRoll
dotnet publish -c Release --no-self-contained -o ../out
```

Copy `out/Jellyfin.Plugin.PreRoll.dll` to your Jellyfin plugins folder, restart.

---

## 📋 Requirements

| Component | Version |
|---|---|
| Jellyfin Server | 10.9.x |
| .NET SDK (to build) | 8.0+ |

> **10.8.x users:** change `TargetFramework` to `net6.0` and the
> `Jellyfin.Controller` package version to `10.8.0` in the `.csproj`.

---

## 📄 License

MIT — do whatever you want with it.
