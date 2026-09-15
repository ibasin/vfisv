namespace Vfisv;

/// <summary>VFISV model-vector positions, matching the original Fortran ordering.</summary>
public enum ModelParameter
{
    Eta0,
    Inclination,
    Azimuth,
    Damping,
    DopplerWidth,
    FieldStrength,
    LineOfSightVelocity,
    SourceContinuum,
    SourceGradient,
    MagneticFillingFactor
}

public enum ConvergenceStatus
{
    Converged = 0,
    IntensityTooLow = 1,
    MaximumIterationsReached = 2,
    NoFiniteFit = 4,
    ErrorEstimationFailed = 5
}

public sealed record VfisvOptions
{
    public int Iterations { get; init; } = 200;
    public int SyntheticWavelengthCount { get; init; } = 49;
    public double SyntheticMinimumMilliAngstrom { get; init; } = -648.0;
    public double WavelengthStepMilliAngstrom { get; init; } = 27.0;
    public double LineCenterAngstrom { get; init; } = 6173.3433;
    public double ZeemanShiftMilliAngstromPerGauss { get; init; } = 0.044475775;
    public double IntensityThreshold { get; init; } = 100.0;
    public double SvdTolerance { get; init; } = 1e-32;
    public double[] NoiseFactors { get; init; } = [0.118, 0.204, 0.204, 0.204];
    public double[] Weights { get; init; } = [1.0, 5.0, 5.0, 3.5];
    public int RandomSeed { get; init; } = 314159;
    public bool UseVariableChange { get; init; } = true;
    public bool UseRegularization { get; init; } = true;
}

public sealed record InversionRequest
{
    /// <summary>Filter matrix indexed [full-wavelength, filter-bin].</summary>
    public required double[,] Filters { get; init; }

    /// <summary>I, Q, U, V concatenated, each with one value per filter bin.</summary>
    public required double[] Observed { get; init; }

    public double[] ScatteredLight { get; init; } = [];

    /// <summary>Ten VFISV model parameters in <see cref="ModelParameter"/> order.</summary>
    public double[] InitialGuess { get; init; } =
        [15.0, 90.0, 45.0, 0.5, 50.0, 150.0, 0.0, 2400.0, 3600.0, 1.0];

    /// <summary>One means free, zero means fixed.</summary>
    public int[] FreeParameters { get; init; } = [1, 1, 1, 0, 1, 1, 1, 1, 1, 0];
}

public sealed record InversionResult
{
    public required double[] Model { get; init; }
    public required double[] Errors { get; init; }
    public required double[] SyntheticStokes { get; init; }
    public required ConvergenceStatus Status { get; init; }
    public required int Iterations { get; init; }
    public required double ChiSquared { get; init; }
}

internal sealed class InversionContext
{
    public required int BinCount { get; init; }
    public required int WavelengthCount { get; init; }
    public required int FullWavelengthCount { get; init; }
    public required bool[] Free { get; init; }
    public required int[] FreeLocations { get; init; }
    public required int FreeDegrees { get; init; }
    public required double[] Wave { get; init; }
    public required double[] TunePositions { get; init; }
    public required double[] Noise { get; init; }
    public required double Continuum { get; init; }
    public required double[] LowerLimit { get; init; }
    public required double[] UpperLimit { get; init; }
    public required double[] Norm { get; init; }
    public required double[] DeltaLimit { get; init; }
}

internal sealed record SynthesisResult(double[,] Stokes, double[,,] Derivatives);
