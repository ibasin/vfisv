using System.Buffers.Binary;
using System.Globalization;

namespace Vfisv;

public sealed record DetectorGeometry(
    double ReferencePixelX,
    double ReferencePixelY,
    double SolarRadiusMeters,
    double ObserverDistanceMeters,
    double PixelScaleArcSeconds);

/// <summary>HMI optical-filter calibration ported from vfisv_subprogs.c.</summary>
public sealed class FilterCalibration
{
    private const int MapWidth = 128;
    private const int MapHeight = 128;
    private readonly double[] _phaseNonTunable;
    private readonly double[] _phaseTunable;
    private readonly double[] _contrastNonTunable;
    private readonly double[] _contrastTunable;
    private readonly double[] _frontWavelength;
    private readonly double[] _frontTransmission;
    private readonly double[] _blockerWavelength;
    private readonly double[] _blockerTransmission;

    private FilterCalibration(int phaseMapId, int hcme1, int hcmwb, int hcmnb,
        double[] phaseNonTunable, double[] phaseTunable, double[] contrastNonTunable, double[] contrastTunable,
        (double[] X, double[] Y) front, (double[] X, double[] Y) blocker)
    {
        PhaseMapId = phaseMapId; Hcme1 = hcme1; Hcmwb = hcmwb; Hcmnb = hcmnb;
        _phaseNonTunable = phaseNonTunable; _phaseTunable = phaseTunable;
        _contrastNonTunable = contrastNonTunable; _contrastTunable = contrastTunable;
        (_frontWavelength, _frontTransmission) = front; (_blockerWavelength, _blockerTransmission) = blocker;
    }

    public int PhaseMapId { get; }
    public int Hcme1 { get; }
    public int Hcmwb { get; }
    public int Hcmnb { get; }
    public static double[] FreeSpectralRanges { get; } = [0.1689, 0.33685, 0.695, 1.417, 2.779, 5.682, 11.354];

    /// <param name="jsocTimeSeconds">JSOC/DRMS time in seconds, as used by filePhaseMaps.txt.</param>
    public static FilterCalibration Load(string dataDirectory, double jsocTimeSeconds)
    {
        var phaseMapId = SelectPhaseMap(Path.Combine(dataDirectory, "filePhaseMaps.txt"), jsocTimeSeconds);
        var fits = FitsPrimaryImage.Read(Path.Combine(dataDirectory, $"hmi.phasemaps_corrected.{phaseMapId}.phases.fits"));
        if (fits.Axes.Length != 3 || fits.Axes[0] != 5 || fits.Axes[1] != MapWidth || fits.Axes[2] != MapHeight)
            throw new InvalidDataException("Unexpected phase-map dimensions.");
        var phaseTunable = new double[3 * MapWidth * MapHeight];
        for (var pixel = 0; pixel < MapWidth * MapHeight; pixel++)
        for (var element = 0; element < 3; element++)
            phaseTunable[pixel * 3 + element] = fits.Data[pixel * 5 + element] * Math.PI / 180.0;
        return new FilterCalibration(phaseMapId, fits.GetInt32("HCME1"), fits.GetInt32("HCMWB"), fits.GetInt32("HCMNB"),
            ReadFloat32(Path.Combine(dataDirectory, "non_tunable_phases_710660_June09_cal_128_2.bin"), 4 * MapWidth * MapHeight, Math.PI / 180.0),
            phaseTunable,
            ReadFloat32(Path.Combine(dataDirectory, "non_tunable_contrasts_710660_June09_cal_128_2.bin"), 4 * MapWidth * MapHeight),
            ReadFloat32(Path.Combine(dataDirectory, "tunable_contrasts_710660_June09_cal_128.bin"), 3 * MapWidth * MapHeight),
            ReadCsv(Path.Combine(dataDirectory, "front_window_profile.csv")), ReadCsv(Path.Combine(dataDirectory, "blocker_profile.csv")));
    }

    public double[,] CreateFilters(int count, int wavelengthCount, int column, int row,
        DetectorGeometry geometry, double minimumMilliAngstrom = -1998.0, double stepMilliAngstrom = 27.0,
        int detectorWidth = 4096, int detectorHeight = 4096)
    {
        if (count is not (5 or 6 or 8 or 10)) throw new ArgumentOutOfRangeException(nameof(count));
        var x = Math.Clamp(column * MapWidth / detectorWidth, 0, MapWidth - 1);
        var y = Math.Clamp(row * MapHeight / detectorHeight, 0, MapHeight - 1);
        if (x + 1 >= MapWidth) x--;
        if (y + 1 >= MapHeight) y--;
        var fx = (double)(column % (detectorWidth / MapWidth)) / (detectorWidth / MapWidth);
        var fy = (double)(row % (detectorHeight / MapHeight)) / (detectorHeight / MapHeight);
        var phaseNt = InterpolatePlanar(_phaseNonTunable, 4, x, y, fx, fy);
        var contrastNt = InterpolatePlanar(_contrastNonTunable, 4, x, y, fx, fy);
        var contrastT = InterpolatePlanar(_contrastTunable, 3, x, y, fx, fy);
        var phaseT = InterpolateInterleaved(_phaseTunable, 3, x, y, fx, fy);
        var motor = MotorPhases(count, Hcme1, Hcmwb, Hcmnb);
        var result = new double[wavelengthCount, count];
        var wavelength = Enumerable.Range(0, wavelengthCount).Select(i => minimumMilliAngstrom / 1000.0 + i * stepMilliAngstrom / 1000.0).ToArray();
        var frontX = _frontWavelength.Select(v => v * 10.0 - 6173.3433).ToArray();
        var blockerX = _blockerWavelength.Select(v => v + 2.7 - 6173.3433).ToArray();
        var frontY = _frontTransmission.Select(v => v / 100.0).ToArray();
        var blockerY = _blockerTransmission.Select(v => v / 100.0).ToArray();
        var sunRadiusPixels = Math.Asin(geometry.SolarRadiusMeters / geometry.ObserverDistanceMeters)
                              / Math.PI * 180.0 * 3600.0 / geometry.PixelScaleArcSeconds;
        var radialPixels = Math.Sqrt(Math.Pow(row - (geometry.ReferencePixelY - 1.0), 2) + Math.Pow(column - (geometry.ReferencePixelX - 1.0), 2));
        _ = Math.Cos(Math.Asin(Math.Min(1.0, radialPixels / sunRadiusPixels))); // Preserved for parity; current C filter does not use distance.

        for (var w = 0; w < wavelengthCount; w++)
        {
            var envelope = Interpolate(_frontWavelength.Length, frontX, frontY, wavelength[w], 0.0)
                           * Interpolate(_blockerWavelength.Length, blockerX, blockerY, wavelength[w], 0.0);
            var lyot = envelope;
            for (var e = 0; e < 4; e++) lyot *= (1.0 + contrastNt[e] * Math.Cos(2.0 * Math.PI / FreeSpectralRanges[e + 3] * wavelength[w] + phaseNt[e])) / 2.0;
            for (var bin = 0; bin < count; bin++)
            {
                var value = lyot;
                value *= (1.0 + contrastT[0] * Math.Cos(2.0 * Math.PI / FreeSpectralRanges[0] * wavelength[w] + motor.Narrow[bin] + phaseT[0])) / 2.0;
                value *= (1.0 + contrastT[1] * Math.Cos(2.0 * Math.PI / FreeSpectralRanges[1] * wavelength[w] + motor.Wide[bin] + phaseT[1])) / 2.0;
                value *= (1.0 + contrastT[2] * Math.Cos(2.0 * Math.PI / FreeSpectralRanges[2] * wavelength[w] - motor.E1[bin] + phaseT[2])) / 2.0;
                result[w, bin] = value;
            }
        }
        return result;
    }

    public static void NormalizeInPlace(double[,] filters)
    {
        var average = NormalizationFactor(filters);
        for (var b = 0; b < filters.GetLength(1); b++) for (var w = 0; w < filters.GetLength(0); w++) filters[w, b] /= average;
    }

    public static double NormalizationFactor(double[,] filters)
    {
        var sums = new double[filters.GetLength(1)];
        for (var b = 0; b < sums.Length; b++) for (var w = 0; w < filters.GetLength(0); w++) sums[b] += filters[w, b];
        var average = sums.Average();
        return !double.IsFinite(average) || Math.Abs(average) < 1e-10 ? 1.0 : average;
    }

    public static void DivideInPlace(double[,] filters, double divisor)
    {
        if (!double.IsFinite(divisor) || divisor == 0.0) throw new ArgumentOutOfRangeException(nameof(divisor));
        for (var b = 0; b < filters.GetLength(1); b++) for (var w = 0; w < filters.GetLength(0); w++) filters[w, b] /= divisor;
    }

    private static int SelectPhaseMap(string path, double time)
    {
        var selected = 0;
        foreach (var line in File.ReadLines(path))
        {
            var fields = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (fields.Length >= 2 && time > double.Parse(fields[0], CultureInfo.InvariantCulture)) selected = int.Parse(fields[1], CultureInfo.InvariantCulture);
        }
        if (selected == 0) throw new InvalidDataException("No phase map applies to the requested observation time.");
        return selected;
    }

    private static double[] ReadFloat32(string path, int count, double multiplier = 1.0)
    {
        var bytes = File.ReadAllBytes(path); if (bytes.Length < count * 4) throw new EndOfStreamException(path);
        var result = new double[count];
        for (var i = 0; i < count; i++) result[i] = BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(i * 4, 4))) * multiplier;
        return result;
    }

    private static (double[] X, double[] Y) ReadCsv(string path)
    {
        var rows = File.ReadLines(path).Skip(1).Select(line => line.Split(','))
            .Select(v => (double.Parse(v[0], CultureInfo.InvariantCulture), double.Parse(v[1], CultureInfo.InvariantCulture))).ToArray();
        return (rows.Select(x => x.Item1).ToArray(), rows.Select(x => x.Item2).ToArray());
    }

    private static double Interpolate(int count, double[] x, double[] y, double target, double outside)
    {
        if (target < x[0] || target > x[count - 1]) return outside;
        var index = Array.BinarySearch(x, target); if (index >= 0) return y[index];
        index = ~index; return (target - x[index - 1]) / (x[index] - x[index - 1]) * (y[index] - y[index - 1]) + y[index - 1];
    }

    private static double[] InterpolatePlanar(double[] data, int elements, int x, int y, double fx, double fy)
    {
        var result = new double[elements]; var x1 = Math.Min(x + 1, MapWidth - 1); var y1 = Math.Min(y + 1, MapHeight - 1);
        for (var e = 0; e < elements; e++) result[e] = Bilinear(data, e * MapWidth * MapHeight, x, y, x1, y1, fx, fy, 1);
        return result;
    }

    private static double[] InterpolateInterleaved(double[] data, int elements, int x, int y, double fx, double fy)
    {
        var result = new double[elements]; var x1 = Math.Min(x + 1, MapWidth - 1); var y1 = Math.Min(y + 1, MapHeight - 1);
        for (var e = 0; e < elements; e++) result[e] = Bilinear(data, e, x, y, x1, y1, fx, fy, elements);
        return result;
    }

    private static double Bilinear(double[] data, int offset, int x0, int y0, int x1, int y1, double fx, double fy, int stride)
    {
        double At(int x, int y) => data[offset + (x + y * MapWidth) * stride];
        return (1.0 - fy) * ((1.0 - fx) * At(x0, y0) + fx * At(x1, y0))
               + fy * ((1.0 - fx) * At(x0, y1) + fx * At(x1, y1));
    }

    private static (double[] E1, double[] Wide, double[] Narrow) MotorPhases(int count, int e1, int wide, int narrow)
    {
        int[] e1Offsets; int[] wideOffsets; int[] narrowOffsets;
        switch (count)
        {
            case 5: e1Offsets = [-12, -6, 0, 6, 12]; wideOffsets = [24, 12, 0, -12, -24]; narrowOffsets = [-12, 24, 0, -24, 12]; break;
            case 6: e1Offsets = [-15, -9, -3, 3, 9, 15]; wideOffsets = [-30, 18, 6, -6, -18, -30]; narrowOffsets = [0, -24, 12, -12, 24, 0]; break;
            case 8: e1Offsets = [-21, -15, -9, -3, 3, 9, 15, 21]; wideOffsets = [-18, -30, 18, 6, -6, -18, -30, 18]; narrowOffsets = [24, 0, -24, 12, -12, 24, 0, -24]; break;
            case 10: e1Offsets = [-27, -21, -15, -9, -3, 3, 9, 15, 21, 27]; wideOffsets = [-6, -18, -30, 18, 6, -6, -18, -30, 18, 6]; narrowOffsets = [-12, 24, 0, -24, 12, -12, 24, 0, -24, 12]; break;
            default: throw new ArgumentOutOfRangeException(nameof(count));
        }
        double Phase(int motor, int offset) => ((motor + offset) * 6 % 360) * Math.PI / 180.0;
        return (e1Offsets.Select(x => Phase(e1, x)).ToArray(), wideOffsets.Select(x => Phase(wide, x)).ToArray(), narrowOffsets.Select(x => Phase(narrow, x)).ToArray());
    }
}
