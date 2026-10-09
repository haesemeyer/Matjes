# Z Calibration View: Specification

This document describes the `ZCalibrationView` user control in MatjesImager and records where it departs from the design guidelines in *Z_Calibration_View.docx*.

## Purpose

Calibrate, for each of the two light sheets, the linear relationship between the objective piezo position (µm) and the command voltage of that sheet's z (Y) mirror:

```
V_sheet = Slope * um_piezo + Offset
```

The result is one `LinearZConverter` per sheet. Linearity of galvo voltage versus sheet z was confirmed beforehand for this optical layout, so a straight-line fit is sufficient.

## User workflow

1. The view starts the hardware like `TestView`: all AOs are zeroed, the PPC001 piezo controller is configured, the camera is armed and the idle scan is started at 100 Hz.
2. The user moves the piezo to a depth inside the brain (slider or text box).
3. The user focuses both sheets with the Z1/Z2 sliders, text boxes or the fine `-`/`+` buttons until the image is maximally sharp.
4. *Add calibration point* stores the piezo position, both sheet voltages and a snapshot of the current camera frame in the first free of five slots.
5. Steps 2 to 4 are repeated for the remaining slots. From the second point on, both fits update live.
6. The user checks R² and the per-point residuals, clears and redoes any flagged point, then presses *Accept calibration*.

## Layout

| Region | Contents |
| --- | --- |
| Left column | Piezo position, sheet 1 and sheet 2 z-position (slider, text box, fine step buttons), fine step size, *Sheets follow fit* toggle, sheet extents (as in TestView), frame rate and frame counter |
| Centre | Live camera stream (`EZImage` + `EZImageSource_LH`) with the vertical 16-bit floor/ceiling `RangeSlider` next to it, as in TestView |
| Right column | Fit plot (points and fit line per sheet), slope, offset and R² per sheet, residual warning threshold, *Accept calibration* |
| Bottom row | Five calibration slots, each with snapshot, piezo position, both sheet voltages, both residuals, *Go to* and *Clear* |
| Footer | *Add calibration point*, *Clear all*, status/guidance message |

A slot gets a red border when either of its residuals exceeds the warning threshold.

## Fit

* Ordinary least squares of sheet voltage on piezo position (`MatjesUtils.LinearFit`), giving `Slope` and `Offset` directly in the form `LinearZConverter` expects.
* The piezo position is the independent variable because, in this procedure, it is set precisely by the user, while the in-focus sheet voltage is judged by eye and carries the error. Least squares minimises error in the dependent variable, so this is the correct orientation for this workflow, and no inversion is needed.
* Sums are centred on the means for numerical stability (piezo values are in the hundreds, voltages around one).
* R² = 1 − SS_res / SS_tot is reported per sheet.
* Residuals are shown as the equivalent focal shift in µm (`residual_V / Slope`), which is directly comparable to the sheet thickness. The default warning threshold is 2.5 µm (about a quarter of a ~10 µm sheet) and can be changed in the view.
* A fit needs at least two points with distinct piezo positions. Points closer than 1 µm in piezo position to an existing point are rejected. Accepting requires at least three points so R² and residuals are meaningful; five slots are provided as in the guidelines.

## Result

`AcceptCalibration` creates `Sheet1Converter` and `Sheet2Converter` (`LinearZConverter`) with voltage limits of ±5 V, matching the range of the z-mirror AO channels in `ScanControl`, and raises `CalibrationAccepted`. The view exposes its view model through `ZCalibrationView.Calibration`, so a host can subscribe to the event and hand the converters to whatever needs them. Making them available program-wide (and persisting them) is left for later, as the guidelines say.

## Code organisation

| File | Role |
| --- | --- |
| `MatjesImager/Views/ZCalibrationView.xaml(.cs)` | The `WindowAwareView` user control; code-behind only forwards clicks to the view model and disposes it on window close, as in TestView |
| `MatjesImager/ViewModels/ZCalibrationViewModel.cs` | All calibration logic: hardware start/stop, adding/removing points, fitting, residuals, follow-fit, accepting |
| `MatjesImager/ViewModels/CalibrationPoint.cs` | One calibration slot (piezo µm, both voltages, snapshot, residuals, warning flag) |
| `MatjesImager/Hardware/CameraStream.cs` | Camera acquisition and 16→8 bit conversion, extracted from TestViewModel |
| `MatjesUtils/LinearFit.cs` | Least squares fit with R² and residuals |
| `MatjesUtils/FitPlot.cs` | Lightweight plot control (two point series with fit lines) |

## Departures from the guidelines

1. **Camera logic in its own class.** Instead of copying the acquisition loop from `TestViewModel` into a second view model, it lives in `CameraStream`, which both views can share. The logic is the same (hardware trigger, ROI, MONO16, ipp copy and `ippiScaleC_16u8u_C1R` scaling with adjustable floor/ceiling, display every 10th frame). Two changes were made on the way: `Stop()` waits for the receiving thread to exit before the camera is closed, and the latest 8-bit frame is kept under a lock so the UI can copy it for a snapshot. `TestViewModel` is unchanged; it could switch to `CameraStream` later.
2. **Sheets follow the fit.** Once two points exist, moving the piezo places both sheets at their predicted voltage (can be switched off). The user then only fine-tunes, which makes points three to five much faster and immediately shows if the fit is off.
3. **Fine z control.** The ±2 V slider alone is too coarse for focusing (roughly 0.02 V per pixel, about 2 µm of focal shift with the placeholder slope of −4 V per 450 µm), so text boxes and fine-step buttons (default 0.005 V, about 0.5 µm with the current placeholder slope) were added.
4. **Point management.** Each slot can be cleared and redone, or revisited with *Go to*, which restores the stored piezo and sheet positions.
5. **Accept with three or more points** rather than strictly five.
6. **Voltage limits ±5 V** for the generated converters instead of the ±10 V used by the placeholder converters in TestViewModel, since the z AO channels are created with a ±5 V range and writes outside it would fail.
7. **MainWindow hosts `ZCalibrationView`** in place of `TestView`, which is commented out. Both views open the camera and DAQ boards in their constructors, so they cannot be hosted at the same time.

## Not yet done

* Persisting the calibration and passing it to other parts of MatjesImager.
* The code has not been compiled or run: the build needs Windows with the DCAM, NI-DAQmx and Kinesis libraries.
