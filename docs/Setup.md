# Building from source

Everything that is ours is in this repository. Two Asset Store packages aren't, because their licence
doesn't allow sharing them (see [THIRD_PARTY_NOTICES.md](../THIRD_PARTY_NOTICES.md)). This page explains
how to add them and which small changes the app needs in MapMagic.

## 1. Requirements

- **Unity 6.3 LTS – exactly `6000.3.24f1`** (Unity Hub → Installs). Newer 6.x versions change APIs the
  project relies on. Add the modules you need: *Mac Build Support (IL2CPP)*, *iOS Build Support*,
  *Android Build Support* (with OpenJDK, SDK & NDK).
- macOS for the Mac app and iOS builds; Xcode for iPad builds.
- Optional: a Bluetooth treadmill (FitShow or FTMS) and a Bluetooth heart-rate strap. Without them you
  run with the keyboard (↑/↓) or the on-screen − / + buttons, or with the built-in simulator (`-beltsim`).

## 2. Add the Asset Store packages

Two packages from the Unity Asset Store, both added to your account in the browser or in Unity's
*Package Manager → My Assets*:

| Package | Publisher | Folder it creates | Used for |
|---|---|---|---|
| [**MapMagic 2**](https://assetstore.unity.com/packages/tools/terrain/mapmagic-2-165180) (built with 2.1.19/2.1.20) | Denis Pahunov | `Assets/MapMagic/` | the endless terrain, its graph, birch/pine/stone models |
| [**Idyllic Fantasy Nature**](https://assetstore.unity.com/packages/3d/environments/fantasy/idyllic-fantasy-nature-260042) (1.0) | Edenity | `Assets/Idyllic Fantasy Nature/` | trees, bushes, plants, rocks, terrain layers |

The project doesn't compile until MapMagic is in it, so the import needs one extra click:

1. Open the project in Unity 6000.3.24f1. It reports compile errors and offers **Safe Mode** – choose
   **Ignore**.
2. *Package Manager → My Assets* → **MapMagic 2** → *Download* → *Import* (everything), then the same for
   **Idyllic Fantasy Nature**. Once MapMagic is in, the errors are gone.

Or, without opening Unity (Unity's batch mode refuses to import into a project with compile errors): download
both packages once in the Package Manager, then unpack them straight from Unity's download cache:

```bash
Tools/unpack-unitypackage.py ~/Library/Unity/Asset\ Store-5.x/Denis\ Pahunov/Editor\ ExtensionsTerrain/MapMagic\ 2.unitypackage
```

```bash
Tools/unpack-unitypackage.py ~/Library/Unity/Asset\ Store-5.x/Edenity/3D\ ModelsEnvironmentsFantasy/Idyllic\ Fantasy\ Nature.unitypackage
```

The scene refers to MapMagic and the nature assets by their GUIDs, so once the packages are imported
unchanged, trees, rocks, terrain layers and the terrain graph connect by themselves. The seasonal tree
textures (spring, autumn, winter) are recoloured from these trees by the first build
(`Editor/SeasonBuilder`), so they aren't in the repository either.

**That's all you need** – the app builds and runs with the unmodified packages (checked: the full
end-to-end test passes, 111 of 111). In the log it says once *"MapMagic ohne die Anpassungen der App"*.

## 2b. Optional: the companion dog

The runner's dog (Profil → Begleithund) is the free "German Shepherd 3D Dog Model" by RetroStyle Games on Fab
(<https://www.fab.com/listings/5ffcabde-3356-4d75-b98e-580825f15e47>). Its licence doesn't allow sharing the
files, so it isn't in this repository; without it the app just has no dog. To add it: download the FBX zip on
Fab, then

```bash
Tools/dogs/import-germanshepherd.sh ~/Downloads/rsg_dogspack_germanshepherd_fbx.zip
```

and in Unity **Jogging → Build → Hunde aufbereiten** (in-place clips, URP material, real size, prefab in
`Assets/PhotoReal/Dogs/Resources/Dogs/`).

## 3. Optional: the app's changes inside MapMagic

The original project runs MapMagic with a few changes that make it faster and smoother, especially on
tablets. Their code belongs to MapMagic, so it isn't shared here; the list below says what each one does,
so you can make it yourself. The app finds them at runtime (`World/MapMagicExt.cs`) and uses them when they
are there. In the original project each change is marked with a `// Jogging` comment.

| File (in `Assets/MapMagic/`) | Change | Effect |
|---|---|---|
| `Tools/TileManager.cs` | a small static class `Den.Tools.TileDiag` (per-frame timing of the tile grid, a `PrewarmAllowed` flag); spare tiles are built ahead in quiet frames instead of all at once when the grid moves | no hitch at tile borders |
| `Core/MapMagicObject.cs` | spare tiles get the same terrain settings as the grid; a new MapMagic starts with prewarming off until the app allows it | memory on tablets |
| `Terrains/TerrainTileManager.cs` | a spare tile stays hidden until it is deployed | no flicker |
| `Terrains/TerrainTile.cs` | new tiles start with a small base map/heightmap until attached; a destroyed tile stops its tasks through the stop token (no `Thread.Abort`); timing hooks for `-hitch` | memory, stable scene reloads under IL2CPP |
| `Tools/ThreadManager/ThreadManager.cs` | a fixed pool of lower-priority worker threads instead of a thread per task; a static `ClearQueue()` drops queued work | smooth frame rate, clean scene changes |
| `Tools/ThreadManager/CoroutineManager.cs` | static `FrameMs`, `SlowestStepMs`, `SlowestStep`: main-thread time of the queue per frame | `-hitch` diagnostics |
| `Tools/Erosion.cs` | allocation-free versions of the managed erosion fallback (used under IL2CPP), bit-identical results, and a static `UseReference` switch to compare (`ErosionCheck`) | ~5× faster terrain on devices |
| `Generators/Matrix/Runtime/HeightOut.cs` | a `Pedestal` (metres of ground below the graph's zero) added to every height | deep cuts and lake basins along the trail keep their depth |
| `Nodes/Generator.cs`, `Nodes/Graph.cs` | node timing always recorded in a `mainTime` field (not serialized); `-diag` logs the slowest nodes | diagnostics |

## 4. Build and run

With Unity closed:

```bash
/Applications/Unity/Hub/Editor/6000.3.24f1/Unity.app/Contents/MacOS/Unity -batchmode -quit -projectPath . -buildTarget OSXUniversal -executeMethod Jogging.EditorTools.BuildTools.BuildMacBatch -logFile build.log
```

```bash
open Builds/macOS/Jogging.app --args -beltsim
```

`-beltsim` simulates a treadmill (B start/stop, ↑/↓ speed, PgUp/PgDn incline). Self-tests and the
full end-to-end run:

```bash
Tools/check.sh
```

```bash
Tools/e2e.sh
```

iPad, Android, all command-line options and the architecture are described (in German) in
[docs/Entwicklung.md](Entwicklung.md).
