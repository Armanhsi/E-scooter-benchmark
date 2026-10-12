#!/usr/bin/env python3
import argparse
import csv
import sqlite3
from pathlib import Path

from rclpy.serialization import deserialize_message
from sensor_msgs.msg import Imu


def topic_id(conn, topic_name):
    row = conn.execute("select id from topics where name = ?", (topic_name,)).fetchone()
    return None if row is None else row[0]


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("bag_dir", type=Path)
    parser.add_argument("csv_path", type=Path)
    parser.add_argument("--topic", default="/camera/d435i/accel/sample")
    args = parser.parse_args()

    db_paths = sorted(args.bag_dir.glob("*.db3"))
    if not db_paths:
        raise RuntimeError(f"No .db3 files found in {args.bag_dir}")

    args.csv_path.parent.mkdir(parents=True, exist_ok=True)
    first_timestamp = None
    rows_written = 0

    with args.csv_path.open("w", newline="") as csv_file:
        writer = csv.writer(csv_file)
        writer.writerow(
            [
                "bag_time_ns",
                "time_from_start_s",
                "header_stamp_ns",
                "linear_accel_x_mps2",
                "linear_accel_y_mps2",
                "linear_accel_z_mps2",
            ]
        )

        for db_path in db_paths:
            with sqlite3.connect(db_path) as conn:
                tid = topic_id(conn, args.topic)
                if tid is None:
                    continue
                for timestamp, data in conn.execute(
                    """
                    select timestamp, data
                    from messages
                    where topic_id = ?
                    order by timestamp
                    """,
                    (tid,),
                ):
                    if first_timestamp is None:
                        first_timestamp = timestamp
                    msg = deserialize_message(data, Imu)
                    header_stamp_ns = msg.header.stamp.sec * 1_000_000_000 + msg.header.stamp.nanosec
                    writer.writerow(
                        [
                            timestamp,
                            f"{(timestamp - first_timestamp) / 1_000_000_000:.9f}",
                            header_stamp_ns,
                            f"{msg.linear_acceleration.x:.9f}",
                            f"{msg.linear_acceleration.y:.9f}",
                            f"{msg.linear_acceleration.z:.9f}",
                        ]
                    )
                    rows_written += 1

    print(f"Wrote {rows_written} rows to {args.csv_path}")


if __name__ == "__main__":
    main()
