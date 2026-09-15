# VFISV — managed C# port

This is a native .NET 8 port of the mixed Fortran/C/Python VFISV v0.9 archive.
It does not load the original `.so` files and does not require Python, NumPy,
LAPACK, BLAS, MPI, GCC, or gfortran. It uses the managed CSharpFITS 1.1.0 NuGet
package for FITS/HDU and binary-table parsing.

## What was ported

- Milne–Eddington / Unno–Rachkovsky forward synthesis and analytic response
  functions from `forward.f90`.
- The Levenberg–Marquardt inversion, regularization, limits, resets, weak-field
  initial guess, parameter transformations, uncertainty estimates, and
  convergence flags from the Fortran inversion modules.
- The Hui–Armstrong–Wray complex Voigt approximation from `voigt.f`. Calling
  the rational approximation directly replaces the generated 3,000-row
  Taylor lookup source in `voigt_init.f90`.
- HMI filter calibration, bilinear map interpolation, optical-element
  transmission model, invalid-pixel mapping, work assignment, Stokes
  reorganization, and result extraction from `vfisv_subprogs.c` and
  `vfisv_tools.py`.
- JSOC record discovery and segment downloading, JSOC epoch conversion,
  CSharpFITS-based image/table parsing, RICE_1 decompression for HMI segments,
  and FITS output from the Python orchestration layer.
- `Parallel.For`-friendly, thread-safe APIs in place of MPI process globals.

The calibration binary/FITS files from `basic_data` are included unchanged.
The two transmission curves that were hard-coded C arrays were mechanically
converted to CSV (`blocker_profile.csv` and `front_window_profile.csv`).

## Build and verify

Install the .NET 8 SDK, then run:

```bash
dotnet build Vfisv.sln -c Release
dotnet run --project tests/Vfisv.Tests -c Release
```

The test executable has no testing-framework dependency. It checks the Voigt
symmetries, FITS byte order/round trip, bundled phase-map and filter generation,
analytic response functions against finite differences, a saved result from the
original Fortran forward model, and a public inversion status path.

## Use the library

Filters are represented as `[fullWavelength, filterBin]`. Observed and
scattered Stokes values use the original flat layout: all I bins, then Q, U,
and V. The ten-element model vectors retain the original ordering.

```csharp
using Vfisv;

var engine = new VfisvEngine();
var result = engine.Invert(new InversionRequest
{
    Filters = filters,                 // normally 149 x 6
    Observed = observedIQUV,           // normally 24 values
    ScatteredLight = new double[24],
    InitialGuess = [15, 90, 45, .5, 50, 150, 0, 2400, 3600, 1],
    FreeParameters = [1, 1, 1, 0, 1, 1, 1, 1, 1, 0]
});
```

`VfisvEngine` contains no shared mutable numerical state, so one instance can
be called concurrently for independent pixels. Use `PipelineUtilities` to
produce a valid-pixel list and `Parallel.For` (or your own scheduler) for a
full image.

## CLI

```bash
# Generate a documented JSON input, then invert it
dotnet run --project src/Vfisv.Cli -- make-sample sample.json
dotnet run --project src/Vfisv.Cli -- invert sample.json result.json

# Generate normalized six-bin HMI filters for one CCD pixel
dotnet run --project src/Vfisv.Cli -- filter 1108394640 2048 2048 basic_data filters.json

# Resolve and download the 24 HMI Stokes FITS segments
dotnet run --project src/Vfisv.Cli -- download 2012 2 15 15 24 observation

# Verify or inspect an ordinary or RICE_1-compressed image
dotnet run --project src/Vfisv.Cli -- fits-info observation/I0.fits

# Invert segments that have already been downloaded
dotnet run --project src/Vfisv.Cli -- invert-fits observation basic_data output 1900 1900 64 64

# Download and invert a 64x64 region in one command
dotnet run --project src/Vfisv.Cli -- process-date 2012 2 15 15 24 basic_data observation output 1900 1900 64 64
```

The date arguments identify an exact JSOC TAI record. HMI `hmi.S_720s` records
use a 12-minute cadence, so typical minute values are `00`, `12`, `24`, `36`,
and `48`. `process-date` downloads and decompresses all 24 Stokes segments,
runs the inversion, and writes inclination, azimuth, field-strength, and
line-of-sight-velocity FITS maps. Omit the final `X Y WIDTH HEIGHT` values to
process the full disk; a small region is strongly recommended for an initial
run because a full 4096x4096 inversion is computationally expensive.

CSharpFITS itself parses the compressed-image binary table but predates the
FITS tile-compression convention. `CSharpFitsImageReader` therefore supplies
the missing managed RICE_1 decoder. It currently supports the JSOC/HMI layout:
16-bit pixels and one complete image row per compressed tile. The bundled
calibration phase maps are ordinary primary images and remain supported.

## Compatibility notes

- LAPACK `DGESVD` was used only to solve and invert symmetric Hessians. This
  port uses a Jacobi eigendecomposition and thresholded symmetric
  pseudoinverse, avoiding a native dependency.
- The original fixed random seed was compiler-specific. `RandomSeed` makes
  reset behavior deterministic in .NET, but a reset path is not expected to be
  bit-identical to a particular gfortran build.
- Direct evaluation of the original Voigt rational approximation avoids lookup
  interpolation error; results can therefore differ slightly from the
  `voigt_taylor.f90` fast path while remaining within that approximation's
  accuracy.
- CSharpFITS 1.1.0 targets the older .NET Framework API surface. The managed
  assembly runs under .NET 8 for the APIs used here; `NU1701` is suppressed in
  `Vfisv.Core.csproj` for that package reference.
- The source archive contains no license file. Confirm redistribution terms
  with the original VFISV authors before distributing this port.

## Project layout

```text
src/Vfisv.Core/       numerical library, filter calibration, FITS and JSOC APIs
src/Vfisv.Cli/        JSON command-line front end
tests/Vfisv.Tests/    dependency-free verification executable
basic_data/           original calibration data plus converted profiles
tools/                one-time C-array to CSV conversion script
```
