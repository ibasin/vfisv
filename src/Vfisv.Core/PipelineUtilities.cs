namespace Vfisv;

public enum PixelMaskReason
{
    Valid = 0,
    InvalidStokes = 1,
    OutsideSolarDisk = 2,
    OutsideRegion = 3,
    Masked = 4
}

public readonly record struct PixelRegion(int X, int Y, int Width, int Height)
{
    public bool Contains(int x, int y) => x >= X && x < X + Width && y >= Y && y < Y + Height;
}

public sealed record PixelMask(PixelMaskReason[] Reasons, int ValidCount);

/// <summary>Managed equivalents of mapinvalidvalue, pixel_assign, stokes_reorganize and result scatter.</summary>
public static class PipelineUtilities
{
    public static PixelMask MapInvalidValues(ReadOnlySpan<float> planarStokes, int variableCount,
        int width, int height, double referencePixelX, double referencePixelY,
        double pixelScaleX, double pixelScaleY, double solarRadiusArcSeconds,
        PixelRegion? region = null, ReadOnlySpan<bool> externalMask = default)
    {
        var pixels = checked(width * height);
        if (planarStokes.Length != pixels * variableCount) throw new ArgumentException("Planar Stokes size is inconsistent.");
        if (!externalMask.IsEmpty && externalMask.Length != pixels) throw new ArgumentException("External mask size is inconsistent.");
        var reasons = new PixelMaskReason[pixels]; var valid = 0;
        for (var pixel = 0; pixel < pixels; pixel++)
        {
            var sumSquares = 0.0;
            for (var variable = 0; variable < variableCount; variable++)
            { var value = planarStokes[pixel + variable * pixels]; sumSquares += value * value; }
            var x = pixel % width; var y = pixel / width;
            if (!double.IsFinite(sumSquares) || sumSquares < 1e-2) reasons[pixel] = PixelMaskReason.InvalidStokes;
            var dx = (x - (referencePixelX - 1.0)) * pixelScaleX;
            var dy = (y - (referencePixelY - 1.0)) * pixelScaleY;
            if (Math.Sqrt(dx * dx + dy * dy + 1e-20) > solarRadiusArcSeconds) reasons[pixel] = PixelMaskReason.OutsideSolarDisk;
            if (region is { } r && !r.Contains(x, y)) reasons[pixel] = PixelMaskReason.OutsideRegion;
            if (!externalMask.IsEmpty && externalMask[pixel]) reasons[pixel] = PixelMaskReason.Masked;
            if (reasons[pixel] == PixelMaskReason.Valid) valid++;
        }
        return new PixelMask(reasons, valid);
    }

    public static int[] AssignPixels(ReadOnlySpan<PixelMaskReason> mask, int workerCount, bool cyclic = true)
    {
        if (workerCount <= 0) throw new ArgumentOutOfRangeException(nameof(workerCount));
        var valid = 0;
        for (var i = 0; i < mask.Length; i++) if (mask[i] == PixelMaskReason.Valid) valid++;
        if (valid == 0) return [];
        var jobsPerWorker = (valid + workerCount - 1) / workerCount;
        var result = Enumerable.Repeat(-1, jobsPerWorker * workerCount).ToArray();
        var count = 0; var last = -1;
        for (var pixel = 0; pixel < mask.Length; pixel++)
        {
            if (mask[pixel] != PixelMaskReason.Valid) continue;
            var first = cyclic ? count / workerCount : count % jobsPerWorker;
            var second = cyclic ? count % workerCount : count / jobsPerWorker;
            result[first + second * jobsPerWorker] = last = pixel; count++;
        }
        for (var i = 0; i < result.Length; i++) if (result[i] < 0) result[i] = last;
        return result;
    }

    public static float[] ReorganizeStokes(int worker, int jobsPerWorker, int variableCount,
        int imagePixels, ReadOnlySpan<float> planarStokes, ReadOnlySpan<int> assignments)
    {
        var output = new float[jobsPerWorker * variableCount];
        for (var job = 0; job < jobsPerWorker; job++)
        {
            var pixel = assignments[job + jobsPerWorker * worker];
            for (var variable = 0; variable < variableCount; variable++) output[job * variableCount + variable] = planarStokes[pixel + variable * imagePixels];
        }
        return output;
    }

    public static float[] ExtractModelMap(ReadOnlySpan<double> packedModels, int pixelCount, ModelParameter parameter)
    {
        if (packedModels.Length != pixelCount * 10) throw new ArgumentException("Packed model size is inconsistent.");
        var output = new float[pixelCount];
        for (var pixel = 0; pixel < pixelCount; pixel++) output[pixel] = (float)packedModels[pixel * 10 + (int)parameter];
        return output;
    }
}
