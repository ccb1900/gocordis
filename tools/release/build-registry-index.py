#!/usr/bin/env python3
"""Build the read-only plugin registry index for a directory of packages."""

import argparse
import hashlib
import json
import zipfile
from pathlib import Path
from urllib.parse import quote


def build(packages: Path) -> list[dict[str, str]]:
    entries: list[dict[str, str]] = []
    for path in sorted(packages.glob("*.gocordis-plugin")):
        with zipfile.ZipFile(path) as archive:
            manifest = json.loads(archive.read("manifest.json"))
        entries.append(
            {
                "id": manifest["Id"],
                "version": manifest["Version"],
                "url": "/packages/" + quote(path.name),
                "sha256": hashlib.sha256(path.read_bytes()).hexdigest(),
            }
        )
    return entries


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("packages", type=Path)
    parser.add_argument("output", type=Path)
    args = parser.parse_args()
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(
        json.dumps(build(args.packages), indent=2, ensure_ascii=False) + "\n",
        encoding="utf-8",
    )


if __name__ == "__main__":
    main()
