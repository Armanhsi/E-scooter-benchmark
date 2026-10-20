# E-Scooter Surface Classification from IMU

Classifies whether the e-scooter is riding on **sidewalk** or **asphalt** using **only the IMU acceleration**.
Camera frames are used only to create the training labels; the model never sees images.

---

## Contents

| File | Description |
|---|---|
| `surface_classification.ipynb` | Full pipeline: labelled windows, 1D-CNN training, leave-one-recording-out test, final model, results |
| `requirements.txt` | Python dependencies |
| `results/` | Figures produced by the notebook |

## How to run

The notebook is written for **Google Colab** (GPU runtime recommended) and reads its inputs from Google Drive.
Set `drive_dir` / `data_dir` in cell 2 if the files are in a different place, then *Run all*.

For a local run, remove the `google.colab` lines in cell 1 and point `data_dir` to the local folder:

```bash
pip install -r requirements.txt
```

**Inputs** (per recording folder): `accel.csv`, `gyro.csv`.
**Labels**: `classification/frame_surface_labels.csv` (camera-frame surface labels, one frame every 2 s) and `classification/manual_labels.csv` (hand-labelled frames, used for the final check).

---

## What was done

**Starting point.** A first model was trained on the earlier recordings with the sensor on the handlebar (one recording per surface). It reached about 70% within those recordings but failed on the new recordings, because the sensor mounting changed. Those recordings are therefore not used. The four new recordings mix sidewalk and asphalt and have no surface labels, so the work was done in three steps.

### Step 1 – Labels from the camera

The RGB camera was recorded together with the IMU on the same clock, so every moment has both an image and the vibration.

- One frame every 2 s was extracted from each recording (~1250 frames).
- Frames that do not show the road (indoors, under a desk, sky only) were removed with CLIP.
- 150 frames were labelled by hand (sidewalk / asphalt), looking only at the path directly in front of the scooter. Brick pavers count as sidewalk.
- A classifier on CLIP features of these frames labelled all remaining frames (5-fold balanced accuracy 0.91).

The result is a table with the surface for every 2 s of every recording. These labels are only the answer key: **the IMU model never sees images**.

### Step 2 – Training on vibration only

The IMU signal was cut into 12 s windows. Each window received the label of the camera frames inside it and was dropped if the surface changed within the window (577 labelled windows). A 1D-CNN was trained on the vertical and horizontal acceleration of these windows.

### Step 3 – Testing on unseen recordings

The model was trained four times, each time on three recordings and tested on the fourth, which it had never seen (leave-one-recording-out). Each test was repeated with 3 seeds and averaged. Recall is reported separately for sidewalk and asphalt. As an independent check, the predictions were also compared with the 150 hand-labelled frames only.

### Experiments

- Window length 4, 8 and 12 s: sidewalk recall rose from 0.72 to 0.85 with longer windows.
- Adding the gyroscope reduced performance on unseen recordings, so only the accelerometer is used.

---

## Notebook cells

| Cell | Step |
|---|---|
| 1 | Libraries, mount Google Drive |
| 2 | Resample to 200 Hz and flag USB gaps; gravity direction from a 0.3 Hz low-pass; band-pass 0.5–80 Hz; split vibration into **vertical** and **horizontal** components (independent of sensor mounting angle); cut into 12 s windows (2 s step); drop stationary and gap windows; label each window from the camera frames inside it (window kept only if all frames agree) |
| 3 | 1D-CNN (4 conv blocks, ~131k parameters), augmentation (random crop, amplitude ×0.8–1.25, noise), class-weighted loss, AdamW + cosine LR, 40 epochs |
| 4 | **Leave-one-recording-out**: train on 3 recordings, test on the unseen 4th; 3 seeds averaged |
| 5 | Final model trained on all recordings, saved as `surface_cnn_final.pt` (weights of the 3 seeds, normalization, settings) |
| 6 | Results: per-recording and pooled metrics, confusion matrix, ablation, vibration spectrum, prediction timelines |
| 7 | Camera frames next to IMU predictions (correct and wrong examples) |
| 8 | Check against the hand-labelled frames only |

Windows are not split randomly between training and test: consecutive windows overlap by 10 s and would leak into the test set.

---

## Results

Leave-one-recording-out (model never saw the test recording), 12 s windows, acceleration only.

| Recording | Sidewalk recall | Asphalt recall | Accuracy |
|---|---|---|---|
| scooter_15min | 0.824 | 1.000 | 0.960 |
| lab_to_home | 1.000 (n = 14) | 0.649 | 0.679 |
| gps_outdoor | 0.745 | – (no asphalt) | 0.745 |
| field_recording | 0.816 | 1.000 | 0.922 |

| | Accuracy | Balanced accuracy | Sidewalk recall | Asphalt recall |
|---|---|---|---|---|
| All 577 windows (camera labels) | 0.816 | 0.812 | 0.785 | 0.839 |
| 122 hand-labelled frames | 0.885 | 0.887 | 0.844 | 0.931 |

**Ablation** (mean recall over recordings):

| Setting | Sidewalk | Asphalt |
|---|---|---|
| 4 s windows | 0.715 | 0.857 |
| 8 s windows | 0.791 | 0.875 |
| **12 s windows** | **0.846** | **0.883** |
| 12 s + gyro | 0.824 | 0.802 |

### Findings

- Longer windows steadily improve sidewalk detection, consistent with the periodic impacts of sidewalk slab joints.
- The vibration spectra of the two surfaces largely overlap; the model relies on the temporal pattern of impacts rather than vibration intensity.
- Adding the gyroscope reduced generalization to unseen recordings.
- Weakest case: asphalt in `lab_to_home` (recorded at dusk, high speed).
- Some "errors" are label errors (e.g. a parking lot labelled as sidewalk and predicted as asphalt).

### Figures

| File | Content |
|---|---|
| `results/training_curves.png` | Test balanced accuracy per epoch for each split and seed |
| `results/results_summary.png` | Recall per recording, confusion matrix, ablation, vertical-vibration spectrum |
| `results/results_timelines.png` | IMU prediction over time vs camera label for each recording |
| `results/results_frames_grid.png` | Camera frames next to correct and wrong IMU predictions |

### Limitations

Four recordings from one area with one sensor setup; camera labels are about 90% accurate.
The window length was chosen on the same test recordings, so all lengths are reported as an ablation.
