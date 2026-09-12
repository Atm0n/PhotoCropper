using PhotoCropper.Models;

namespace PhotoCropper.Detection;

public sealed record AutoTuneResult(
    DetectionOptions BestOptions,
    int PhotoCount,
    double Score,
    bool Improved
);
