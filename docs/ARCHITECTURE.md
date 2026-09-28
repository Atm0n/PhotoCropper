# PhotoCropper Architecture

PhotoCropper is built using a modular `.NET 10` architecture that cleanly separates the image processing logic from the user interfaces. The system uses `Emgu.CV` (an OpenCV wrapper) for high-performance computer vision tasks.

## High-Level System Overview

The solution is divided into three primary projects:

1. **`PhotoCropper.Core`**: The headless engine containing all OpenCV logic, models, AI orientation, and file I/O.
2. **`PhotoCropper.Gui`**: The Avalonia-based desktop application (MVVM).
3. **`PhotoCropper.Cli`**: The unattended console application for batch processing.

```mermaid
flowchart TD
    subgraph UI Layer
        GUI[PhotoCropper.Gui <br/> Avalonia UI / MVVM]
        CLI[PhotoCropper.Cli <br/> Console / Spectre.Console]
    end

    subgraph Core Layer
        Engine[PhotoCropperEngine]
        Scanner[ScannerService]
        AutoTune[AutoTuneService]
        Orient[AutoOrientationService]
        Extract[PhotoExtractionEngine]
    end

    GUI --> Engine
    CLI --> Engine
    
    Engine --> Scanner
    Engine --> AutoTune
    Engine --> Orient
    Engine --> Extract
    
    subgraph External Dependencies
        OpenCV[Emgu.CV / OpenCV 5]
        ONNX[YuNet ONNX Model]
    end
    
    AutoTune --> OpenCV
    Extract --> OpenCV
    Orient --> ONNX
```

## Auto-Tuning Detection Pipeline

The core capability of PhotoCropper is automatically identifying physical photos placed arbitrarily on a scanner bed. Because scanners introduce different noise, light-bleed, and contrast levels, the `AutoTuneService` performs a combinatorial sweep across multiple thresholds to find the best extraction parameters.

```mermaid
sequenceDiagram
    participant Engine as PhotoCropperEngine
    participant Tune as AutoTuneService
    participant CV as Emgu.CV
    participant Filter as CandidateResolutionFilter

    Engine->>Tune: Tune(scanImage, options)
    activate Tune
    
    loop Parameter Sweep (80 Combinations)
        Tune->>CV: Generate Edge Map (Canny)
        Tune->>CV: Background Subtraction (HSV)
        Tune->>CV: Extract Contours
        
        CV-->>Tune: Raw Rectangles
        
        Tune->>Filter: FilterCandidates(rects)
        Filter-->>Tune: Valid Photo Candidates
        
        Note over Tune: Calculate heuristic score based on<br/>candidate count, area, and aspect ratio variance
    end
    
    Tune-->>Engine: AutoTuneResult (Best Parameters)
    deactivate Tune
```

## AI Auto-Orientation Heuristics

Once photos are extracted from the raw scan bed, they are often rotated (e.g. 90° or upside down). The `AutoOrientationService` uses a dual-pass approach to right the images:

1. **Primary AI Pass**: Uses the `YuNet` ONNX facial detection model to find human faces and measure their geometric rotation.
2. **Secondary Heuristic Pass**: If no faces are found, it analyzes the landscape using HSV ranges to find skies, water, and vegetation.

```mermaid
stateDiagram-v2
    [*] --> ExtractPhoto
    ExtractPhoto --> DetectFace: YuNet ONNX
    
    DetectFace --> RotateFaceUpright: Face Found (Confidence > 0.6)
    RotateFaceUpright --> Save
    
    DetectFace --> AnalyzeLandscape: No Faces Found
    
    state AnalyzeLandscape {
        direction LR
        SplitImage --> ComputeSky
        SplitImage --> ComputeWater
        SplitImage --> ComputeVegetation
        ComputeSky --> ScoreRotations
        ComputeWater --> ScoreRotations
        ComputeVegetation --> ScoreRotations
    }
    
    AnalyzeLandscape --> RotateLandscapeUpright: Clear Heuristic Winner
    RotateLandscapeUpright --> Save
    
    AnalyzeLandscape --> PreserveRotation: Ambiguous Scene
    PreserveRotation --> Save
    
    Save --> [*]
```

## Memory Management & Performance

Because working with uncompressed 600 DPI scans takes immense memory, the Core utilizes strict memory disposal patterns:
- Images are wrapped in `using var` statements aggressively.
- `GC.Collect()` triggers are hinted post-batch.
- Large processing arrays are pinned via `GCHandle` when interfacing between native C# memory and unmanaged OpenCV memory (e.g., ONNX extraction buffers).
- Background masks are calculated on heavily downscaled `Cv32F` representations of the image, running in under `<50ms`, while the final physical crop operation runs on the original full-resolution byte matrix.
