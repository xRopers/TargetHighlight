"""Writes repo.json (the Dalamud custom repository feed) from the manifest DalamudPackager produced.

Usage: python scripts/update_repo_json.py <tag>    e.g. v0.2.0
Run after a Release build; reads bin/x64/Release/TargetHighlight/TargetHighlight.json.
"""
import json
import sys
import time
from pathlib import Path

REPO = "xRopers/TargetHighlight"
ROOT = Path(__file__).resolve().parent.parent
MANIFEST = ROOT / "bin" / "x64" / "Release" / "TargetHighlight" / "TargetHighlight.json"
REPO_JSON = ROOT / "repo.json"
ICON_URL = f"https://raw.githubusercontent.com/{REPO}/main/images/icon.png"

# Manifest keys copied as-is into the store entry.
KEYS = [
    "Author", "Name", "InternalName", "AssemblyVersion", "Description", "ApplicableVersion",
    "RepoUrl", "IconUrl", "Tags", "DalamudApiLevel", "Punchline", "LoadPriority", "AcceptsFeedback",
]


def main() -> None:
    if len(sys.argv) != 2:
        sys.exit(__doc__)

    tag = sys.argv[1]
    manifest = json.loads(MANIFEST.read_text(encoding="utf-8-sig"))
    download = f"https://github.com/{REPO}/releases/download/{tag}/latest.zip"

    entry = {key: manifest[key] for key in KEYS if key in manifest}
    entry.setdefault("IconUrl", ICON_URL)
    entry.update({
        "TestingAssemblyVersion": None,
        "IsHide": False,
        "IsTestingExclusive": False,
        "DownloadLinkInstall": download,
        "DownloadLinkUpdate": download,
        "DownloadLinkTesting": download,
        "LastUpdate": str(int(time.time())),
    })

    REPO_JSON.write_text(json.dumps([entry], indent=2) + "\n", encoding="utf-8", newline="\n")
    print(f"Wrote {REPO_JSON} for {manifest['InternalName']} {manifest['AssemblyVersion']} ({tag})")


if __name__ == "__main__":
    main()
