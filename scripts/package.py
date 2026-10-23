#!/usr/bin/env python3
"""Bundle Windows binaries with their license notices and hash all deliverables."""
import hashlib
from pathlib import Path
import zipfile

root = Path(__file__).resolve().parent.parent
artifacts = root / "build/artifacts"
notices = [root / "LICENSE", root / "THIRD_PARTY_NOTICES.md", *sorted((root / "docs/licenses").glob("*.txt"))]
for arch in ("amd64", "arm64"):
    exe = artifacts / f"tethermax-windows-{arch}.exe"
    if not exe.exists():
        raise RuntimeError(f"Build missing: {exe.name}")
    with zipfile.ZipFile(artifacts / f"tethermax-windows-{arch}.zip", "w", compression=zipfile.ZIP_DEFLATED) as z:
        z.write(exe, exe.name)
        for file in notices:
            z.write(file, file.relative_to(root))
with (artifacts / "SHA256SUMS.txt").open("w") as out:
    for file in sorted(artifacts.iterdir()):
        if file.suffix in (".apk", ".zip", ".exe", ".msi"):
            out.write(f"{hashlib.sha256(file.read_bytes()).hexdigest()}  {file.name}\n")
