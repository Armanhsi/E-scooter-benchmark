# RealSense D435i Bag Summary

Context: RealSense D435i + GlobalSat BU-353N GPS on Jetson Orin Nano, ROS 2 Humble. Internal bags are under `bags/`; field bags are saved directly to the Seagate portable drive when mounted.

## What Was Captured

The recordings are ROS 2 `rosbag2` SQLite bags from the RealSense ROS wrapper. Topics are under `/camera/d435i` and include combinations of:

- RGB color images: `/camera/d435i/color/image_raw`
- Depth images: `/camera/d435i/depth/image_rect_raw`
- Infrared stereo images: `/camera/d435i/infra1/image_rect_raw`, `/camera/d435i/infra2/image_rect_raw`
- Aligned depth: `/camera/d435i/aligned_depth_to_color/image_raw`
- Camera calibration: `*/camera_info`
- IMU streams: `/camera/d435i/imu`, `/camera/d435i/gyro/sample`, `/camera/d435i/accel/sample`
- TF: `/tf_static`
- Some metadata/extrinsics topics in the first bag
- One point cloud topic in the focused bag: `/camera/d435i/depth/color/points`
- GPS data from the GlobalSat BU-353N is saved as files inside the run folder under `gps/`:
  - `gps_raw.nmea`
  - `gps_parsed.csv`

## Bags

| Bag | Meaning | Size / Duration |
| --- | --- | --- |
| `bench_test_20260525_1626` | Broad first bench capture: color, depth, infrared, aligned depth, IMU, metadata, extrinsics. | 833 MiB, about 19.5 s |
| `bench_test_focused_20260525_1628` | Short focused capture with color, depth, infrared, aligned depth, IMU, and point cloud. | 963 MiB, about 11.5 s by ROS metadata |
| `bench_video_imu_20260525_1632` | Longer video plus IMU capture, but effective visual frame rates are low over the full duration. | 1.6 GiB, about 138 s |
| `bench_color_depth_imu_20260525_1640` | Clean color + depth + IMU capture; best baseline sample for RGB-D + IMU testing. | 412 MiB, about 9.4 s |
| `imu_rotation_test_20260525_1648` | IMU-only rotation test with gyro, accel, combined IMU, and TF. | 1.4 MiB, about 7.5 s |
| `Seagate/RealsenseD435i/bags/Field_Test_1` | Detached no-Wi-Fi field workflow test. Color + raw accel/gyro. | 794 MiB, about 59.4 s |
| `Seagate/RealsenseD435i/bags/Sidewalk_cafe` | Sidewalk/cafe ride sample. Color + raw accel/gyro. Stopped early by request. | 7.2 GiB, about 549.4 s |
| `Seagate/RealsenseD435i/bags/cafe_asphalt` | Cafe/asphalt ride sample. Color + raw accel/gyro. | 7.8 GiB, about 593.1 s |
| `bags/gps_outdoor_10min_20261012_172325` | First confirmed GPS + color + raw accel/gyro run. GPS got valid fix during the run. | 7.3 GiB, about 599.4 s |

## Practical Takeaways

- Best baseline for RGB-D + IMU work: `bags/bench_color_depth_imu_20260525_1640`
- Good IMU sanity/rotation sample: `bags/imu_rotation_test_20260525_1648`
- `bench_test_focused_20260525_1628` includes point clouds, which explains the large file size for a short recording.
- `bench_video_imu_20260525_1632` is long, but visual topics appear sparse relative to the duration, so do not use it as the first quality baseline.
- Current scooter/vibration default: use `scripts/start_field_recording.sh <run_name> <seconds>` for field rides. It records color at `640x480,15 Hz`, raw gyro, raw accel, and GPS files when `/dev/gps0` is present. Depth and aligned depth are disabled.
- This 15 Hz color + raw IMU setup is preferred for e-scooter vibration/surface work because it keeps raw accelerometer/gyro data while reducing image write pressure.
- When the Seagate portable drive is mounted at `/media/jetson/Seagate Portable Drive`, `scripts/scooter_record_color_imu.sh` saves bags to `/media/jetson/Seagate Portable Drive/RealsenseD435i/bags` by default.
- For field recording where Wi-Fi may disappear, `scripts/start_field_recording.sh <run_name> <seconds>` starts a detached `tmux` session and redirects recorder stdin from `/dev/null`, so the recording keeps running after SSH disconnects.
- The field launcher asks every time whether to shut down after recording. Shutdown only happens if the user types exactly `YES`, the recording exits successfully, and the default 2-minute post-save buffer has elapsed.
- Current output layout for new rides:

  ```text
  <bag_root>/<run_name>/
    metadata.yaml
    <run_name>_*.db3
    gps/
      gps_raw.nmea
      gps_parsed.csv
  ```

- `scripts/export_imu_csv.py` exports accel or gyro IMU topics from a bag to CSV.
- `scripts/extract_color_samples.py` extracts sampled color frames and a contact sheet from a time window in a bag.
- ROS 2 Humble is installed and `ros2 bag info` can read these bags.

## Observed Rates From Direct DB Counts

`bench_color_depth_imu_20260525_1640`:

- Color/depth: about 30 Hz
- Gyro and combined IMU: about 200 Hz
- Accel: about 100 Hz

`imu_rotation_test_20260525_1648`:

- Gyro and combined IMU: about 200 Hz
- Accel: about 100 Hz

`scooter_color15_rawimu_1min_20260531_141843`:

- Color: about 15 Hz
- Gyro: about 200 Hz
- Accel: about 100 Hz in the recorded 20260531 test; scripts now request 200 Hz for future runs
- No depth topics
- No cache-loss warning observed during the 1-minute test

`Field_Test_1` on Seagate:

- Color: 891 frames over 59.42 s, about 15.0 Hz
- Gyro: 11,819 samples, about 198.9 Hz
- Accel: 11,972 samples, about 201.5 Hz
- Size: 794 MiB

`Sidewalk_cafe` on Seagate:

- Color: 8,232 frames over 549.35 s, about 15.0 Hz
- Gyro: 109,574 samples, about 199.5 Hz
- Accel: 110,881 samples, about 201.8 Hz
- Size: 7.2 GiB
- Sample frames from minutes 3 to 5 were exported to `analysis/Sidewalk_cafe_3min_5min_samples/`.

`cafe_asphalt` on Seagate:

- Color: 8,878 frames over 593.11 s, about 15.0 Hz
- Gyro: 117,700 samples, about 198.4 Hz
- Accel: 119,128 samples, about 200.9 Hz
- Size: 7.8 GiB
- Acceleration CSV exported to `analysis/cafe_asphalt_accel.csv` with columns `bag_time_ns`, `time_from_start_s`, `header_stamp_ns`, `linear_accel_x_mps2`, `linear_accel_y_mps2`, `linear_accel_z_mps2`.

`gps_outdoor_10min_20261012_172325`:

- Duration: about 599.37 s
- Size: 7.3 GiB
- Color: 8,372 frames, about 14.0 Hz effective due to split/cache losses
- Gyro: 111,476 samples
- Accel: 112,981 samples
- GPS raw NMEA: 2,026 lines
- GPS parsed CSV: 1,194 rows
- GPS valid fix rows: 461
- First valid GPS fix: 2026-10-12T21:30:33Z
- Last valid GPS fix: 2026-10-12T21:34:23Z
- Satellites during valid fix: 4-5
- HDOP during valid fix: 1.63-2.68
- Note: this run used the old 2 GiB bag split and showed cache-loss warnings at splits/final flush. The recorder now defaults to an 8 GiB split size to reduce split-related loss.

Note: Some `metadata.yaml` files showed inconsistent top-level vs per-file message totals. `ros2 bag info` and direct SQLite message counts agree on the useful per-topic counts.
