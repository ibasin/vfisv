VFISV C# — source-only package
================================

This lightweight archive contains the complete C# source code, tests, command-line
application, and documentation. The unchanged calibration files in basic_data/ are
omitted to keep the download small.

NuGet restore downloads CSharpFITS 1.1.0 automatically. It is used to parse FITS
HDUs and binary tables; the project adds the RICE_1 decoder required by JSOC/HMI.

Before running VFISV:

1. Extract the original vfisv_python_v09_orig.zip archive.
2. Copy its basic_data directory into this directory, next to Vfisv.sln.
3. Build and test with:

       dotnet build Vfisv.sln -c Release
       dotnet run --project tests/Vfisv.Tests -c Release

See README.md for command-line usage and PORTING_NOTES.md for implementation details.
