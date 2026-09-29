#!/usr/bin/env python3
"""Unpacks a .unitypackage into a Unity project – the same files and GUIDs as Unity's own import, without
opening Unity. Useful here because the project doesn't compile until MapMagic 2 is in it, and Unity's batch
mode refuses to import into a project with compile errors (docs/Setup.md).
  Tools/unpack-unitypackage.py "<path>/MapMagic 2.unitypackage" [project-folder]"""
import os, sys, tarfile

pkg = sys.argv[1]
project = sys.argv[2] if len(sys.argv) > 2 else os.path.join(os.path.dirname(__file__), "..")
entries = {}
with tarfile.open(pkg, "r:gz") as tar:
    for m in tar.getmembers():
        parts = m.name.strip("./").split("/")
        if len(parts) != 2 or not m.isfile():
            continue
        guid, kind = parts
        entries.setdefault(guid, {})[kind] = tar.extractfile(m).read()
count = 0
for guid, e in entries.items():
    if "pathname" not in e:
        continue
    rel = e["pathname"].decode("utf-8").splitlines()[0].strip()
    if not rel.startswith("Assets/") or ".." in rel.split("/"):
        continue  # only into the project's Assets
    dst = os.path.join(project, rel)
    if "asset" in e:
        os.makedirs(os.path.dirname(dst), exist_ok=True)
        with open(dst, "wb") as f:
            f.write(e["asset"])
        count += 1
    else:
        os.makedirs(dst, exist_ok=True)  # a folder
    if "asset.meta" in e:
        with open(dst + ".meta", "wb") as f:
            f.write(e["asset.meta"])
print(f"{count} Dateien aus {os.path.basename(pkg)} entpackt")
