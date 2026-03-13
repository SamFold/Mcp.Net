#!/bin/sh
# ───────────────────────────────────────────────────────────────────
# snapshot-testdata.sh — Creates a frozen copy of the Mcp.Net repo
# tree for repeatable grep benchmarks and tests.
#
# Usage:
#   ./snapshot-testdata.sh              # defaults to repo root
#   ./snapshot-testdata.sh /path/to/src # snapshot a different tree
#
# Output: scripts/GrepBenchmark/testdata/
#
# Re-run to refresh the snapshot. Safe to delete and recreate.
# ───────────────────────────────────────────────────────────────────

set -eu

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
DEST="$SCRIPT_DIR/testdata"

# Source: first argument, or walk up to find repo root.
if [ $# -ge 1 ]; then
    SRC="$(cd "$1" && pwd)"
else
    SRC="$SCRIPT_DIR"
    while [ "$SRC" != "/" ]; do
        [ -d "$SRC/.git" ] && break
        SRC="$(dirname "$SRC")"
    done
fi

if [ ! -d "$SRC/.git" ]; then
    echo "Error: could not find a git repository root." >&2
    exit 1
fi

echo "Source:      $SRC"
echo "Destination: $DEST"

# Clean previous snapshot.
if [ -d "$DEST" ]; then
    echo "Removing previous snapshot..."
    rm -rf "$DEST"
fi

# rsync with exclusions — keeps the tree shape but drops noise.
rsync -a \
    --exclude='.git/' \
    --exclude='bin/' \
    --exclude='obj/' \
    --exclude='node_modules/' \
    --exclude='.vs/' \
    --exclude='*.user' \
    --exclude='testdata/' \
    "$SRC/" "$DEST/"

# Count what we got.
FILE_COUNT=$(find "$DEST" -type f | wc -l | tr -d ' ')
DIR_COUNT=$(find "$DEST" -type d | wc -l | tr -d ' ')

echo "Snapshot complete: $FILE_COUNT files, $DIR_COUNT directories"
