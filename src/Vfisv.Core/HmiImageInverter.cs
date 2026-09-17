namespace Vfisv;

public sealed record HmiImageInversionResult(
    int Width, int Height, double[] Models, double[] Errors,
    ConvergenceStatus[] Status, PixelMask Mask)
{
    public float[] GetModelMap(ModelParameter parameter) =>
        PipelineUtilities.ExtractModelMap(Models, checked(Width * Height), parameter);
}

/// <summary>Parallel full-image orchestration replacing vfisv_stand_alone.py and its MPI gather loop.</summary>
public sealed class HmiImageInverter(FilterCalibration calibration, VfisvOptions? options = null)
{
    private readonly FilterCalibration _calibration = calibration ?? throw new ArgumentNullException(nameof(calibration));
    private readonly VfisvOptions _options = options ?? new VfisvOptions();

    /// <param name="planarStokes">24 planes in I0..I5,Q0..Q5,U0..U5,V0..V5 order.</param>
    public HmiImageInversionResult Invert(float[] planarStokes, HmiObservationMetadata metadata, PixelRegion? region = null, int? maximumDegreeOfParallelism = null, CancellationToken cancellationToken = default)
    {
        const int bins = 6; 
        const int variables = 24;
        
        var pixelsCount = checked(metadata.Width * metadata.Height);
        if (planarStokes.Length != pixelsCount * variables) throw new ArgumentException("Expected 24 complete Stokes planes.", nameof(planarStokes));
        var solarArcSeconds = Math.Asin(metadata.SolarRadiusMeters / metadata.ObserverDistanceMeters) / Math.PI * 180.0 * 3600.0;
        var mask = PipelineUtilities.MapInvalidValues(planarStokes, variables, metadata.Width, metadata.Height,
            metadata.ReferencePixelX, metadata.ReferencePixelY, metadata.PixelScaleX, metadata.PixelScaleY, solarArcSeconds, region);
        var geometry = new DetectorGeometry(metadata.ReferencePixelX, metadata.ReferencePixelY,
            metadata.SolarRadiusMeters, metadata.ObserverDistanceMeters, metadata.PixelScaleX);
        var centerFilters = _calibration.CreateFilters(bins, 149, 2048, 2048, geometry);
        var normalization = FilterCalibration.NormalizationFactor(centerFilters);
        var engineOptions = metadata.Camera == 3
            ? _options with { NoiseFactors = [0.083, 0.167, 0.167, 0.118] }
            : _options;
        var engine = new VfisvEngine(engineOptions);
        var models = Enumerable.Repeat(double.NaN, pixelsCount * 10).ToArray();
        var errors = Enumerable.Repeat(double.NaN, pixelsCount * 12).ToArray();
        var statuses = Enumerable.Repeat(ConvergenceStatus.IntensityTooLow, pixelsCount).ToArray();
        var parallelOptions = new ParallelOptions { CancellationToken = cancellationToken };
        if (maximumDegreeOfParallelism is { } degree) parallelOptions.MaxDegreeOfParallelism = degree;

        Parallel.For(0, pixelsCount, parallelOptions, pixel =>
        {
            if (mask.Reasons[pixel] != PixelMaskReason.Valid) return;
            var x = pixel % metadata.Width; var y = pixel / metadata.Width;
            var scaledX = (int)((double)x / metadata.Width * 4096.0 + 1e-9);
            var scaledY = (int)((double)y / metadata.Height * 4096.0 + 1e-9);
            var filters = _calibration.CreateFilters(bins, 149, scaledX, scaledY, geometry);
            FilterCalibration.DivideInPlace(filters, normalization);
            var observed = new double[variables];
            for (var variable = 0; variable < variables; variable++) observed[variable] = planarStokes[pixel + variable * pixelsCount];
            var result = engine.Invert(new InversionRequest { Filters = filters, Observed = observed });
            Array.Copy(result.Model, 0, models, pixel * 10, 10);
            Array.Copy(result.Errors, 0, errors, pixel * 12, 12);
            statuses[pixel] = result.Status;
        });
        return new HmiImageInversionResult(metadata.Width, metadata.Height, models, errors, statuses, mask);
    }

    public static void WriteStandardFitsMaps(HmiImageInversionResult result, string directory, string prefix)
    {
        Directory.CreateDirectory(directory);
        foreach (var parameter in new[] { ModelParameter.Inclination, ModelParameter.Azimuth, ModelParameter.FieldStrength, ModelParameter.LineOfSightVelocity })
        {
            var path = Path.Combine(directory, $"{prefix}.{parameter}.fits");
            FitsPrimaryImage.WriteFloat32(path, result.Width, result.Height, result.GetModelMap(parameter));
        }
    }
}
