# E-Scooter Surface Classification from IMU — Full Dataset

Extends [`escooter-surface-classification`](../escooter-surface-classification/) from 4 to all field recordings.
Same idea: the 1D-CNN classifies **sidewalk** vs **asphalt** from **IMU acceleration only**; camera frames are used only to create the labels.

New in this version:

- all recordings (11 after adding 26 and 28 Oct), read straight from the ROS bag zips, including damaged bags;
- hand labels for the recordings up to 21 Oct, and a CLIP-based frame labeller trained on them (26 and 28 Oct are labelled by it without hand labels of their own);
- GPS speed and position added to every window **for checking only** (does speed help? where were the errors?). The final model does **not** use GPS;
- the IMU windows saved here are the input of [`escooter-vibration-speed-model`](../escooter-vibration-speed-model/).

---

## Contents

| File | Description |
|---|---|
| `surface_full_dataset.ipynb` | Full pipeline with outputs: extraction, labels, windows, 1D-CNN, leave-one-recording-out test, GPS check, final model, results |
| `requirements.txt` | Python dependencies |
| `results/` | Figures produced by the notebook |

## How to run

Written for **Google Colab** (GPU runtime recommended) with the data on Google Drive (`MyDrive/scooter_data`).
Set `drive_dir` / `data_dir` in cell 2 if the files are elsewhere, then run the cells in order.
Every step is cached on Drive (extracted CSVs, frames, CLIP embeddings, hand labels, model checkpoints), so a rerun only processes what is new.

**Adding a recording:** add one line to `recs` in cell 2 and run again; cell 7 asks for 40 hand labels of the new recording.

**Inputs**

| Input | Where |
|---|---|
| ROS bag zips (`<recording>.zip`, D435i accel/gyro/color image, GPS if recorded) | `MyDrive` or `MyDrive/Data` |
| Phone GPS (Sensor Logger zips) for recordings without scooter GPS | `MyDrive/gps_datasets` |
| Earlier hand labels | `scooter_data/classification/manual_labels.csv` |

Outputs are written to `scooter_data/classification_full/`.

---

## What was done

### Step 1 – Data from the ROS bags (cells 3–5)

Each recording is extracted once: accelerometer and gyroscope (~200 Hz), GPS if the bag has it, and one camera frame every 2 s.
Some recordings were not closed cleanly (no `metadata.yaml`, damaged `.db3`); these are rebuilt with SQLite `.recover` and read row by row, skipping corrupted rows.
Recordings 16–21 Oct have no GPS in the bag; their GPS comes from the phone (Sensor Logger).

### Step 2 – Labels from the camera (cells 6–8)

- CLIP (ViT-L/14) embeddings of every frame, full frame and the path right in front of the scooter; frames that do not show the road are dropped.
- 318 hand-labelled frames (sidewalk 119, asphalt 199), looking only at the path in front of the scooter.
- A logistic regression on the CLIP features labels all other frames: **5-fold accuracy 0.947**; on recordings it never saw, 0.68–1.00.

### Step 3 – IMU windows (cell 9)

200 Hz resampling, gravity direction from a 0.3 Hz low-pass, band-pass 0.5–80 Hz, vertical and horizontal vibration (independent of the sensor mounting angle), 12 s windows with a 2 s step.
A window is kept only if it is moving, has almost no gaps, and all confident camera labels inside it agree.
With 11 recordings: **1061 labelled windows**.

### Step 4 – Model and test (cells 10–14)

1D-CNN on vertical and horizontal acceleration, 3 seeds. Leave-one-recording-out: each recording with enough windows is the test set once, the model is trained on all the others.
Recordings with fewer than 20 labelled windows (16–21 Oct, damaged or short) are used for training only.

### Step 5 – GPS check (cells 12 and 15)

The same test on the windows that have GPS speed, three ways: acceleration only, acceleration + speed, speed only.

---

## Notebook cells

| Cell | Step |
|---|---|
| 1–2 | Libraries, Google Drive, paths and the list of recordings |
| 3 | Extract each recording from its ROS bag (with recovery of damaged bags) |
| 4–5 | Phone GPS for recordings without scooter GPS; GPS coverage check |
| 6 | CLIP embeddings, invalid (non-road) frames |
| 7 | Click tool for hand labels |
| 8 | Frame labels from CLIP + hand labels |
| 9 | IMU windows with labels (and GPS speed/position for checking) |
| 10–11 | 1D-CNN, leave-one-recording-out |
| 12 | GPS check: accel vs accel + speed vs speed only |
| 13 | Final model (acceleration only), saved as `surface_cnn_final.pt` |
| 14 | Results and figures |
| 15 | Map of the routes with the test windows (`surface_map.html`) |
| 16 | Camera frames next to IMU predictions |
| 17 | Check against the hand-labelled frames only |

---

## Results

> The extraction, labels and windows (cells 1–9) were run with all 11 recordings.
> **The training and results below are from the run on the first 9 recordings**, before 26 and 28 Oct were added.
> On 28 Oct the camera points up and many camera labels are wrong; check them by eye first (label review in `escooter-vibration-speed-model`) before retraining.

Leave-one-recording-out, 12 s windows, acceleration only, 862 test windows (sidewalk 349, asphalt 513).

| Recording | Sidewalk recall | Asphalt recall | Accuracy |
|---|---|---|---|
| scooter_15min | 0.971 | 0.992 | 0.988 |
| lab_to_home | 1.000 (n = 17) | 0.788 | 0.810 |
| gps_outdoor | 0.738 | – (no asphalt) | 0.738 |
| field_recording | 0.900 | 1.000 | 0.957 |
| field_1014 | 0.843 | 0.970 | 0.922 |

| | Accuracy | Balanced accuracy | Sidewalk recall | Asphalt recall |
|---|---|---|---|---|
| All 862 windows (camera labels) | 0.886 | 0.877 | 0.828 | 0.926 |
| 162 hand-labelled frames | 0.944 | 0.940 | 0.892 | 0.989 |

The 4-recording version reached accuracy 0.816; with more recordings to train on, the result here is clearly higher (the test sets differ, so the numbers are not a strict comparison).

### GPS check (windows with GPS speed, 431 windows)

| Input | Sidewalk recall | Asphalt recall | Accuracy |
|---|---|---|---|
| **Acceleration only** | **0.727** | **0.873** | **0.814** |
| Acceleration + speed | 0.698 | 0.865 | 0.798 |
| Speed only | 0.599 | 0.622 | 0.613 |

- Adding GPS speed does not help, so **the final model uses acceleration only**. GPS is kept for checking and for the VR vibration work.
- Speed alone is weak but not random, because we rode slower on sidewalks (median 3.7 m/s) than on asphalt (5.0 m/s).
- The model is weakest at low speed (sidewalk 0.73 below 3 m/s) and almost perfect on asphalt above 4.5 m/s.

### Figures

| File | Content |
|---|---|
| `results/training_curves.png` | Test balanced accuracy per epoch for each split and seed |
| `results/results_summary.png` | Recall per recording, confusion matrix, vibration spectrum |
| `results/results_timelines.png` | IMU prediction over time vs camera label for each recording |
| `results/results_frames_grid.png` | Camera frames next to correct and wrong IMU predictions |
| `results/surface_map.html` | Interactive map: routes, camera labels, IMU predictions and errors. GitHub shows only its code; download it and open it in a browser (background tiles from Esri, no key needed) |

### Limitations

- Camera labels are not perfect (about 95 % on hand-labelled frames, lower on recordings where the camera does not see the ground).
- Recordings 16–21 Oct and 26 Oct lost most of their camera/IMU messages (camera at 1280×720), so they give only a few windows. 28 Oct (640×480, 15 fps) is complete.
- One area, one scooter, one sensor setup.
