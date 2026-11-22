using PhotoCropper.Trainer.Training;
using TorchSharp;
using static TorchSharp.torch;

namespace PhotoCropper.Trainer.Platform;

/// <summary>Where training runs (the NVIDIA GPU when it can, else the CPU) and the plan that suits it.</summary>
internal static class DeviceChoice
{
#if TORCH_CUDA
    public const string BuildBackend = "GPU";
#else
    public const string BuildBackend = "CPU";
#endif

    /// <returns>The device, its plan, and why it isn't the GPU (null when it is).</returns>
    public static (Device Device, TrainingPlan Plan, string? Reason) Choose(PlatformInfo platform)
    {
        ArgumentNullException.ThrowIfNull(platform);
        if (platform.Nvidia is not { } gpu)
        {
            return (CPU, TrainingPlan.Cpu, "No NVIDIA graphics card found. Integrated graphics (AMD, Intel) can't be used for training.");
        }
        if (!gpu.DriverIsRecentEnough)
        {
            return (CPU, TrainingPlan.Cpu, $"The NVIDIA driver is {gpu.DriverVersion}; training needs {NvidiaGpu.MinimumDriverMajor} or newer (CUDA 12.8). Update the driver from nvidia.com and run check again.");
        }
#if TORCH_CUDA
        if (!cuda.is_available())
        {
            return (CPU, TrainingPlan.Cpu, "The NVIDIA card was found, but CUDA could not be started. Updating the NVIDIA driver usually fixes this.");
        }
        return (CUDA, TrainingPlan.Gpu, null);
#else
        return (CPU, TrainingPlan.Cpu, "This copy was built without GPU support. Rebuild it on this computer (dotnet build picks the GPU build when an NVIDIA driver is installed).");
#endif
    }
}
