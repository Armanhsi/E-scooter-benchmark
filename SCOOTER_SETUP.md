# Scooter RealSense Setup

Goal: run the Jetson Orin Nano + RealSense D435i + GlobalSat BU-353N GPS on the e-scooter without a monitor, powered from a portable battery, and record synchronized ride data into `bags/`.

## Current State

- Jetson has ROS 2 Humble.
- SSH is enabled and active.
- Current visible host IP from this session: `172.27.176.169`
- RealSense D435i is detected:
  - Serial: `148122071023`
  - Firmware: `5.17.0.10`
- GlobalSat BU-353N USB GPS is installed:
  - Device: `/dev/gps0` symlinked to `/dev/ttyUSB0`
  - Driver: `pl2303`
  - Output: NMEA at 4800 baud
  - For good GPS data, place the puck outside with the flat/logo side up and give it open sky before/during recording.
- Free storage checked during setup: about 76 GiB available.
- Seagate Portable Drive was used successfully for direct field recording:
  - Mount path: `/media/jetson/Seagate Portable Drive`
  - Bag root: `/media/jetson/Seagate Portable Drive/RealsenseD435i/bags`
  - Filesystem seen by Linux as NTFS/fuseblk

## Power Setup

Use a power bank that can provide enough power for the Jetson Orin Nano developer kit plus the USB RealSense camera. The developer kit expects its main power through the DC input/barrel-jack path, not through the normal USB-C data port.

Recommended practical target:

- USB-C PD power bank, at least 45 W output.
- PD trigger/barrel-jack cable or adapter set for the Jetson dev kit DC input voltage.
- Prefer a 19 V or 20 V PD output path only if the adapter and carrier input are rated for it.
- Use a short, good USB-C cable rated for the power level.
- Fully charge the power bank before field tests.
- Secure both the power bank and Jetson so vibration cannot loosen connectors.

Do not power the Jetson by plugging a power bank into the USB-C data port unless your exact carrier board documentation says that port accepts power input. Do not power the RealSense from a weak USB hub. Plug the D435i directly into the Jetson with a short USB 3 cable.

## Headless Access

Before removing the monitor:

1. Connect the Jetson to Wi-Fi or Ethernet.
2. Confirm the IP:

   ```bash
   hostname -I
   ```

3. From your laptop on the same network:

   ```bash
   ssh jetson@<JETSON_IP>
   ```

4. Go to the project:

   ```bash
   cd ~/Desktop/RealsenceD435i
   ```

If the field network is unreliable, use a phone hotspot or configure the Jetson to auto-connect to the same hotspot before the ride.

Wi-Fi is only needed to start/check recordings. The camera and rosbag recording do not need Wi-Fi after launch if the detached field launcher is used.

## Record A Scooter Ride

From now on, field collections should include:

- RealSense color frames at 15 Hz
- RealSense raw gyro at 200 Hz
- RealSense raw accel at 200 Hz
- GlobalSat GPS raw NMEA and parsed CSV

For field rides where Wi-Fi may disconnect, use the detached launcher. This saves directly to the Seagate drive when it is mounted:

```bash
cd ~/Desktop/RealsenceD435i
./scripts/start_field_recording.sh Ride_Name 600
```

This example records for 600 seconds. The script prints the output path, then starts a detached `tmux` session. After it prints `Detached recording session started`, SSH/Wi-Fi can disconnect and recording will continue locally.

The output folder contains both the ROS bag and GPS files:

```text
<bag_root>/<run_name>/
  metadata.yaml
  <run_name>_*.db3
  gps/
    gps_raw.nmea
    gps_parsed.csv
```

For a 15-minute ride, use:

```bash
cd ~/Desktop/RealsenceD435i
./scripts/start_field_recording.sh Ride_Name 900
```

The launcher asks every time whether to shut down the Jetson after the recording. To enable shutdown, type exactly `YES` at the warning prompt. Press Enter for the default behavior, which is no shutdown. When shutdown is enabled, it only happens after successful recording/save, with a default 2-minute buffer. The buffer can be changed with `SHUTDOWN_DELAY_MINUTES`.

To check while still connected:

```bash
tmux attach -t record_Ride_Name
```

Detach without stopping:

```text
Ctrl-b, then d
```

To stop early, interrupt the recorder process. If the tmux session is active, try:

```bash
tmux send-keys -t record_Ride_Name C-c
```

Then verify no recorder is still running:

```bash
ps -eo pid,ppid,stat,cmd | rg 'Ride_Name|ros2 bag record|scooter_record_color_imu'
```

If needed, send SIGINT to the `timeout ... ros2 bag record ...` process so rosbag flushes metadata:

```bash
kill -INT <timeout_pid>
```

For a local, non-detached test of the current scooter vibration/surface setup:

```bash
cd ~/Desktop/RealsenceD435i
RECORD_SECONDS=300 ./scripts/scooter_record_color_imu.sh scooter_color_imu_$(date +%Y%m%d_%H%M%S)
```

This disables raw depth and aligned depth. It records:

- `/camera/d435i/color/image_raw`
- `/camera/d435i/color/camera_info`
- `/camera/d435i/gyro/sample`
- `/camera/d435i/accel/sample`
- `/tf_static`
- GPS files under `<bag_root>/<run_name>/gps/`

Current profile:

- Color: `640x480 @ 15 Hz`
- Gyro: `200 Hz`
- Accel: `200 Hz`
- Depth: disabled
- Default rosbag split size: 8 GiB, adjustable with `MAX_BAG_SIZE`

This is the default for vibration work because it preserves raw accelerometer/gyro signals while reducing camera write load enough to avoid the cache-loss behavior seen with 30 Hz color plus duplicate IMU topics.

For a quick check before riding:

```bash
cd ~/Desktop/RealsenceD435i
RECORD_SECONDS=10 ./scripts/scooter_record_color_imu.sh scooter_check_$(date +%Y%m%d_%H%M%S)
```

For full RGB-D + IMU recording:

From SSH:

```bash
cd ~/Desktop/RealsenceD435i
./scripts/scooter_record.sh
```

The script records into:

```bash
bags/scooter_rgbd_imu_YYYYMMDD_HHMMSS
```

Stop recording with `Ctrl-C`. The script will stop the RealSense launch process too.

For a short video + IMU test:

```bash
cd ~/Desktop/RealsenceD435i
RECORD_SECONDS=10 ./scripts/scooter_record.sh short_video_imu_$(date +%Y%m%d_%H%M%S)
```

The current RGB-D + IMU profile writes roughly 40-45 MB/s, so 76 GiB free storage is roughly 25-30 minutes of recording. Leave extra margin and check disk space before long rides.

The current 15 Hz color + raw IMU profile tested at about 766 MiB for 57.6 seconds, so five minutes is roughly 4 GiB.

Recent Seagate tests used about 7-8 GiB for about 10 minutes at 15 Hz color plus 200 Hz accel/gyro.

## Verify After Recording

```bash
ros2 bag info bags/<bag_name>
```

For a healthy current scooter vibration run, expect:

- Color around 15 Hz
- Gyro around 200 Hz
- Accel around 200 Hz

For a healthy RGB-D + IMU run with `scooter_record.sh`, expect color/depth around 30 Hz.

For bags on the Seagate drive:

```bash
ros2 bag info "/media/jetson/Seagate Portable Drive/RealsenseD435i/bags/<bag_name>"
```

## Extract Samples And CSV

Export sampled color frames from a time window:

```bash
cd ~/Desktop/RealsenceD435i
./scripts/extract_color_samples.py \
  "/media/jetson/Seagate Portable Drive/RealsenseD435i/bags/Sidewalk_cafe" \
  analysis/Sidewalk_cafe_3min_5min_samples \
  --start-sec 180 \
  --end-sec 300 \
  --samples 9
```

This writes individual JPG frames, `manifest.csv`, and `contact_sheet.jpg`.

Export raw acceleration to CSV:

```bash
cd ~/Desktop/RealsenceD435i
./scripts/export_imu_csv.py \
  "/media/jetson/Seagate Portable Drive/RealsenseD435i/bags/cafe_asphalt" \
  analysis/cafe_asphalt_accel.csv
```

CSV columns are:

- `bag_time_ns`
- `time_from_start_s`
- `header_stamp_ns`
- `linear_accel_x_mps2`
- `linear_accel_y_mps2`
- `linear_accel_z_mps2`

## GlobalSat BU-353N USB GPS

The GlobalSat BU-353N appears on USB as:

```text
067b:23a3 Prolific Technology, Inc. USB-Serial Controller
```

This receiver outputs standard NMEA sentences over a USB serial interface. On this Jetson kernel, `CONFIG_USB_SERIAL_PL2303` is currently disabled, so the receiver is visible in `lsusb` but does not automatically create `/dev/ttyUSB0`.

After plugging in the GPS, check:

```bash
lsusb | grep -i '067b:23a3'
ls -l /dev/ttyUSB* /dev/gps0
```

If no serial device appears, install the project-built PL2303 driver:

```bash
cd ~/Desktop/RealsenceD435i
./scripts/install_bu353n_gps.sh
```

Unplug and replug the BU-353N, then check:

```bash
ls -l /dev/gps0 /dev/ttyUSB*
```

Fallback only: if the PL2303 install is not available, try the generic USB serial driver:

```bash
sudo modprobe usbserial vendor=0x067b product=0x23a3
```

Then check again:

```bash
ls -l /dev/ttyUSB*
```

For a persistent device name, the installer creates this udev rule:

```bash
echo 'SUBSYSTEM=="tty", ATTRS{idVendor}=="067b", ATTRS{idProduct}=="23a3", SYMLINK+="gps0", MODE="0666"' | \
  sudo tee /etc/udev/rules.d/99-globalsat-bu353n.rules
sudo udevadm control --reload-rules
sudo udevadm trigger
```

Optional GPS tools:

```bash
sudo apt update
sudo apt install -y gpsd gpsd-clients
```

Quick raw NMEA check:

```bash
timeout 10s cat /dev/gps0
```

Direct project logging without `gpsd`:

```bash
cd ~/Desktop/RealsenceD435i
./scripts/gps_log_nmea.py analysis/gps_test_$(date +%Y%m%d_%H%M%S) \
  --device /dev/gps0 \
  --baud 4800 \
  --duration 60
```

This writes:

- `gps_raw.nmea`: raw NMEA sentences with Jetson system timestamps
- `gps_parsed.csv`: parsed GGA/RMC fields with latitude, longitude, altitude, speed, course, fix quality, satellite count, HDOP, and Jetson system timestamps

If `/dev/gps0` does not exist but `/dev/ttyUSB0` does, use:

```bash
./scripts/gps_log_nmea.py analysis/gps_test_$(date +%Y%m%d_%H%M%S) \
  --device /dev/ttyUSB0 \
  --baud 4800 \
  --duration 60
```

## Upload Bags To UVA Box

Use `rclone` with a Box remote. Configure the remote once from a browser-capable machine or from the Jetson if browser login is available:

```bash
rclone config
```

Recommended remote name:

```bash
uva_box
```

The upload script treats `bags/` as a local buffer. It skips incomplete bags, uploads complete bag directories to Box, verifies file sizes, and only deletes local data if explicitly enabled:

```bash
cd ~/Desktop/RealsenceD435i
BOX_REMOTE="uva_box:RealSenseD435i" ./scripts/upload_bags_to_box.sh
```

After confirming uploads work, enable local cleanup:

```bash
cd ~/Desktop/RealsenceD435i
BOX_REMOTE="uva_box:RealSenseD435i" DELETE_AFTER_UPLOAD=1 ./scripts/upload_bags_to_box.sh
```

Logs are written under `ros_logs/upload_<bag_name>_YYYYMMDD_HHMMSS.log`.

## Mounting Notes

- Mount the camera rigidly to the scooter frame, not to a loose handlebar accessory.
- Keep the lens clear of cables and hands.
- Add strain relief for the RealSense USB cable.
- Avoid pointing the camera too far down; include enough forward scene for motion/obstacle context.
- Run a short stationary test, then a slow walking/scooter push test, before a full ride.
