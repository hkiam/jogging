#!/usr/bin/env python3
# Takes Rocketbox avatars (MIT, github.com/microsoft/Microsoft-Rocketbox) into Assets/Rocketbox/Avatars the
# way the app uses them: <Name>/<Name>.fbx (from Export/) and Textures/*_color.png, *_normal.png at 1024 px
# (the specular maps and the facial rigs are left out). Downloads straight from the library.
#   python3 Tools/rocketbox/import-avatars.py Professions/Sports_Female_02 Professions/Sports_Male_04 …
import io, json, os, sys, urllib.request
from PIL import Image

LIB = "https://raw.githubusercontent.com/microsoft/Microsoft-Rocketbox/master/Assets/Avatars/"
API = "https://api.github.com/repos/microsoft/Microsoft-Rocketbox/contents/Assets/Avatars/"
OUT = os.path.join(os.path.dirname(__file__), "..", "..", "Assets", "Rocketbox", "Avatars")

for arg in sys.argv[1:]:
    name = arg.split("/")[-1]
    dst = os.path.join(OUT, name); os.makedirs(os.path.join(dst, "Textures"), exist_ok=True)
    open(os.path.join(dst, name + ".fbx"), "wb").write(urllib.request.urlopen(f"{LIB}{arg}/Export/{name}.fbx").read())
    for f in json.load(urllib.request.urlopen(f"{API}{arg}/Textures")):
        n = f["name"]
        if not n.endswith(".tga") or "specular" in n: continue
        img = Image.open(io.BytesIO(urllib.request.urlopen(f["download_url"]).read()))
        img = img.convert("RGBA" if "opacity" in n else "RGB").resize((1024, 1024), Image.LANCZOS)
        img.save(os.path.join(dst, "Textures", n[:-4] + ".png"))
    print(name, "→", sorted(os.listdir(os.path.join(dst, "Textures"))))
