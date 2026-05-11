# PhotoCropper

An intelligent, cross-platform .NET 10 desktop application designed to automatically detect, straighten, and extract multiple photos from a single scanner bed image. Built with Avalonia UI and Emgu.CV (OpenCV).

## Key Features

- **High-Performance Architecture:**
  - **Direct Memory Rendering:** Skips slow PNG encoding/decoding by using a pointer-based pipeline directly from OpenCV to Avalonia's UI.
  - **Parallel Extraction:** Utilizes all CPU cores to simultaneously rotate and process multiple photos from a single scan.
  - **Asynchronous Processing:** All heavy operations (detection, rotation, refinement) run in background threads, keeping the UI silky smooth even with high-DPI scans.
- **Intelligent Auto-Detection:** Automatically finds photos on a scanner bed using HSV background profiling and morphological edge detection.
- **Zero-Loss Straightening:** Uses a "Local ROI" rotation with Cubic interpolation to perfectly straighten tilted photos without clipping edges or causing pixel displacement.
- **Interactive Refinement Mode:** An advanced overlay mode (Shortcut: `Ñ` or `N`) that uses Adaptive Border Trimming to perfectly shrink-wrap the crop box around the photo, removing stubborn white scanner margins.
- **Manual Cropping:** Draw directly on the original scan to extract custom regions. You can also manually draw refinement boxes in the Refinement Mode.
- **True Color Saving:** Automatically manages RGB-to-BGR color space conversion to guarantee saved JPEG files retain 100% of their original color accuracy without bluish artifacts.
- **Professional Dark GUI:** A sleek, fully responsive dark theme interface with intuitive carousels, status updates, and constraint-based image scaling.

## Keyboard Shortcuts

| Shortcut | Action |
|----------|--------|
| `Left / Right` | Navigate between cropped photos |
| `Up / Down` | Switch between original loaded scans |
| `R` | Rotate the current cropped photo 90° clockwise |
| `X` | Permanently delete the currently selected photo |
| `Ñ` or `N` | Enter Interactive Refinement Mode |
| `Enter` | Accept Refinement (while in Refinement Mode) |
| `Backspace` or `Esc` | Reject Refinement (while in Refinement Mode) |
