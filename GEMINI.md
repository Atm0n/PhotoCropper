# PhotoCropper AI Rules & Conventions

See `README.md` for full project architecture, documentation, and usage instructions.

## Core Conventions
- **Framework**: .NET 10 / Avalonia 12 / Emgu.CV (OpenCV wrapper).
- **Color Space**: Standardized on **BGR** internally (OpenCV default). The UI performs a single `Bgr2Bgra` conversion purely for display.
- **Testing Standards**: Maintain 100% pass rate in all test projects (`PhotoCropper.Core.Tests`, `PhotoCropper.Cli.Tests`, `PhotoCropper.Gui.Tests`). Uses **Shouldly** fluent assertions. Run tests with `dotnet run --project tests/PhotoCropper.Core.Tests/PhotoCropper.Core.Tests.csproj`, etc.
- **Code Style**:
  - Strictly **no `#region` / `#endregion`** directives.
  - Zero warnings and zero errors across the entire solution (`TreatWarningsAsErrors`).
  - Follow .NET code analysis guidelines (CA1062 null validations, IReadOnlyList return types).
- **Performance**:
  - Direct pointer-to-bitmap memory copy (`Bgra8888`) for high-DPI rendering.
  - Parallel extraction across CPU cores (`Parallel.For`).
  - Asynchronous background execution (`Task.Run`) for heavy image operations.
