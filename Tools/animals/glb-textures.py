#!/usr/bin/env python3
# Pulls the textures and material settings out of a Sketchfab GLB for Editor/AnimalAssets:
#   Assets/PhotoReal/Animals/<name>/textures/<name>_<i>_tex.png  and  .../<name>/materials.json
# Needed because Sketchfab stores the colour as KHR_materials_pbrSpecularGlossiness, which Blender (glb2fbx.py)
# drops – the FBX would come out white.
#   python3 Tools/animals/glb-textures.py <in.glb> <name>
import json, os, shutil, struct, subprocess, sys

src, name = sys.argv[1], sys.argv[2]
out = os.path.join(os.path.dirname(__file__), "..", "..", "Assets", "PhotoReal", "Animals", name)
data = open(src, "rb").read()
jlen = struct.unpack("<I", data[12:16])[0]
j = json.loads(data[20:20 + jlen])
blob = data[20 + jlen + 8:]  # the BIN chunk after the JSON chunk
os.makedirs(os.path.join(out, "textures"), exist_ok=True)

files = []
for i, img in enumerate(j.get("images", [])):
    ext = ".jpg" if img.get("mimeType") == "image/jpeg" else ".png"
    fn = f"{name}_{i}_tex{ext}"
    bv = j["bufferViews"][img["bufferView"]]
    start = bv.get("byteOffset", 0)
    path = os.path.join(out, "textures", fn)
    open(path, "wb").write(blob[start:start + bv["byteLength"]])
    # the app uses at most 1024 px (AnimalAssets) – no need to keep more in the repo
    if shutil.which("sips"):
        info = subprocess.run(["sips", "-g", "pixelWidth", "-g", "pixelHeight", path], capture_output=True, text=True).stdout
        if max(int(v) for v in info.split()[-3::2]) > 1024:
            subprocess.run(["sips", "-Z", "1024", path], stdout=subprocess.DEVNULL, check=True)
    files.append(fn)

def tex(t):
    return files[j["textures"][t["index"]]["source"]] if t else None

mats = []
for m in j.get("materials", []):
    pbr = m.get("pbrMetallicRoughness", {})
    sg = m.get("extensions", {}).get("KHR_materials_pbrSpecularGlossiness", {})
    mats.append({
        "name": m.get("name"),
        "base": tex(pbr.get("baseColorTexture")) or tex(sg.get("diffuseTexture")),
        "normal": tex(m.get("normalTexture")),
        "alpha": m.get("alphaMode", "OPAQUE") == "MASK",
        "cutoff": m.get("alphaCutoff", 0.5),
        "gloss": sg.get("glossinessFactor", 0.2) if sg else 1.0 - pbr.get("roughnessFactor", 0.8),
    })
json.dump(mats, open(os.path.join(out, "materials.json"), "w"), indent=1)
print(name, [(m["name"], m["base"], m["normal"], m["alpha"]) for m in mats])
