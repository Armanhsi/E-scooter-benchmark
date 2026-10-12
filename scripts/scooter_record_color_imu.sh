#!/usr/bin/env bash
set -eo pipefail

PROJECT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$PROJECT_DIR"

source /opt/ros/humble/setup.bash
set -u

export ROS_LOG_DIR="$PROJECT_DIR/ros_logs"

SEAGATE_MOUNT="/media/jetson/Seagate Portable Drive"
if [[ -n "${BAG_ROOT:-}" ]]; then
  BAG_ROOT="$BAG_ROOT"
elif mountpoint -q "$SEAGATE_MOUNT"; then
  BAG_ROOT="$SEAGATE_MOUNT/RealsenseD435i/bags"
else
  BAG_ROOT="$PROJECT_DIR/bags"
fi

mkdir -p "$ROS_LOG_DIR" "$BAG_ROOT"

RUN_NAME="${1:-scooter_color_imu_$(date +%Y%m%d_%H%M%S)}"
BAG_DIR="$BAG_ROOT/$RUN_NAME"
GPS_DIR="$BAG_DIR/gps"
CAMERA_PID=""
TIMER_PID=""
GPS_PID=""

cleanup() {
  if [[ -n "$TIMER_PID" ]] && kill -0 "$TIMER_PID" 2>/dev/null; then
    kill "$TIMER_PID" 2>/dev/null || true
    wait "$TIMER_PID" 2>/dev/null || true
  fi
  if [[ -n "$GPS_PID" ]] && kill -0 "$GPS_PID" 2>/dev/null; then
    kill -INT "$GPS_PID" 2>/dev/null || true
    sleep 1
    if kill -0 "$GPS_PID" 2>/dev/null; then
      kill -TERM "$GPS_PID" 2>/dev/null || true
    fi
    wait "$GPS_PID" 2>/dev/null || true
  fi
  if [[ -n "$CAMERA_PID" ]] && kill -0 "$CAMERA_PID" 2>/dev/null; then
    kill -INT "$CAMERA_PID" 2>/dev/null || true
    sleep 5
    if kill -0 "$CAMERA_PID" 2>/dev/null; then
      kill -TERM "$CAMERA_PID" 2>/dev/null || true
      sleep 2
    fi
    if kill -0 "$CAMERA_PID" 2>/dev/null; then
      kill -KILL "$CAMERA_PID" 2>/dev/null || true
    fi
    wait "$CAMERA_PID" 2>/dev/null || true
  fi
}
trap cleanup EXIT INT TERM

start_recording_timer() {
  local duration="$1"
  (
    local elapsed=0
    while [[ -z "$duration" || "$elapsed" -lt "$duration" ]]; do
      sleep 1
      elapsed=$((elapsed + 1))
      if [[ -n "$duration" ]]; then
        printf 'Recording elapsed: %03ds / %03ds\n' "$elapsed" "$duration"
      else
        printf 'Recording elapsed: %03ds\n' "$elapsed"
      fi
    done
  ) &
  TIMER_PID="$!"
}

if [[ -e "$BAG_DIR" ]]; then
  echo "Bag directory already exists: $BAG_DIR" >&2
  exit 1
fi

echo "Starting RealSense D435i node for color + IMU..."
ros2 launch realsense2_camera rs_launch.py \
  camera_namespace:=camera \
  camera_name:=d435i \
  serial_no:="'148122071023'" \
  initial_reset:=true \
  output:=log \
  enable_color:=true \
  enable_depth:=false \
  rgb_camera.color_profile:=640,480,15 \
  align_depth.enable:=false \
  pointcloud.enable:=false \
  enable_infra1:=false \
  enable_infra2:=false \
  enable_gyro:=true \
  enable_accel:=true \
  gyro_fps:=200 \
  accel_fps:=200 \
  unite_imu_method:=2 \
  publish_tf:=true \
  > "$ROS_LOG_DIR/${RUN_NAME}_camera_stdout.log" 2>&1 &
CAMERA_PID="$!"

WARMUP_SECONDS="${WARMUP_SECONDS:-20}"
echo "Waiting ${WARMUP_SECONDS}s for camera topics..."
sleep "$WARMUP_SECONDS"

echo "Bag root: $BAG_ROOT"
echo "Recording color + IMU to $BAG_DIR"
GPS_DEVICE="${GPS_DEVICE:-/dev/gps0}"
GPS_BAUD="${GPS_BAUD:-4800}"
GPS_ENABLE="${GPS_ENABLE:-auto}"
if [[ "$GPS_ENABLE" != "0" && "$GPS_ENABLE" != "false" ]]; then
  if [[ -e "$GPS_DEVICE" ]]; then
    mkdir -p "$GPS_DIR"
    GPS_ARGS=("$GPS_DIR" --device "$GPS_DEVICE" --baud "$GPS_BAUD")
    if [[ -n "${RECORD_SECONDS:-}" ]]; then
      GPS_ARGS+=(--duration "$RECORD_SECONDS")
    fi
    echo "Recording GPS NMEA/CSV to $GPS_DIR"
    ./scripts/gps_log_nmea.py "${GPS_ARGS[@]}" > "$ROS_LOG_DIR/${RUN_NAME}_gps_stdout.log" 2>&1 &
    GPS_PID="$!"
  elif [[ "$GPS_ENABLE" == "1" || "$GPS_ENABLE" == "true" ]]; then
    echo "GPS was required but $GPS_DEVICE does not exist." >&2
    exit 1
  else
    echo "GPS device not found at $GPS_DEVICE; continuing without GPS."
  fi
fi
if [[ -n "${RECORD_SECONDS:-}" ]]; then
  echo "Recording for ${RECORD_SECONDS}s."
else
  echo "Press Ctrl-C to stop."
fi

RECORD_ARGS=(
  --max-bag-size "${MAX_BAG_SIZE:-8589934592}"
  --max-cache-size "${MAX_CACHE_SIZE:-268435456}"
  -o "$BAG_DIR"
  /tf_static
  /camera/d435i/color/image_raw
  /camera/d435i/color/camera_info
  /camera/d435i/gyro/sample
  /camera/d435i/accel/sample
)

if [[ -n "${RECORD_SECONDS:-}" ]]; then
  echo "RECORDING STARTED at $(date --iso-8601=seconds)"
  start_recording_timer "$RECORD_SECONDS"
  timeout --signal=INT --kill-after=120s "${RECORD_SECONDS}s" ros2 bag record "${RECORD_ARGS[@]}" || status="$?"
  if [[ -n "$TIMER_PID" ]] && kill -0 "$TIMER_PID" 2>/dev/null; then
    kill "$TIMER_PID" 2>/dev/null || true
    wait "$TIMER_PID" 2>/dev/null || true
  fi
  TIMER_PID=""
  if [[ "${status:-0}" != "0" && "${status:-0}" != "124" ]]; then
    exit "$status"
  fi
else
  echo "RECORDING STARTED at $(date --iso-8601=seconds)"
  start_recording_timer ""
  ros2 bag record "${RECORD_ARGS[@]}"
fi

cleanup
trap - EXIT INT TERM
