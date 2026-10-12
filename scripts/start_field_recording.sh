#!/usr/bin/env bash
set -euo pipefail

PROJECT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$PROJECT_DIR"

RUN_NAME="${1:-field_recording_$(date +%Y%m%d_%H%M%S)}"
RECORD_SECONDS="${2:-120}"
SESSION_NAME="record_${RUN_NAME//[^A-Za-z0-9_]/_}"
SESSION_NAME="${SESSION_NAME:0:80}"
LOG_DIR="$PROJECT_DIR/ros_logs"
SEAGATE_MOUNT="/media/jetson/Seagate Portable Drive"
SEAGATE_BAG_ROOT="$SEAGATE_MOUNT/RealsenseD435i/bags"
LOG_FILE="$LOG_DIR/${RUN_NAME}_detached_recording.log"
PID_FILE="$LOG_DIR/${RUN_NAME}_detached_recording.pid"
RUN_SCRIPT="$LOG_DIR/${RUN_NAME}_detached_recording_run.sh"
SHUTDOWN_AFTER="${SHUTDOWN_AFTER:-ask}"
SHUTDOWN_CONFIRMED="0"
SHUTDOWN_DELAY_MINUTES="${SHUTDOWN_DELAY_MINUTES:-2}"

if ! command -v tmux >/dev/null 2>&1; then
  echo "tmux is required for disconnected recording, but it was not found." >&2
  exit 1
fi

if ! mountpoint -q "$SEAGATE_MOUNT"; then
  echo "Seagate drive is not mounted at: $SEAGATE_MOUNT" >&2
  exit 1
fi

mkdir -p "$LOG_DIR" "$SEAGATE_BAG_ROOT"

if tmux has-session -t "$SESSION_NAME" 2>/dev/null; then
  echo "A tmux recording session already exists: $SESSION_NAME" >&2
  echo "Attach with: tmux attach -t $SESSION_NAME" >&2
  exit 1
fi

if [[ -e "$SEAGATE_BAG_ROOT/$RUN_NAME" ]]; then
  echo "Bag directory already exists: $SEAGATE_BAG_ROOT/$RUN_NAME" >&2
  exit 1
fi

if [[ "$SHUTDOWN_AFTER" == "ask" ]]; then
  echo
  echo "!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!"
  echo "!!! WARNING: AUTO-SHUTDOWN OPTION                      !!!"
  echo "!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!"
  echo "This recording can shut down the Jetson after the bag is saved."
  echo "Use this only when you will not have a screen/SSH at the end."
  echo "Run name: $RUN_NAME"
  echo "Duration: ${RECORD_SECONDS}s"
  echo "Shutdown delay after successful save: ${SHUTDOWN_DELAY_MINUTES} minute(s)"
  echo
  read -r -p "Type YES to shut down after recording, or press Enter for no shutdown: " shutdown_answer
  if [[ "$shutdown_answer" == "YES" ]]; then
    SHUTDOWN_CONFIRMED="1"
  fi
elif [[ "$SHUTDOWN_AFTER" == "1" || "$SHUTDOWN_AFTER" == "true" || "$SHUTDOWN_AFTER" == "yes" ]]; then
  echo "Auto-shutdown requested by SHUTDOWN_AFTER=$SHUTDOWN_AFTER."
  read -r -p "Type YES to confirm Jetson shutdown after recording: " shutdown_answer
  if [[ "$shutdown_answer" == "YES" ]]; then
    SHUTDOWN_CONFIRMED="1"
  else
    echo "Shutdown was not confirmed; continuing without auto-shutdown."
  fi
fi

cat > "$RUN_SCRIPT" <<EOF
#!/usr/bin/env bash
set -euo pipefail
cd "$PROJECT_DIR"
WARMUP_SECONDS=45 \\
MAX_CACHE_SIZE=1073741824 \\
BAG_ROOT="$SEAGATE_BAG_ROOT" \\
RECORD_SECONDS="$RECORD_SECONDS" \\
  ./scripts/scooter_record_color_imu.sh "$RUN_NAME" > "$LOG_FILE" 2>&1 < /dev/null
record_status=\$?
if [[ "$SHUTDOWN_CONFIRMED" == "1" && "\$record_status" == "0" ]]; then
  echo "Recording finished successfully. Jetson will shut down in ${SHUTDOWN_DELAY_MINUTES} minute(s)." >> "$LOG_FILE"
  echo "Cancel from another shell with: sudo shutdown -c" >> "$LOG_FILE"
  sudo shutdown -h +"$SHUTDOWN_DELAY_MINUTES" "Scooter recording $RUN_NAME finished; shutting down."
fi
exit "\$record_status"
EOF
chmod +x "$RUN_SCRIPT"

tmux new-session -d -s "$SESSION_NAME" "bash '$RUN_SCRIPT'"
echo "$SESSION_NAME" > "$PID_FILE"

echo "Detached recording session started."
echo "Session: $SESSION_NAME"
echo "Run name: $RUN_NAME"
echo "Duration: ${RECORD_SECONDS}s"
echo "Bag path: $SEAGATE_BAG_ROOT/$RUN_NAME"
if [[ "$SHUTDOWN_CONFIRMED" == "1" ]]; then
  echo "AUTO-SHUTDOWN: ENABLED after successful recording"
else
  echo "AUTO-SHUTDOWN: disabled"
fi
echo "Log file: $LOG_FILE"
echo
echo "You can disconnect Wi-Fi/SSH after this starts."
echo "Check live status while connected: tmux attach -t $SESSION_NAME"
echo "Detach from tmux without stopping recording: Ctrl-b then d"
echo "Check log later: tail -f '$LOG_FILE'"
echo "Check session later: tmux ls"
