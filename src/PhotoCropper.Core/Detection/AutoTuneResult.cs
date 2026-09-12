using PhotoCropper.Core.Models;

namespace PhotoCropper.Core.Detection;

public sealed record AutoTuneResult(
    DetectionOptions BestOptions,
    int PhotoCount,
    double Score,
    bool Improved
);
