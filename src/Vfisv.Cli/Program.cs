using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Vfisv;

var jsonOptions = new JsonSerializerOptions
{
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    WriteIndented = true,
    NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals
};

try
{
    if (args.Length == 0) { Help(); return 0; }
    switch (args[0].ToLowerInvariant())
    {
        case "invert":
        {
            if (args.Length is < 2 or > 3) return UsageError("invert requires INPUT.json and optional OUTPUT.json");
            var dto = JsonSerializer.Deserialize<InversionRequestDto>(File.ReadAllText(args[1]), jsonOptions)
                      ?? throw new InvalidDataException("Input JSON is empty.");
            var result = new VfisvEngine(dto.Options).Invert(dto.ToRequest());
            var output = JsonSerializer.Serialize(result, jsonOptions);
            if (args.Length == 3) File.WriteAllText(args[2], output); else Console.WriteLine(output);
            return 0;
        }
        case "make-sample":
        {
            if (args.Length != 2) return UsageError("make-sample requires OUTPUT.json");
            File.WriteAllText(args[1], JsonSerializer.Serialize(InversionRequestDto.Sample(), jsonOptions));
            Console.WriteLine($"Wrote {args[1]}"); return 0;
        }
        case "synthesize":
        {
            if (args.Length != 4) return UsageError("synthesize requires INPUT.json MODEL.json OUTPUT.json");
            var dto = JsonSerializer.Deserialize<InversionRequestDto>(File.ReadAllText(args[1]), jsonOptions)
                      ?? throw new InvalidDataException("Input JSON is empty.");
            var model = JsonSerializer.Deserialize<double[]>(File.ReadAllText(args[2]), jsonOptions)
                        ?? throw new InvalidDataException("Model JSON is empty.");
            var request = dto.ToRequest();
            var synthetic = new VfisvEngine(dto.Options).Synthesize(request.Filters, request.ScatteredLight, model);
            File.WriteAllText(args[3], JsonSerializer.Serialize(synthetic, jsonOptions));
            Console.WriteLine($"Wrote {args[3]}"); return 0;
        }
        case "filter":
        {
            if (args.Length != 6) return UsageError("filter requires JSOC_SECONDS COLUMN ROW DATA_DIR OUTPUT.json");
            var calibration = FilterCalibration.Load(args[4], double.Parse(args[1], CultureInfo.InvariantCulture));
            var filters = calibration.CreateFilters(6, 149, int.Parse(args[2], CultureInfo.InvariantCulture), int.Parse(args[3], CultureInfo.InvariantCulture),
                new DetectorGeometry(2048.5, 2048.5, 6.957e8, 1.496e11, 0.5));
            FilterCalibration.NormalizeInPlace(filters);
            File.WriteAllText(args[5], JsonSerializer.Serialize(ToJagged(filters), jsonOptions));
            Console.WriteLine($"Wrote {args[5]} using phase map {calibration.PhaseMapId}"); return 0;
        }
        case "download":
        {
            if (args.Length != 7) return UsageError("download requires YEAR MONTH DAY HOUR MINUTE OUTPUT_DIR");
            var time = new DateTimeOffset(int.Parse(args[1]), int.Parse(args[2]), int.Parse(args[3]), int.Parse(args[4]), int.Parse(args[5]), 0, TimeSpan.Zero);
            using var http = new HttpClient(); var client = new JsocClient(http); var descriptor = await client.DescribeAsync(time);
            await client.DownloadSegmentsAsync(descriptor, args[6]);
            File.WriteAllText(Path.Combine(args[6], "metadata.json"), JsonSerializer.Serialize(descriptor.Metadata, jsonOptions));
            Console.WriteLine($"Downloaded {descriptor.Segments.Count} segments to {args[6]}"); return 0;
        }
        case "fits-info":
        {
            if (args.Length != 2) return UsageError("fits-info requires INPUT.fits");
            var image = CSharpFitsImageReader.Read(args[1]);
            var finite = image.Pixels.Where(float.IsFinite).ToArray();
            Console.WriteLine($"{image.Width}x{image.Height}; pixels={image.Pixels.Length}; finite={finite.Length}; " +
                              (finite.Length == 0 ? "no finite values" : $"min={finite.Min():R}; max={finite.Max():R}"));
            return 0;
        }
        case "invert-fits":
        {
            if (args.Length is not (4 or 8))
                return UsageError("invert-fits requires INPUT_DIR DATA_DIR OUTPUT_DIR and optional X Y WIDTH HEIGHT");
            PixelRegion? region = args.Length == 8 ? ParseRegion(args, 4) : null;
            InvertFitsDirectory(args[1], args[2], args[3], region, jsonOptions);
            return 0;
        }
        case "process-date":
        {
            if (args.Length is not (9 or 13))
                return UsageError("process-date requires YEAR MONTH DAY HOUR MINUTE DATA_DIR WORK_DIR OUTPUT_DIR and optional X Y WIDTH HEIGHT");
            var time = new DateTimeOffset(int.Parse(args[1]), int.Parse(args[2]), int.Parse(args[3]), int.Parse(args[4]), int.Parse(args[5]), 0, TimeSpan.Zero);
            PixelRegion? region = args.Length == 13 ? ParseRegion(args, 9) : null;
            using var http = new HttpClient(); var client = new JsocClient(http); var descriptor = await client.DescribeAsync(time);
            await client.DownloadSegmentsAsync(descriptor, args[7]);
            File.WriteAllText(Path.Combine(args[7], "metadata.json"), JsonSerializer.Serialize(descriptor.Metadata, jsonOptions));
            InvertFitsDirectory(args[7], args[6], args[8], region, jsonOptions);
            return 0;
        }
        default: return UsageError($"Unknown command: {args[0]}");
    }
}
catch (Exception exception)
{
    Console.Error.WriteLine(exception.Message); return 1;
}

static int UsageError(string message) { Console.Error.WriteLine(message); Help(); return 2; }
static void Help() => Console.WriteLine("""
VFISV C# port

  vfisv invert INPUT.json [OUTPUT.json]
  vfisv make-sample OUTPUT.json
  vfisv synthesize INPUT.json MODEL.json OUTPUT.json
  vfisv filter JSOC_SECONDS COLUMN ROW DATA_DIR OUTPUT.json
  vfisv download YEAR MONTH DAY HOUR MINUTE OUTPUT_DIR
  vfisv fits-info INPUT.fits
  vfisv invert-fits INPUT_DIR DATA_DIR OUTPUT_DIR [X Y WIDTH HEIGHT]
  vfisv process-date YEAR MONTH DAY HOUR MINUTE DATA_DIR WORK_DIR OUTPUT_DIR [X Y WIDTH HEIGHT]
""");

static PixelRegion ParseRegion(string[] args, int offset) => new(
    int.Parse(args[offset], CultureInfo.InvariantCulture),
    int.Parse(args[offset + 1], CultureInfo.InvariantCulture),
    int.Parse(args[offset + 2], CultureInfo.InvariantCulture),
    int.Parse(args[offset + 3], CultureInfo.InvariantCulture));

static void InvertFitsDirectory(string inputDirectory, string dataDirectory, string outputDirectory,
    PixelRegion? region, JsonSerializerOptions jsonOptions)
{
    var metadataPath = Path.Combine(inputDirectory, "metadata.json");
    var metadata = JsonSerializer.Deserialize<HmiObservationMetadata>(File.ReadAllText(metadataPath), jsonOptions)
                   ?? throw new InvalidDataException("Observation metadata is empty.");
    Console.WriteLine("Reading and decompressing 24 HMI FITS segments...");
    var stokes = HmiFitsObservationReader.ReadDirectory(inputDirectory, metadata);
    var recordTime = DateTimeOffset.ParseExact(metadata.RecordTime, "yyyy.MM.dd_HH:mm:ss'_TAI'",
        CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal);
    var calibration = FilterCalibration.Load(dataDirectory, JsocTime.FromTaiCalendar(recordTime));
    Console.WriteLine(region is null ? "Inverting full image..." : $"Inverting region {region}...");
    var result = new HmiImageInverter(calibration).Invert(stokes, metadata, region);
    var prefix = metadata.RecordTime.Replace('.', '-').Replace(":", string.Empty, StringComparison.Ordinal);
    HmiImageInverter.WriteStandardFitsMaps(result, outputDirectory, prefix);
    Console.WriteLine($"Wrote inversion maps to {outputDirectory}");
}

static double[][] ToJagged(double[,] matrix)
{
    var rows = new double[matrix.GetLength(0)][];
    for (var r = 0; r < rows.Length; r++)
    {
        rows[r] = new double[matrix.GetLength(1)];
        for (var c = 0; c < rows[r].Length; c++) rows[r][c] = matrix[r, c];
    }
    return rows;
}

internal sealed record InversionRequestDto
{
    public required double[][] Filters { get; init; }
    public required double[] Observed { get; init; }
    public double[] ScatteredLight { get; init; } = [];
    public double[] InitialGuess { get; init; } = [15.0, 90.0, 45.0, 0.5, 50.0, 150.0, 0.0, 2400.0, 3600.0, 1.0];
    public int[] FreeParameters { get; init; } = [1, 1, 1, 0, 1, 1, 1, 1, 1, 0];
    public VfisvOptions? Options { get; init; }

    public InversionRequest ToRequest()
    {
        if (Filters.Length == 0 || Filters.Any(row => row.Length != Filters[0].Length)) throw new InvalidDataException("Filters must be a non-empty rectangular array.");
        var matrix = new double[Filters.Length, Filters[0].Length];
        for (var r = 0; r < Filters.Length; r++) for (var c = 0; c < Filters[r].Length; c++) matrix[r, c] = Filters[r][c];
        return new InversionRequest { Filters = matrix, Observed = Observed, ScatteredLight = ScatteredLight, InitialGuess = InitialGuess, FreeParameters = FreeParameters };
    }

    public static InversionRequestDto Sample()
    {
        var filters = new double[149][];
        for (var w = 0; w < filters.Length; w++)
        {
            filters[w] = new double[6];
            for (var b = 0; b < 6; b++)
            {
                var center = 24.0 + b * 20.0; var x = (w - center) / 7.0;
                filters[w][b] = Math.Exp(-0.5 * x * x) / 17.5;
            }
        }
        return new InversionRequestDto
        {
            Filters = filters,
            Observed = [52000, 50500, 47000, 47000, 50500, 52000, 40, 80, 120, 100, 60, 20,
                -30, -50, -80, -70, -40, -20, 180, 260, 350, -330, -240, -160]
        };
    }
}
