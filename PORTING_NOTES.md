# Source-to-C# map

| Original source | C# destination |
| --- | --- |
| `cons_param.f90`, `line_param.f90`, `filt_param.f90`, `inv_param.f90` | `Models.cs`, per-call `InversionContext` |
| `forward.f90` | `ForwardModel.cs` |
| `voigt.f`, `voigt_taylor.f90`, `voigt_init.f90`, `voigt_data.f90` | `VoigtProfile.cs` |
| `invert.f90`, `inv_utils.f90`, `change_var.f90`, `wfa_guess.f90` | `VfisvEngine.cs` |
| `inv_init.f90`, `free_init.f90`, `lim_init.f90`, `line_init.f90`, `wave_init.f90`, `filt_init.f90` | context construction in `VfisvEngine.cs` |
| `svbksb.f90` and LAPACK `DGESVD` calls | `SymmetricMatrix.cs` |
| `vfisv_subprogs.c` | `FilterCalibration.cs`, `PipelineUtilities.cs` |
| `vfisv_tools.py`, `vfisv_stand_alone.py` | public core APIs and `Vfisv.Cli` |
| used portions of `PHSjsoc_3.py` | `JsocClient.cs`, `JsocTime` |
| `c_wrapping_vfisv.c`, `vfisv_as_subroutine.f90` | replaced by the strongly typed `VfisvEngine.Invert` API |

Array indexing was intentionally redesigned around zero-based CLR arrays.
Matrices document their orientation at public boundaries. The original global
Fortran module variables are held in a private per-inversion context, removing
cross-pixel data races.
