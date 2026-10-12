#!/usr/bin/env bash
set -euo pipefail

PROJECT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
KERNEL_RELEASE="$(uname -r)"
MODULE_SRC="$PROJECT_DIR/drivers/pl2303-${KERNEL_RELEASE}.ko"
MODULE_DIR="/lib/modules/${KERNEL_RELEASE}/kernel/drivers/usb/serial"
MODULE_DST="$MODULE_DIR/pl2303.ko"
UDEV_RULE="/etc/udev/rules.d/99-globalsat-bu353n.rules"

if [[ ! -f "$MODULE_SRC" ]]; then
  echo "Missing module: $MODULE_SRC" >&2
  echo "Build or copy the matching pl2303 module before running this installer." >&2
  exit 1
fi

echo "Installing GlobalSat BU-353N PL2303 driver for kernel: $KERNEL_RELEASE"
sudo mkdir -p "$MODULE_DIR"
sudo cp "$MODULE_SRC" "$MODULE_DST"
sudo depmod -a "$KERNEL_RELEASE"
sudo modprobe pl2303

echo 'SUBSYSTEM=="tty", ATTRS{idVendor}=="067b", ATTRS{idProduct}=="23a3", SYMLINK+="gps0", MODE="0666"' | \
  sudo tee "$UDEV_RULE" >/dev/null
sudo udevadm control --reload-rules
sudo udevadm trigger

echo "Installed $MODULE_DST"
echo "Unplug and replug the BU-353N if /dev/gps0 does not appear immediately."
echo "Check with: ls -l /dev/gps0 /dev/ttyUSB*"
