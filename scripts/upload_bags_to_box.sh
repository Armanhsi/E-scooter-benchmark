#!/usr/bin/env bash
set -euo pipefail

PROJECT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$PROJECT_DIR"

BAGS_DIR="${BAGS_DIR:-$PROJECT_DIR/bags}"
REMOTE="${BOX_REMOTE:-uva_box:RealSenseD435i}"
DELETE_AFTER_UPLOAD="${DELETE_AFTER_UPLOAD:-0}"
MIN_AGE_SECONDS="${MIN_AGE_SECONDS:-120}"
LOG_DIR="${LOG_DIR:-$PROJECT_DIR/ros_logs}"
LOCK_DIR="$PROJECT_DIR/.upload_bags_to_box.lock"

mkdir -p "$LOG_DIR"

if ! command -v rclone >/dev/null 2>&1; then
  echo "rclone is not installed. Install it and configure a Box remote first." >&2
  echo "Expected remote path: $REMOTE" >&2
  exit 127
fi

if ! mkdir "$LOCK_DIR" 2>/dev/null; then
  echo "Another upload_bags_to_box.sh process is already running." >&2
  exit 1
fi
trap 'rmdir "$LOCK_DIR"' EXIT

now_epoch="$(date +%s)"

for bag_dir in "$BAGS_DIR"/*; do
  [[ -d "$bag_dir" ]] || continue

  bag_name="$(basename "$bag_dir")"
  marker="$bag_dir/.uploaded_to_box"

  if [[ -f "$marker" ]]; then
    continue
  fi

  if [[ ! -f "$bag_dir/metadata.yaml" ]]; then
    echo "Skipping $bag_name: metadata.yaml is missing, so the bag may be incomplete."
    continue
  fi

  newest_mtime="$(find "$bag_dir" -type f -printf '%T@\n' | sort -nr | head -n 1)"
  newest_epoch="${newest_mtime%.*}"
  age_seconds=$((now_epoch - newest_epoch))

  if (( age_seconds < MIN_AGE_SECONDS )); then
    echo "Skipping $bag_name: last modified ${age_seconds}s ago."
    continue
  fi

  remote_dir="$REMOTE/$bag_name"
  log_file="$LOG_DIR/upload_${bag_name}_$(date +%Y%m%d_%H%M%S).log"

  echo "Uploading $bag_name to $remote_dir"
  if rclone copy "$bag_dir" "$remote_dir" \
      --create-empty-src-dirs \
      --transfers 1 \
      --checkers 4 \
      --retries 5 \
      --low-level-retries 20 \
      --stats 30s \
      --log-file "$log_file" \
      --log-level INFO; then
    echo "Verifying $bag_name"
    rclone check "$bag_dir" "$remote_dir" \
      --one-way \
      --size-only \
      --log-file "$log_file" \
      --log-level INFO

    date -Is > "$marker"

    if [[ "$DELETE_AFTER_UPLOAD" == "1" ]]; then
      echo "Upload verified. Removing local bag: $bag_dir"
      rm -rf -- "$bag_dir"
    else
      echo "Upload verified. Keeping local bag: $bag_dir"
    fi
  else
    echo "Upload failed for $bag_name. Keeping local bag. See $log_file" >&2
  fi
done
