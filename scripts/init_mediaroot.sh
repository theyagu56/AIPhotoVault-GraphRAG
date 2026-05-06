#!/usr/bin/env bash
# ============================================================
#  PhotoVault — MediaRoot Initializer
#  Run once to set up the folder structure on your media drive.
#  Usage: bash init_mediaroot.sh /path/to/MediaRoot
# ============================================================
set -euo pipefail

MEDIA_ROOT="${1:-$(pwd)/MediaRoot}"

echo "🗂  Initializing PhotoVault MediaRoot at: $MEDIA_ROOT"

# ── Primary folders ─────────────────────────────────────────
mkdir -p "$MEDIA_ROOT/PhotosVideos"
mkdir -p "$MEDIA_ROOT/Application/Database"
mkdir -p "$MEDIA_ROOT/Application/Thumbnails/sm"    # 200px
mkdir -p "$MEDIA_ROOT/Application/Thumbnails/md"    # 400px
mkdir -p "$MEDIA_ROOT/Application/Thumbnails/lg"    # 800px
mkdir -p "$MEDIA_ROOT/Application/Embeddings"
mkdir -p "$MEDIA_ROOT/Application/Metadata"
mkdir -p "$MEDIA_ROOT/Application/Trash"
mkdir -p "$MEDIA_ROOT/Application/Logs"
mkdir -p "$MEDIA_ROOT/Application/Temp"

# ── README stubs ─────────────────────────────────────────────
cat > "$MEDIA_ROOT/PhotosVideos/README.txt" << 'EOF'
PhotoVault — Original Media Storage
=====================================
• All original photo and video files live here.
• NEVER manually delete files from this folder.
• Sub-folders are allowed (by date, event, etc.).
• Supported: .jpg .jpeg .png .heic .gif .webp .mp4 .mov .avi .mkv
• On "delete" in the app, files move to /Application/Trash/ — never gone.
EOF

cat > "$MEDIA_ROOT/Application/README.txt" << 'EOF'
PhotoVault — Application Data
==============================
• Database/   → photovault.db  (SQLite, the source of truth)
• Thumbnails/ → generated cache (safe to delete, will regenerate)
• Embeddings/ → AI vector embeddings for semantic search
• Metadata/   → extracted EXIF / sidecar JSON files
• Trash/      → moved originals pending permanent deletion
• Logs/       → application and pipeline logs
• Temp/       → transient processing scratch space
EOF

cat > "$MEDIA_ROOT/Application/Trash/README.txt" << 'EOF'
PhotoVault — Trash
===================
• Files moved here from /PhotosVideos/ when "deleted" in the app.
• A zero-byte placeholder (*.photovault-deleted) remains at the
  original path to preserve folder structure.
• Restore via the app Trash view → original path is stored in the DB.
• Permanent delete: only via admin action in the app.
EOF

# ── .gitignore for DB + logs ─────────────────────────────────
cat > "$MEDIA_ROOT/Application/.gitignore" << 'EOF'
Database/*.db
Database/*.db-shm
Database/*.db-wal
Logs/*.log
Temp/
Embeddings/*.bin
EOF

# ── Permissions (macOS / Linux) ──────────────────────────────
chmod 755 "$MEDIA_ROOT/PhotosVideos"
chmod 700 "$MEDIA_ROOT/Application/Database"

echo ""
echo "✅  MediaRoot ready:"
find "$MEDIA_ROOT" -type d | sort | sed 's|'"$MEDIA_ROOT"'||' | sed 's/^/   /'
echo ""
echo "Next step → run: dotnet ef database update (from PhotoVault.API project)"
