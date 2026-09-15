namespace Vfisv;

/// <summary>Loads the 24 JSOC HMI Stokes segments into the planar layout used by VFISV.</summary>
public static class HmiFitsObservationReader
{
    public static readonly string[] SegmentNames =
    [
        "I0", "I1", "I2", "I3", "I4", "I5",
        "Q0", "Q1", "Q2", "Q3", "Q4", "Q5",
        "U0", "U1", "U2", "U3", "U4", "U5",
        "V0", "V1", "V2", "V3", "V4", "V5"
    ];

    public static float[] ReadDirectory(string directory, HmiObservationMetadata metadata)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        var pixelsPerPlane = checked(metadata.Width * metadata.Height);
        var planar = new float[checked(pixelsPerPlane * SegmentNames.Length)];

        for (var plane = 0; plane < SegmentNames.Length; plane++)
        {
            var path = Path.Combine(directory, SegmentNames[plane] + ".fits");
            if (!File.Exists(path)) throw new FileNotFoundException($"Missing HMI segment {SegmentNames[plane]}.", path);
            var image = CSharpFitsImageReader.Read(path);
            if (image.Width != metadata.Width || image.Height != metadata.Height)
                throw new InvalidDataException($"{Path.GetFileName(path)} is {image.Width}x{image.Height}; expected {metadata.Width}x{metadata.Height}.");
            image.Pixels.CopyTo(planar, plane * pixelsPerPlane);
        }

        return planar;
    }
}
