#!/usr/bin/env bash
set -eo pipefail

PROJECT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$PROJECT_DIR"

source /opt/ros/humble/setup.bash
set -u

export ROS_LOG_DIR="$PROJECT_DIR/ros_logs"
mkdir -p "$ROS_LOG_DIR" "$PROJECT_DIR/bags"

RUN_NAME="${1:-scooter_rgbd_imu_$(date +%Y%m%d_%H%M%S)}"
BAG_DIR="$PROJECT_DIR/bags/$RUN_NAME"
CAMERA_PID=""
RECORDER_PID=""

cleanup() {
  if [[ -n "$RECORDER_PID" ]]; then
    kill -INT "$RECORDER_PID" 2>/dev/null || true
    wait "$RECORDER_PID" 2>/dev/null || true
  fi
  if [[ -n "$CAMERA_PID" ]]; then
    kill -INT "$CAMERA_PID" 2>/dev/null || true
    wait "$CAMERA_PID" 2>/dev/null || true
  fi
}
trap cleanup EXIT INT TERM

echo "Starting RealSense D435i node..."
ros2 launch realsense2_camera rs_launch.py \
  camera_namespace:=camera \
  camera_name:=d435i \
  serial_no:="'148122071023'" \
  initial_reset:=true \
  output:=log \
  enable_color:=true \
  enable_depth:=true \
  rgb_camera.color_profile:=640,480,30 \
  depth_module.depth_profile:=640,480,30 \
  align_depth.enable:=true \
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

echo "Waiting for camera topics..."
sleep 8

echo "Recording to $BAG_DIR"
if [[ -n "${RECORD_SECONDS:-}" ]]; then
  echo "Recording for ${RECORD_SECONDS}s."
else
  echo "Press Ctrl-C to stop."
fi

RECORD_ARGS=(
  --max-bag-size 2147483648
  --max-cache-size 1073741824
  -o "$BAG_DIR"
  /tf_static
  /camera/d435i/color/image_raw
  /camera/d435i/color/camera_info
  /camera/d435i/depth/image_rect_raw
  /camera/d435i/depth/camera_info
  /camera/d435i/aligned_depth_to_color/image_raw
  /camera/d435i/aligned_depth_to_color/camera_info
  /camera/d435i/imu
  /camera/d435i/gyro/sample
  /camera/d435i/accel/sample
)

if [[ -n "${RECORD_SECONDS:-}" ]]; then
  timeout --signal=INT --kill-after=60s "${RECORD_SECONDS}s" ros2 bag record "${RECORD_ARGS[@]}" || status="$?"
  if [[ "${status:-0}" != "0" && "${status:-0}" != "124" ]]; then
    exit "$status"
  fi
else
  ros2 bag record "${RECORD_ARGS[@]}"
fi

cleanup
trap - EXIT INT TERM
