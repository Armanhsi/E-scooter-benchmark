#!/usr/bin/env python3
import argparse
import csv
import datetime as dt
import os
import signal
import sys
import termios
import time


BAUD_RATES = {
    4800: termios.B4800,
    9600: termios.B9600,
    19200: termios.B19200,
    38400: termios.B38400,
    57600: termios.B57600,
    115200: termios.B115200,
}


stop_requested = False


def handle_stop(signum, frame):
    global stop_requested
    stop_requested = True


def configure_serial(fd, baud):
    attrs = termios.tcgetattr(fd)
    attrs[0] = 0
    attrs[1] = 0
    attrs[2] = termios.CLOCAL | termios.CREAD | termios.CS8
    attrs[3] = 0
    attrs[4] = BAUD_RATES[baud]
    attrs[5] = BAUD_RATES[baud]
    attrs[6][termios.VMIN] = 0
    attrs[6][termios.VTIME] = 10
    termios.tcsetattr(fd, termios.TCSANOW, attrs)


def nmea_checksum_ok(line):
    if "*" not in line:
        return False
    body, checksum = line[1:].split("*", 1)
    calc = 0
    for char in body:
        calc ^= ord(char)
    try:
        return calc == int(checksum[:2], 16)
    except ValueError:
        return False


def parse_coord(raw, hemi):
    if not raw or not hemi:
        return ""
    split_at = 2 if hemi in ("N", "S") else 3
    degrees = float(raw[:split_at])
    minutes = float(raw[split_at:])
    value = degrees + minutes / 60.0
    if hemi in ("S", "W"):
        value *= -1.0
    return f"{value:.8f}"


def parse_hhmmss(raw_time, raw_date=""):
    if not raw_time:
        return ""
    try:
        hour = int(raw_time[0:2])
        minute = int(raw_time[2:4])
        second = int(raw_time[4:6])
        microsecond = 0
        if "." in raw_time:
            fraction = raw_time.split(".", 1)[1]
            microsecond = int((fraction + "000000")[:6])
        if raw_date:
            day = int(raw_date[0:2])
            month = int(raw_date[2:4])
            year_two_digit = int(raw_date[4:6])
            year = 1900 + year_two_digit if year_two_digit >= 80 else 2000 + year_two_digit
            stamp = dt.datetime(year, month, day, hour, minute, second, microsecond, tzinfo=dt.timezone.utc)
            return stamp.isoformat().replace("+00:00", "Z")
        return f"{hour:02d}:{minute:02d}:{second:02d}.{microsecond:06d}Z"
    except (ValueError, IndexError):
        return ""


def parse_sentence(line):
    if not line.startswith("$") or not nmea_checksum_ok(line):
        return None
    body = line[1:].split("*", 1)[0]
    fields = body.split(",")
    sentence = fields[0][-3:]

    if sentence == "GGA" and len(fields) >= 15:
        return {
            "sentence": fields[0],
            "gps_time_utc": parse_hhmmss(fields[1]),
            "latitude_deg": parse_coord(fields[2], fields[3]),
            "longitude_deg": parse_coord(fields[4], fields[5]),
            "fix_quality": fields[6],
            "satellites": fields[7],
            "hdop": fields[8],
            "altitude_m": fields[9],
            "speed_knots": "",
            "speed_mps": "",
            "course_deg": "",
        }

    if sentence == "RMC" and len(fields) >= 12:
        speed_knots = fields[7]
        speed_mps = ""
        if speed_knots:
            speed_mps = f"{float(speed_knots) * 0.514444:.3f}"
        return {
            "sentence": fields[0],
            "gps_time_utc": parse_hhmmss(fields[1], fields[9]),
            "latitude_deg": parse_coord(fields[3], fields[4]),
            "longitude_deg": parse_coord(fields[5], fields[6]),
            "fix_quality": fields[2],
            "satellites": "",
            "hdop": "",
            "altitude_m": "",
            "speed_knots": speed_knots,
            "speed_mps": speed_mps,
            "course_deg": fields[8],
        }

    return None


def main():
    parser = argparse.ArgumentParser(description="Log USB GPS NMEA data to raw NMEA and parsed CSV files.")
    parser.add_argument("output_dir", help="Directory for gps_raw.nmea and gps_parsed.csv")
    parser.add_argument("--device", default="/dev/gps0", help="Serial GPS device, usually /dev/gps0 or /dev/ttyUSB0")
    parser.add_argument("--baud", type=int, default=4800, choices=sorted(BAUD_RATES), help="GPS serial baud rate")
    parser.add_argument("--duration", type=float, default=0, help="Seconds to record; 0 means until Ctrl-C")
    args = parser.parse_args()

    signal.signal(signal.SIGINT, handle_stop)
    signal.signal(signal.SIGTERM, handle_stop)

    os.makedirs(args.output_dir, exist_ok=True)
    raw_path = os.path.join(args.output_dir, "gps_raw.nmea")
    csv_path = os.path.join(args.output_dir, "gps_parsed.csv")

    fd = os.open(args.device, os.O_RDONLY | os.O_NOCTTY)
    configure_serial(fd, args.baud)
    serial_file = os.fdopen(fd, "rb", buffering=0)

    fields = [
        "system_time_ns",
        "system_time_iso",
        "sentence",
        "gps_time_utc",
        "latitude_deg",
        "longitude_deg",
        "fix_quality",
        "satellites",
        "hdop",
        "altitude_m",
        "speed_knots",
        "speed_mps",
        "course_deg",
        "raw_nmea",
    ]

    started = time.monotonic()
    buffer = b""
    with open(raw_path, "a", encoding="ascii", newline="") as raw_file, open(csv_path, "a", encoding="utf-8", newline="") as csv_file:
        writer = csv.DictWriter(csv_file, fieldnames=fields)
        if csv_file.tell() == 0:
            writer.writeheader()

        print(f"GPS logging started: {args.device} at {args.baud} baud")
        print(f"Raw NMEA: {raw_path}")
        print(f"Parsed CSV: {csv_path}")
        while not stop_requested:
            if args.duration and time.monotonic() - started >= args.duration:
                break
            chunk = serial_file.read(256)
            if not chunk:
                continue
            buffer += chunk
            while b"\n" in buffer:
                raw_line, buffer = buffer.split(b"\n", 1)
                line = raw_line.decode("ascii", errors="ignore").strip()
                if not line:
                    continue
                now_ns = time.time_ns()
                now_iso = dt.datetime.now(dt.timezone.utc).isoformat().replace("+00:00", "Z")
                raw_file.write(f"{now_ns},{now_iso},{line}\n")
                raw_file.flush()
                parsed = parse_sentence(line)
                if parsed:
                    parsed["system_time_ns"] = now_ns
                    parsed["system_time_iso"] = now_iso
                    parsed["raw_nmea"] = line
                    writer.writerow(parsed)
                    csv_file.flush()

    print("GPS logging stopped.")


if __name__ == "__main__":
    try:
        main()
    except FileNotFoundError as exc:
        print(f"GPS device not found: {exc.filename}", file=sys.stderr)
        sys.exit(2)
    except PermissionError as exc:
        print(f"Permission denied opening GPS device: {exc.filename}", file=sys.stderr)
        sys.exit(3)
