# Creator Toolkit for Slime Rancher 2

An in-game creator, exploration, and quality-of-life menu mod for **Slime Rancher 2** built on **MelonLoader**. Includes a live world atlas (treasure pods, gordos, drones, resources), minimap and compass, custom waypoints, an object/favorite-food radar, farm helpers, a full spawner (including Radiant slimes and Gordos), time & weather controls, unlock toggles, photo mode with FOV/smooth zoom, an FPS counter & background limiter, smooth slow-motion physics, and optional action hotkeys.

**Author:** [slitvin12](https://github.com/slitvin12) · **Version:** 0.9.3 · Unofficial fan mod, not affiliated with Monomi Park.

> **No text commands to memorize:** Creator Toolkit is controlled entirely through a visual point-and-click in-game menu opened with **Insert** (rebindable under **Settings → Menu key**). The interface automatically follows your in-game language across all **9 supported languages** (`EN`, `RU`, `FR`, `DE`, `ES`, `ZH`, `JA`, `KO`, `PT`) with CJK font support.

---

## Requirements

- **Slime Rancher 2 `1.3.0`** (Windows)
- **[MelonLoader](https://github.com/LavaGang/MelonLoader) `0.7.x`** (tested on `0.7.3`)

## Installation

1. Install MelonLoader into your Slime Rancher 2 folder and launch the game once so `Mods\` and `UserData\` are generated.
2. Download `CreatorToolkit.dll` from [Releases](https://github.com/slitvin12/SR2-CreatorToolkit/releases/latest).
3. Place `CreatorToolkit.dll` into `<game folder>\Mods\`.
4. Load any save and press **Insert** (**Fn + Insert** on some laptops). Press **Esc** to close the menu, or rebind the key in **Settings → Menu key**.

Settings and custom waypoints are stored per save / mod profile under `<game folder>\UserData\CreatorToolkit\`. Actions that permanently change save progression (unlocks, atlas discoveries, reducing Newbucks, etc.) ask for confirmation first — backing up your save beforehand is always recommended.

---

## Quick Start & Spawning Recipes

Because everything uses the visual UI rather than chat/console commands, here is how to do the most common tasks right away:

### 1. Respawn Tangle Slimes (or any regular Slime / Largo / Food / Plort)

1. Load your save and press **Insert** to open Creator Toolkit.
2. Open the **Spawn** tab.
3. Keep **Slimes** selected in the left category list (or use the search bar at the top and type `Tangle`).
4. Click **Tangle Slime** (or any Tangle Largo in the list).
5. Choose **Amount** (`1–25` via the slider or `1 / 5 / 10 / 25` quick buttons) on the right panel.
6. Click **Spawn in front of the camera** (~2.5 m ahead), or click **Pick a spot…** to place directly with your cursor:
   - Green ring = valid ground surface
   - Mouse wheel or `Q` / `E` = rotate facing (`Hold Shift` for fine rotation, `R` = face toward you)
   - `Left Click` = spawn (`Right Click` = exit placement mode)

### 2. Spawn Pure Radiant Slimes (Vanilla + Modded)

1. Open **Spawn** → **Slimes**.
2. Click the **Radiant** filter chip at the top of the list (or type `radiant` in the search box).
3. Select any of the **22** native Radiant slimes (`Pink` through `Hyper`) or any supported modded pure Radiant slime with `RadiantBase` (such as `Mosaic`, `Rad`, `Glitch`, and `Quicksilver` when **[Classic Slimes](https://github.com/slitvin12/ClassicSlimes) 1.3.9+** is installed).
4. Set your count (`1–25`) and click **Spawn in front of the camera** or **Pick a spot…**.

> *Note:* To keep native save data clean, Radiant slimes cannot be vacuumed into the vacpack directly (`SlimeRadiant` state restriction in-game), and the **Radiant** filter spawns pure base species rather than pre-built Radiant Largos.

### 3. Create Any Radiant Largo (2-Step Recipe)

Since the Radiant filter spawns pure Radiant species cleanly, you can turn any of them into a **Radiant Largo** in seconds using real game fusion:

1. In **Spawn → Slimes → Radiant**, spawn the first parent as a pure Radiant slime (for example, **Radiant · Tangle Slime**).
2. Switch to **Spawn → Plorts**, select the second parent's plort (for example, **Honey Plort** or **Mosaic Plort**), and click **Spawn in front of the camera**.
3. The Radiant slime eats the plort and natively becomes a **Radiant Largo** (e.g., Radiant Tangle-Honey Largo) while keeping its shimmering Radiant look.

### 4. Place Gordos & Clean Up Spawned Objects

- **Gordos:** Open **Spawn → Gordo**, pick a Gordo species, and click **Place Gordo** (spawns on a native snare 9 m ahead) or **Pick a spot…**.
- **Cleanup:** Each catalog tile has **Mine ×** (removes only actors spawned by the mod from that tile) and **All ×** (double-click to confirm removing all matching actors in the world). The right panel also includes **Clear spawned (N)** for mod-owned actors and double-click **Remove all chickens**.

---

## Toolset Overview (9 Tabs)

| Tab | What it does |
|---|---|
| **Player** | **God mode**, **Infinite energy**, movement speed (`×1–10`), jump height (`×1–5`), **Flight** / NoClip, **Infinite builder mode**, **Free placement**. |
| **Spawn** | Searchable Slimepedia-ordered catalog (**Slimes** with **All** / **Radiant** filter, **Food**, **Plorts**, **Resources**, **Gordo**, and **Gadgets**), `1–25` spawn count, **Spawn in front of the camera** / **Pick a spot…**, per-tile and bulk cleanup, plus gadget management with infinite/free builder toggles. |
| **Resources** | Manage Newbucks and other currencies (**Add**, **Set**, **Clear**, **Maximum**) and fill the currently selected vacpack slot. |
| **World** | Time skip & freeze time, trigger current-zone weather (including slime rain), disable Tarr spawns, and freeze slimes in place. |
| **Unlocks** | Unlock Slimepedia entries, upgrades, doors, map zones, slime styles, and gadget blueprints. Includes **World tracking** (collected / remaining / unvisited counts, treasure pod contents, compass direction, and distance). |
| **Capture** | **Clean shot** (hide HUD & vacpack), up to 5 saved camera teleport slots, **Camera & capture…** (FOV `30–120°`, **Smooth zoom**, **Smooth physics**, **FPS counter**, background FPS limit), and **Time speed…** (`×0.05–×5`, **Pin the speed** across Esc/pause/loads, smooth slow-motion physics). |
| **Map** | **Atlas**, **My markers**, **Radar**, and **Navigation** (details below). |
| **Utility** | One-click **Explorer** and **Shot** presets, **SOS** unstuck teleport, nearest unopened pod finder, ammo auto-refill, **Info HUD**, ranch/farm auto-feeder helper, and custom save names. |
| **Settings** | Rebindable **Menu key**, optional **Hotkeys** for 6 quick actions (**Flight / NoClip**, **God mode**, **Infinite energy**, **Info HUD**, **FPS counter**, **Clean shot**), **Smooth zoom**, **FPS counter**, **Optimization / Potato Mode**, and **Console / Info** (diagnostic status, active hooks, font/icon info — informational panel, not a CLI prompt). |

### Map Tools (`Map` Tab & Native Map Overlay)

- **Atlas:** Searchable list with **Nearest** button and 7 layer groups (**Treasures / Pods** with listed rewards, Gordos, teleporters, map nodes, research & phantom drones with routes, resources, doors/gadgets, pots, spawners) showing per-save completion progress. The same atlas panel and **Map layers** presets appear directly on the native in-game map (with an option to hide icons under unexplored fog).
- **My markers:** Up to 32 custom waypoints per save, 6 colors, sort by distance/name/date, world beacon, and compass target — saved separately from `.sav` files.
- **Radar:** Track any category (slimes, largos, gordos, food, plorts, resources) within `25–250 m`, including a favorite-food button for corralled slimes, with world highlights, compass pips, and minimap markers.
- **Navigation:** Live minimap preview and settings (mode `Off / Terrain / Radar`, size `S/M/L`, range `30–300 m`, screen corner, opacity, heading-up or north-up) plus compass controls (`50–1000 m`, `1–16` markers, phantom drone priority). Saved in `UserData\CreatorToolkit\navigation.cfg`.

*Minimap, compass, and farm automation are off by default.*

---

## Standalone Sibling Mods (`SlimeMap` & `SlimeFPS`)

If you only want the minimap/atlas or graphics optimization without the full creator menu, both modules are also released as standalone mods built from this repository:

- **[SlimeMap](https://github.com/slitvin12/SlimeMap)** ([Download](https://github.com/slitvin12/SlimeMap/releases/latest)) — Standalone minimap, compass, radar, custom waypoints, and atlas overlay (**F7**, config in `UserData\SlimeMap\navigation.cfg`).
- **[SlimeFPS](https://github.com/slitvin12/SlimeFPS)** ([Download](https://github.com/slitvin12/SlimeFPS/releases/latest)) — Graphics optimization presets, LOD/decor/shadow controls, runtime FPS limiter, smooth physics, and FPS counter (**F8**, config in `UserData\SlimeFPS`).

When `SlimeMap.dll` or `SlimeFPS.dll` is present in `Mods\`, Creator Toolkit detects it automatically and hands over navigation / FPS optimization without duplicate HUD overlays or double counters.

---

## Mod Compatibility

- **Lycoreia 2.x (and Ranching Together / SR2MP):** Optional integration via the official public Lycoreia SDK (`LycoreiaApi` v1 / `LycoreiaClient.cs`, no `Lycoreia.dll` dependency, stable across Lycoreia updates), with reflection fallback for older pre-SDK Lycoreia 2.x builds and Ranching Together (`SR2MP`). Friends without Creator Toolkit can play normally. Displays teammates on the minimap with view arrows and **StreamerMode** support, automatically yields menu/zoom/hotkeys to Lycoreia overlays, enforces host `Allow Cheats` rules, keeps background FPS >= 30 during co-op sessions, publishes spawned actors via `PublishSpawn`, records cheat actions via `BeginCheat`, and stores guest waypoints per host session key.
- **Starlight (ex-SR2E):** Mutually exclusive menus, adds a `Creator Toolkit` button in the pause menu, and yields Flight when Starlight NoClip is active.
- **[Classic Slimes](https://github.com/slitvin12/ClassicSlimes) (`1.3.5+` / `1.3.9+`):** Classic Gordos (`Rad`, `Quantum`, `Mosaic`) appear with their icons on the atlas, minimap, and tracker; Largo compass markers fall back to parent icons; particle optimization preserves classic effects; and in **Creator Toolkit 0.9.2+**, all modded non-largo Radiant slimes (Mosaic, Rad, Glitch, Quicksilver) are dynamically available in **Spawn → Slimes → Radiant**.
- **[SlimeMap](https://github.com/slitvin12/SlimeMap) (`1.1.1+`) & [SlimeFPS](https://github.com/slitvin12/SlimeFPS) (`1.1.1+`):** Automatic clean handoff when installed together.

---

## Release Highlights

### `0.9.3`
- **Official Lycoreia SDK v1 integration:** Integrated `LycoreiaClient.cs` (`LycoreiaSdk`) with zero compile-time dependency on `Lycoreia.dll`, binding the stable public `LycoreiaApi` v1 contract (`Role`, `CheatsAllowed` / `CheatsDeniedReason`, `TypingOpen`, `OverlayOpen`, `Players` with StreamerMode, `SessionKey`, `PublishSpawn`, `BeginCheat`, `Register`, and live `RoleChanged` / `CheatsAllowedChanged` events) while keeping reflection fallback for older pre-SDK Lycoreia builds and `SR2MP`.

### `0.9.2`
- **Dynamic modded Radiant slime catalog:** **Spawn → Slimes → Radiant** and Agent Director (`/spawn/list?filter=radiant`, `/spawn`) dynamically discover registered non-largo Radiant `SlimeDefinition` types with `RadiantBase` (including **Mosaic**, **Rad**, **Glitch**, and **Quicksilver** from Classic Slimes `1.3.9+`), using `RadiantBase._icon` artwork and distinct `SlimeDefinition.<Name>|radiant` catalog keys.

### `0.9.1`
- **Lycoreia `2.0 / 2.1.0-rc4` co-op polish:** Minimap teammate markers with StreamerMode, full UI overlay/input yielding, host `Allow Cheats` gating, per-host guest waypoints, 30 FPS co-op background floor, `VacuumExpelHook` safety, and Classic Slimes `1.3.5+` integration.

### `0.9.0`
- **9-language localization & CJK typography:** Complete built-in translations (`EN`, `RU`, `FR`, `DE`, `ES`, `ZH`, `JA`, `KO`, `PT` — 2,179 lines each) with live language switching and dynamic CJK font loading.

### `0.8.5 – 0.8.8`
- Seamless single-surface UI widgets & IL2CPP nine-slice scaling fixes, **Pick a spot…** cursor placement with rotation, **Time speed…** (`×0.05–×5`) with **Pin the speed** & smooth physics, **World tracking**, Newbucks **Set / Clear**, customizable **Menu key** & action **Hotkeys**, **My markers**, **Radar**, **Atlas**, and live **Navigation** minimap preview.

### Credits / Inspirations
Several quality-of-life ideas from `0.7.0` were implemented from scratch following mods by **Damiano** (Smooth Zoom, FPS Counter, Background FPS Limiter, Name Your Save), **huoyan1231** (Fov Changer), **Brynzananas** (Interpolate All Rigid Bodies), and **Lunarsnail0** (Placement Improvements). If you use any of those standalone mods, avoid enabling the duplicate toggle inside Creator Toolkit.

---

## Building from Source

Requires Windows, PowerShell 5.1, and .NET 8 SDK (targeting `net6.0`). Build scripts expect `dotnet` in `_work\dotnet` and game assemblies in `refs\` (both gitignored).

```powershell
# 1. Local .NET 8 SDK
New-Item -ItemType Directory _work -Force | Out-Null
Invoke-WebRequest https://dot.net/v1/dotnet-install.ps1 -OutFile _work\dotnet-install.ps1
powershell -ExecutionPolicy Bypass -File _work\dotnet-install.ps1 -Channel 8.0 -InstallDir _work\dotnet

# 2. Copy game & MelonLoader assemblies from your installation (after launching MelonLoader once)
$game = 'D:\SteamLibrary\steamapps\common\Slime Rancher 2'
New-Item -ItemType Directory refs -Force | Out-Null
Copy-Item "$game\MelonLoader\Il2CppAssemblies\*.dll" refs\
Copy-Item "$game\MelonLoader\net6\MelonLoader.dll", "$game\MelonLoader\net6\0Harmony.dll", "$game\MelonLoader\net6\Il2CppInterop.Common.dll", "$game\MelonLoader\net6\Il2CppInterop.HarmonySupport.dll", "$game\MelonLoader\net6\Il2CppInterop.Runtime.dll" refs\

# 3. Build -> _work\creator-stage\CreatorToolkit.dll
powershell -ExecutionPolicy Bypass -File src\creator\build.ps1

# 4. Run offline policy & regression tests (no game launch required)
powershell -ExecutionPolicy Bypass -File src\creator\test.ps1

# 5. Install verified DLL (close the game first)
$env:SR2_GAME_DIR = $game
powershell -ExecutionPolicy Bypass -File src\creator\install.ps1
```

`install.ps1` only copies the DLL if `test.ps1` passes for that exact build. Live in-game verification steps are documented in [`tools/ct-test/TESTING.md`](tools/ct-test/TESTING.md).

## Repository Structure

- `src/creator/` — main mod source (C# / IL2CPP via Il2CppInterop), localization tables, and embedded atlas/database assets.
- `src/slimemap/`, `src/slimefps/` — standalone SlimeMap and SlimeFPS builds sharing the same source modules ([SlimeMap](https://github.com/slitvin12/SlimeMap), [SlimeFPS](https://github.com/slitvin12/SlimeFPS)).
- `src/creator/tests/` — offline policy & static-wiring regression suite.
- `src/agentdirector/` & `tools/ct-test/` — optional automation bridge and live test harness.

## License

Mod source code is licensed under the [MIT](LICENSE) license. Third-party assets and data remain under their respective terms (see [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md)):

- Slime Rancher 2, its names, and graphics belong to Monomi Park. No game binaries or proprietary game assets are distributed in this repository.
- `src/creator/FandomAtlas.rgba` — wiki map icons from [ru.slimerancher.fandom.com](https://ru.slimerancher.fandom.com) (original game graphics).
- `src/creator/FandomMarkers.tsv` — marker coordinates and notes partially based on Fandom wiki data ([CC BY-SA 3.0](https://creativecommons.org/licenses/by-sa/3.0/)).
