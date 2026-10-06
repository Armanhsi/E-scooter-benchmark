# Vibration Analysis

Field-vibration analysis for the VR e-scooter simulator: processing of the IMU recordings collected on real surfaces, and validation of the vibration signal used by the simulator.

**Author:** Sarvin Chitsaz

---

## Structure

```
vibration_analysis/
├── README.md                     this file
├── requirements.txt              Python dependencies for all analyses
├── data/                         field recordings shared by all analyses
└── 01_quantitative_validation/   Phase 2 signal processing and field surface separability
    ├── README.md
    ├── quantitative_validation.ipynb
    └── results/
```

Each analysis lives in its own numbered folder with its own README, notebook(s) and `results/`. All analyses read data from `data/`.

---

## Analyses

| Folder | Description | Status |
|---|---|---|
| [`01_quantitative_validation`](01_quantitative_validation/) | Gravity removal, band-limiting, motion mask, PSD, band energies, event rate, sidewalk vs asphalt separability, spectral content of the current Unity command | Done on current recordings; ready for the new data |

---

## Data

| File | Surface | Duration | Sampling |
|---|---|---|---|
| `imu_accel_sidewalk_cafe.csv` | Sidewalk | 549 s | ~200 Hz |
| `imu_accel_cafe_asphalt.csv` | Asphalt | 593 s | ~200 Hz |

Recorded on Water Street, Charlottesville, VA, with the IMU of an Intel RealSense D435i mounted on the e-scooter handlebar.
Columns: `timestamp_ns`, `time_sec` (s), `ax`, `ay`, `az` (m/s²).

New recordings (e.g. deck-mounted runs from the next data collection) go into `data/` and are listed in cell 1 of the notebook.

---

## Setup

```bash
pip install -r requirements.txt
```

Tested with Python 3.11.
