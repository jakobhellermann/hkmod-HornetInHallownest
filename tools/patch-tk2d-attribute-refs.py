#!/usr/bin/env python3
"""Retarget tk2d type names inside custom-attribute blobs from TeamCherry.TK2D to Assembly-CSharp (HK 1.5.78).

UnityDowngrader rewrites tk2d TypeRefs to the host's Assembly-CSharp, but types passed to attributes
(e.g. [RequireComponent(typeof(tk2dSprite))]) are stored as assembly-qualified strings in the #Blob heap, which it
doesn't touch. Mono then throws "Could not load type tk2dSprite, TeamCherry.TK2D ... while decoding custom attribute".

Only occurrences followed by ", Version=" (i.e. assembly-qualified names) are rewritten. Both names are 15 bytes long,
so blob lengths and heap offsets stay valid. Idempotent.

usage: patch-tk2d-attribute-refs.py <assembly.dll>...
"""
import sys

OLD = b", TeamCherry.TK2D, Version="
NEW = b", Assembly-CSharp, Version="
assert len(OLD) == len(NEW)

for path in sys.argv[1:]:
    with open(path, "rb") as f:
        data = f.read()
    count = data.count(OLD)
    if count:
        with open(path, "wb") as f:
            f.write(data.replace(OLD, NEW))
    print(f"{path}: {count} attribute reference(s) retargeted")
