namespace PhotoCropper.Core.Models;

/// <summary>
/// Up to four colour components (BGR or HSV, as OpenCV uses them). Kept as its own type, with the V0–V3 names the
/// session file has always used, so saved settings (e.g. a picked background colour) load the same whatever the
/// OpenCV wrapper; <see cref="Common.CvInterop"/> converts it to OpenCV's scalar.
/// </summary>
public readonly record struct ColorScalar(double V0, double V1, double V2, double V3 = 0);
