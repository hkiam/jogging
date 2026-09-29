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

1. Open the Unity Asset Store (in the browser or Unity's *Package Manager → My Assets*) and add
   **MapMagic 2** (by Denis Pahunov, free) to your account.
2. Open this project in Unity 6000.3.24f1. It will report missing scripts and compile errors – that's
   expected until step 3 is done.
3. *Package Manager → My Assets → MapMagic 2 → Download / Import*. Import everything, including the
   **Idyllic Fantasy Nature** folder that comes with it. The folders must end up as
   `Assets/MapMagic/` and `Assets/Idyllic Fantasy Nature/`. The project was built with **MapMagic
   2.1.20**; a different version may need adjustments.

The scene keeps its references to MapMagic and the nature assets by their GUIDs, so once the package is
imported unchanged, trees, rocks, terrain layers and the terrain graph connect by themselves. The seasonal
tree textures (spring, autumn, winter) are recoloured from these trees by the first build
(`Editor/SeasonBuilder`), so they aren't in the repository either.

## 3. Apply the changes to MapMagic

The app needs a few changes inside MapMagic. Their code belongs to MapMagic, so it isn't shared here; the
list below says what each one does, so you can make it yourself. In the original project each change is
marked with a `// Jogging` comment.

| File (in `Assets/MapMagic/`) | Change | Needed for |
|---|---|---|
| `Core/MapMagicObject.cs` | `StopGenerate()` made **public**; spare tiles get the same terrain settings as the grid; a new MapMagic starts with spare-tile prewarming off (`Den.Tools.TileDiag.PrewarmAllowed = false`) | **compiles** (`SceneReload` calls `StopGenerate`), memory on tablets |
| `Tools/TileManager.cs` | a small static class `Den.Tools.TileDiag` (per-frame main-thread time, `PrewarmAllowed` flag); spare tiles are built ahead in quiet frames instead of all at once when the grid moves | **compiles**, no hitch at tile borders |
| `Terrains/TerrainTileManager.cs` | a spare tile stays hidden until it is deployed | no flicker |
| `Terrains/TerrainTile.cs` | new tiles start with a small base map/heightmap until attached; a destroyed tile stops its tasks through the stop token (no `Thread.Abort`); timing hooks for `-hitch` | memory, stable scene reloads under IL2CPP |
| `Tools/ThreadManager/ThreadManager.cs` | a fixed pool of lower-priority worker threads instead of a thread per task; `ClearQueue()` drops queued work | **compiles** (`RouteRuntime` calls `ClearQueue`), smooth frame rate |
| `Tools/ThreadManager/CoroutineManager.cs` | timing of the main-thread queue for `-hitch` | diagnostics |
| `Tools/Erosion.cs` | allocation-free versions of the managed erosion fallback (used under IL2CPP), bit-identical results (`ErosionCheck`) | ~5× faster terrain on devices |
| `Generators/Matrix/Runtime/HeightOut.cs` | a `Pedestal` (metres of ground below the graph's zero) added to every height | the trail's elevation profile can dig into the land |
| `Nodes/Generator.cs`, `Nodes/Graph.cs` | node timing always recorded (not serialized); `-diag` logs the slowest nodes | diagnostics |

If you only want to get it running: the entries marked **compiles** are needed for the project to build;
the rest improve performance and stability.

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
