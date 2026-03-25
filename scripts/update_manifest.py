#!/usr/bin/env python3
"""
update_manifest.py — Updates manifest.json with the latest release.

Called by the GitHub Actions workflow after a build succeeds.
Prepends the new version to the versions list (newest first).
"""

import argparse
import json
import sys
from datetime import datetime, timezone
from pathlib import Path

MANIFEST_PATH = Path(__file__).parent.parent / "manifest.json"

BASE_ENTRY = {
    "guid":        "a4b2c3d4-e5f6-7890-abcd-ef1234567891",
    "name":        "Pre-Roll Videos",
    "description": (
        "Plays videos from a selected library before TV episodes or movies — "
        "perfect for studio logos, custom commercials, or broadcast intros."
    ),
    "overview": (
        "Choose any Jellyfin library as your pre-roll source. Videos from that library "
        "will automatically play before TV episodes and/or movies. Supports random order, "
        "multiple clips per play, and per-content-type toggles."
    ),
    "owner":    "",          # filled in by workflow
    "category": "General",
    "imageUrl": "",
    "versions": []
}


def main():
    parser = argparse.ArgumentParser(description="Update Jellyfin plugin manifest")
    parser.add_argument("--version",   required=True, help="Plugin version, e.g. 1.0.0.0")
    parser.add_argument("--url",       required=True, help="Download URL for the plugin zip")
    parser.add_argument("--checksum",  required=True, help="MD5 checksum of the zip")
    parser.add_argument("--repo",      required=True, help="GitHub repo in owner/name format")
    parser.add_argument("--abi", default="10.11.0.0", help="Target Jellyfin ABI version")
    args = parser.parse_args()

    # Load existing manifest or start fresh
    if MANIFEST_PATH.exists():
        try:
            manifest = json.loads(MANIFEST_PATH.read_text())
        except json.JSONDecodeError as exc:
            print(f"WARNING: Could not parse existing manifest ({exc}), starting fresh.")
            manifest = [dict(BASE_ENTRY)]
    else:
        manifest = [dict(BASE_ENTRY)]

    entry = manifest[0]
    entry["owner"] = args.repo.split("/")[0]

    new_version = {
        "version":   args.version,
        "changelog": f"https://github.com/{args.repo}/releases/tag/v{args.version}",
        "targetAbi": args.abi,
        "sourceUrl": args.url,
        "checksum":  args.checksum,
        "timestamp": datetime.now(tz=timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ"),
    }

    # Replace existing entry for this version, or prepend
    entry["versions"] = [v for v in entry.get("versions", []) if v["version"] != args.version]
    entry["versions"].insert(0, new_version)

    MANIFEST_PATH.write_text(json.dumps(manifest, indent=2) + "\n")
    print(f"manifest.json updated — {len(entry['versions'])} version(s) listed.")
    print(f"  Latest: {new_version['version']} → {new_version['sourceUrl']}")


if __name__ == "__main__":
    sys.exit(main())
