"""Add a release to manifest.json (Jellyfin plugin repository format).

Reads VERSION, TAG, CHECKSUM and SOURCE_URL from the environment; plugin info comes from build.yaml.
"""

import json
import os
import re
from datetime import datetime, timezone
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent


def read_build_yaml() -> dict:
    # Minimal reader for the flat keys we need, so no PyYAML install is required.
    text = (ROOT / "build.yaml").read_text(encoding="utf-8")
    out = {}
    for key in ("name", "guid", "targetAbi", "overview", "description", "category", "owner", "changelog", "imageUrl"):
        m = re.search(rf'^{key}:\s*"?(.*?)"?\s*$', text, re.MULTILINE)
        if m:
            out[key] = m.group(1)
    return out


def main() -> None:
    meta = read_build_yaml()
    version = os.environ["VERSION"]
    entry = {
        "version": version,
        "changelog": "https://github.com/jshcrtmcd/jellyfin-musicvideometa/releases/tag/" + os.environ.get("TAG", "v" + version),
        "targetAbi": meta["targetAbi"],
        "sourceUrl": os.environ["SOURCE_URL"],
        "checksum": os.environ["CHECKSUM"],
        "timestamp": datetime.now(timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ"),
    }

    path = ROOT / "manifest.json"
    manifest = json.loads(path.read_text(encoding="utf-8")) if path.exists() else []
    plugin = next((p for p in manifest if p.get("guid") == meta["guid"]), None)
    if plugin is None:
        plugin = {"versions": []}
        manifest.append(plugin)

    plugin.update(
        {
            "guid": meta["guid"],
            "name": meta["name"],
            "overview": meta["overview"],
            "description": meta.get("description", meta["overview"]),
            "owner": meta["owner"],
            "category": meta["category"],
        }
    )
    if meta.get("imageUrl"):
        plugin["imageUrl"] = meta["imageUrl"]
    plugin["versions"] = [v for v in plugin["versions"] if v["version"] != version]
    plugin["versions"].insert(0, entry)

    path.write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")


if __name__ == "__main__":
    main()
