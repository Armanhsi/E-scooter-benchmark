#!/usr/bin/env python3
import argparse
import math
import sqlite3
from pathlib import Path

import cv2
import numpy as np
from rclpy.serialization import deserialize_message
from sensor_msgs.msg import Image


def image_to_bgr(msg):
    data = np.frombuffer(msg.data, dtype=np.uint8)
    if msg.encoding in ("rgb8", "bgr8"):
        channels = 3
    elif msg.encoding in ("mono8", "8UC1"):
        channels = 1
    else:
        raise ValueError(f"Unsupported encoding: {msg.encoding}")

    row_bytes = msg.step
    expected_row_bytes = msg.width * channels
    image = data.reshape((msg.height, row_bytes))[:, :expected_row_bytes]
    image = image.reshape((msg.height, msg.width, channels))

    if msg.encoding == "rgb8":
        return cv2.cvtColor(image, cv2.COLOR_RGB2BGR)
    if msg.encoding == "bgr8":
        return image
    return cv2.cvtColor(image, cv2.COLOR_GRAY2BGR)


def topic_id(conn, topic_name):
    row = conn.execute("select id from topics where name = ?", (topic_name,)).fetchone()
    return None if row is None else row[0]


def nearest_message(db_path, topic_name, target_ns):
    with sqlite3.connect(db_path) as conn:
        tid = topic_id(conn, topic_name)
        if tid is None:
            return None
        before = conn.execute(
            """
            select timestamp, data
            from messages
            where topic_id = ? and timestamp <= ?
            order by timestamp desc
            limit 1
            """,
            (tid, target_ns),
        ).fetchone()
        after = conn.execute(
            """
            select timestamp, data
            from messages
            where topic_id = ? and timestamp >= ?
            order by timestamp asc
            limit 1
            """,
            (tid, target_ns),
        ).fetchone()

        candidates = [row for row in (before, after) if row is not None]
        if not candidates:
            return None
        return min(candidates, key=lambda row: abs(row[0] - target_ns))


def first_timestamp(db_paths, topic_name):
    first = None
    for db_path in db_paths:
        with sqlite3.connect(db_path) as conn:
            tid = topic_id(conn, topic_name)
            if tid is None:
                continue
            row = conn.execute(
                "select min(timestamp) from messages where topic_id = ?", (tid,)
            ).fetchone()
            if row and row[0] is not None:
                first = row[0] if first is None else min(first, row[0])
    if first is None:
        raise RuntimeError(f"No messages found for topic {topic_name}")
    return first


def make_contact_sheet(images, output_path, columns=3):
    thumb_w, thumb_h = 426, 320
    label_h = 34
    rows = math.ceil(len(images) / columns)
    sheet = np.full((rows * (thumb_h + label_h), columns * thumb_w, 3), 245, np.uint8)

    for index, (label, image) in enumerate(images):
        row = index // columns
        col = index % columns
        thumb = cv2.resize(image, (thumb_w, thumb_h), interpolation=cv2.INTER_AREA)
        y = row * (thumb_h + label_h)
        x = col * thumb_w
        sheet[y : y + thumb_h, x : x + thumb_w] = thumb
        cv2.putText(
            sheet,
            label,
            (x + 10, y + thumb_h + 23),
            cv2.FONT_HERSHEY_SIMPLEX,
            0.62,
            (20, 20, 20),
            2,
            cv2.LINE_AA,
        )

    cv2.imwrite(str(output_path), sheet)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("bag_dir", type=Path)
    parser.add_argument("output_dir", type=Path)
    parser.add_argument("--topic", default="/camera/d435i/color/image_raw")
    parser.add_argument("--start-sec", type=float, default=180.0)
    parser.add_argument("--end-sec", type=float, default=300.0)
    parser.add_argument("--samples", type=int, default=9)
    args = parser.parse_args()

    db_paths = sorted(args.bag_dir.glob("*.db3"))
    if not db_paths:
        raise RuntimeError(f"No .db3 files found in {args.bag_dir}")

    args.output_dir.mkdir(parents=True, exist_ok=True)
    start_ns = first_timestamp(db_paths, args.topic)
    sample_secs = np.linspace(args.start_sec, args.end_sec, args.samples)

    extracted = []
    manifest_lines = ["sample_sec,actual_sec,frame_file"]
    for sample_sec in sample_secs:
        target_ns = start_ns + int(sample_sec * 1_000_000_000)
        best = None
        for db_path in db_paths:
            row = nearest_message(db_path, args.topic, target_ns)
            if row is None:
                continue
            delta = abs(row[0] - target_ns)
            if best is None or delta < best[0]:
                best = (delta, row[0], row[1])
        if best is None:
            continue

        actual_sec = (best[1] - start_ns) / 1_000_000_000
        msg = deserialize_message(best[2], Image)
        image = image_to_bgr(msg)
        frame_name = f"frame_t{actual_sec:07.3f}s.jpg"
        frame_path = args.output_dir / frame_name
        cv2.imwrite(str(frame_path), image)
        label = f"t={actual_sec:6.1f}s ({actual_sec / 60:4.1f} min)"
        extracted.append((label, image))
        manifest_lines.append(f"{sample_sec:.3f},{actual_sec:.6f},{frame_name}")

    if not extracted:
        raise RuntimeError("No frames extracted")

    (args.output_dir / "manifest.csv").write_text("\n".join(manifest_lines) + "\n")
    make_contact_sheet(extracted, args.output_dir / "contact_sheet.jpg")
    print(args.output_dir / "contact_sheet.jpg")


if __name__ == "__main__":
    main()
