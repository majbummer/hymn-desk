#!/bin/sh
set -e

DB_DIR="${RAILWAY_VOLUME_MOUNT_PATH:-/app}"
DB_PATH="$DB_DIR/lutheran_music_planner.db"

if [ ! -f "$DB_PATH" ]; then
    echo "No database found at $DB_PATH — downloading from GitHub Release as a one-time bootstrap..."
    curl -L -o "$DB_PATH" "https://github.com/majbummer/hymn-desk/releases/download/1.0/lutheran_music_planner.db"
    echo "Download complete."
else
    echo "Using existing database at $DB_PATH"
fi

exec dotnet LutheranMusicLibrary.Web.dll
