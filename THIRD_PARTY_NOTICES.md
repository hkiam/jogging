# Third-party notices

The MIT licence in [LICENSE](LICENSE) covers the code, shaders, tools and documentation written for this
project. The following parts come from others and keep their own licences.

## Included in this repository

| What | Where | Licence |
|---|---|---|
| **Microsoft Rocketbox Avatar Library** – 10 adult avatars, 4 sports avatars (`Tools/rocketbox/import-avatars.py`), running-clothes variants of the textures (`Tools/rocketbox/sport-textures.py`), and run / idle / clap / cheer / wave animations (textures downscaled, specular maps dropped) | `Assets/Rocketbox/` | MIT, © Microsoft Corporation – see [`Assets/Rocketbox/LICENSE.txt`](Assets/Rocketbox/LICENSE.txt) and <https://github.com/microsoft/Microsoft-Rocketbox> |
| **Poly Haven** models and textures (tree stumps, logs, ferns, nettles, bench, lamp, …; asphalt, gravel, forest floor, bark, planks, …), 1k resolution | `Assets/PhotoReal/Wayside/` | CC0 1.0 (public domain) – <https://polyhaven.com> |
| **Hare** – “Animated Rabbit – 3D Animal Model” by [AnimalMesh 3D](https://sketchfab.com/AnimalMesh3D) (converted to FBX, scaled, materials rebuilt) | `Assets/PhotoReal/Animals/hare/` | CC BY 4.0 – see its `LICENSE.txt` |
| **Red deer, rabbit, fox** – “Realistic Deer 3D Model 2.0”, “RABBIT – Realistic 3D Model”, “FOX – Realistic 3D Model” (demo versions) by [WildMesh 3D](https://sketchfab.com/WildMesh_3D) (converted to FBX, scaled, materials rebuilt, deer colour warmed) | `Assets/PhotoReal/Animals/{deer,rabbit,fox}/` | **CC BY-NC 4.0 – non-commercial only**, see each `LICENSE.txt` |

**Note on the animals:** the WildMesh models are licensed for non-commercial use only and are therefore
*not* covered by the MIT licence. Anyone using this project commercially must remove or replace
`Assets/PhotoReal/Animals/{deer,rabbit,fox}` – the app runs without them (animals whose model is missing are simply
left out).

## Not included – get them yourself

These come from the Unity Asset Store. Their licence (the Asset Store EULA) doesn't allow redistributing
them, so they are **not** in this repository. See [docs/Setup.md](docs/Setup.md) for how to add them.

| What | Where it goes | Source |
|---|---|---|
| **MapMagic 2** (v2.1.20, by Denis Pahunov) – the infinite terrain generator | `Assets/MapMagic/` | Unity Asset Store, product 165180 |
| **Idyllic Fantasy Nature** (v1.0, by Edenity) – trees, bushes, plants, rocks, terrain layers | `Assets/Idyllic Fantasy Nature/` | Unity Asset Store, product 260042 |

The project also uses Unity packages (Universal Render Pipeline, Input System, …) that Unity resolves
from `Packages/manifest.json` under the Unity Companion License.

No affiliation with Sportstech, FitShow, Unity, Microsoft, Poly Haven or the MapMagic author. Product
names are used only to describe compatibility.
