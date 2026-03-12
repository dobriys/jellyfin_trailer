#!/usr/bin/env python3
"""
update_manifest.py — Adds (or updates) a version entry in dist/manifest.json.

Usage:
    python3 update_manifest.py <version> <checksum_md5> <zip_url> [changelog] [timestamp]

Arguments:
    version      — plugin version, e.g. "1.0.0.0"
    checksum_md5 — MD5 hex digest of the .zip file
    zip_url      — full URL to the release .zip (GitHub Releases asset URL)
    changelog    — (optional) release notes; defaults to build.yaml changelog field
    timestamp    — (optional) ISO-8601 UTC; defaults to current time

Examples:
    # Typical CI call (timestamp auto-set to now):
    python3 update_manifest.py 1.0.0.0 abc123 https://github.com/…/v1.0.0.0/plugin.zip

    # With explicit changelog and timestamp:
    python3 update_manifest.py 1.1.0.0 def456 https://… "Bug fixes" 2026-03-12T10:00:00Z
"""

import json
import os
import sys
from datetime import datetime, timezone

# ── Paths ─────────────────────────────────────────────────────────────────────
SCRIPT_DIR    = os.path.dirname(os.path.abspath(__file__))
MANIFEST_PATH = os.path.join(SCRIPT_DIR, "dist", "manifest.json")
BUILD_YAML    = os.path.join(SCRIPT_DIR, "build.yaml")

# ── Args ──────────────────────────────────────────────────────────────────────
if len(sys.argv) < 4:
    print(__doc__)
    sys.exit(1)

version    = sys.argv[1]
checksum   = sys.argv[2]
source_url = sys.argv[3]
changelog  = sys.argv[4] if len(sys.argv) > 4 else None
timestamp  = sys.argv[5] if len(sys.argv) > 5 else None

if timestamp is None:
    timestamp = datetime.now(timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ")

# ── Read targetAbi and defaults from build.yaml ───────────────────────────────
target_abi = "10.10.0.0"  # fallback

try:
    # Simple line-by-line parse — avoids PyYAML dependency
    with open(BUILD_YAML, encoding="utf-8") as f:
        for line in f:
            line = line.strip()
            if line.startswith("targetAbi:"):
                target_abi = line.split(":", 1)[1].strip().strip('"').strip("'")
            if changelog is None and line.startswith("changelog:"):
                changelog = line.split(":", 1)[1].strip().strip('"').strip("'")
except FileNotFoundError:
    print(f"Warning: {BUILD_YAML} not found — using defaults")

if not changelog:
    changelog = "See GitHub release for details."

# ── Load manifest ─────────────────────────────────────────────────────────────
with open(MANIFEST_PATH, encoding="utf-8") as f:
    manifest = json.load(f)

plugin = manifest[0]

# ── Remove existing entry for this version (idempotent) ───────────────────────
plugin["versions"] = [v for v in plugin["versions"] if v.get("version") != version]

# ── Build new version entry ───────────────────────────────────────────────────
new_entry = {
    "version":   version,
    "checksum":  checksum,
    "changelog": changelog,
    "targetAbi": target_abi,
    "sourceUrl": source_url,
    "timestamp": timestamp,
}

# Insert at the top — Jellyfin shows the first entry as "latest"
plugin["versions"].insert(0, new_entry)

# ── Write back ────────────────────────────────────────────────────────────────
with open(MANIFEST_PATH, "w", encoding="utf-8") as f:
    json.dump(manifest, f, ensure_ascii=False, indent=2)
    f.write("\n")

print(f"✅ manifest.json updated — version {version} added (targetAbi={target_abi})")
print(f"   sourceUrl : {source_url}")
print(f"   checksum  : {checksum}")
print(f"   timestamp : {timestamp}")
