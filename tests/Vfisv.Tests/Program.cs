using Vfisv;
using System.Buffers.Binary;
using System.Text;

var tests = new List<(string Name, Action Test)>
{
    ("Voigt symmetry", () =>
    {
        var positive = VoigtProfile.Evaluate(0.5, 1.25); var negative = VoigtProfile.Evaluate(0.5, -1.25);
        Equal(positive.Absorption, negative.Absorption, 1e-14); Equal(positive.Dispersion, -negative.Dispersion, 1e-14);
    }),
    ("FITS float image round trip", () =>
    {
        var path = Path.Combine(Path.GetTempPath(), $"vfisv-{Guid.NewGuid():N}.fits");
        try { FitsPrimaryImage.WriteFloat32(path, 2, 2, [1f, -2f, 3.5f, 4f]); var image = FitsPrimaryImage.Read(path); Equal(image.Data[2], 3.5f, 0f); }
        finally { File.Delete(path); }
    }),
    ("CSharpFITS RICE_1 image", () =>
    {
        var path = Path.Combine(Path.GetTempPath(), $"vfisv-rice-{Guid.NewGuid():N}.fits");
        try
        {
            WriteTinyRiceFits(path);
            var image = CSharpFitsImageReader.Read(path);
            if (image.Width != 4 || image.Height != 2) throw new Exception("Incorrect compressed-image dimensions.");
            float[] expected = [100, 100, 100, 100, 100, 101, 99, 102];
            for (var i = 0; i < expected.Length; i++) Equal(image.Pixels[i], expected[i], 0f);
        }
        finally { File.Delete(path); }
    }),
    ("Calibration/filter generation", () =>
    {
        var data = Path.Combine(AppContext.BaseDirectory, "basic_data");
        var calibration = FilterCalibration.Load(data, 1_108_394_640.0);
        var filters = calibration.CreateFilters(6, 149, 2048, 2048, new DetectorGeometry(2048.5, 2048.5, 6.957e8, 1.496e11, 0.5));
        FilterCalibration.NormalizeInPlace(filters);
        if (filters.Cast<double>().Any(x => !double.IsFinite(x)) || filters.Cast<double>().Sum() <= 0.0) throw new Exception("Invalid filter output.");
    }),
    ("Analytic response functions", () =>
    {
        const int bins = 6; const int wavelengths = 49;
        var context = new InversionContext
        {
            BinCount = bins, WavelengthCount = wavelengths, FullWavelengthCount = wavelengths,
            Free = Enumerable.Repeat(true, 10).ToArray(), FreeLocations = Enumerable.Range(0, 10).ToArray(), FreeDegrees = 14,
            Wave = Enumerable.Range(0, wavelengths).Select(i => -648.0 + 27.0 * i).ToArray(),
            TunePositions = Enumerable.Range(0, bins).Select(i => (-2.5 + i) * 69.0).ToArray(),
            Noise = [1, 1, 1, 1], Continuum = 60_000,
            LowerLimit = new double[10], UpperLimit = Enumerable.Repeat(1e9, 10).ToArray(),
            Norm = Enumerable.Repeat(1.0, 10).ToArray(), DeltaLimit = Enumerable.Repeat(1e9, 10).ToArray()
        };
        var filters = new double[wavelengths, bins];
        for (var w = 0; w < wavelengths; w++) for (var b = 0; b < bins; b++)
        { var x = (w - (4 + b * 8)) / 3.0; filters[w, b] = Math.Exp(-0.5 * x * x); }
        var scattered = new double[bins, 4]; var integrated = new double[bins];
        var model = new[] { 5.0, 60.0, 30.0, 0.5, 30.0, 500.0, 10_000.0, 10_000.0, 50_000.0, 0.8 };
        var options = new VfisvOptions { SyntheticWavelengthCount = wavelengths };
        var analytic = ForwardModel.Synthesize(model, scattered, true, filters, integrated, context, options);
        for (var p = 0; p < 10; p++)
        {
            var step = 1e-5 * Math.Max(1.0, Math.Abs(model[p]));
            var plus = (double[])model.Clone(); var minus = (double[])model.Clone(); plus[p] += step; minus[p] -= step;
            var high = ForwardModel.Synthesize(plus, scattered, false, filters, integrated, context, options).Stokes;
            var low = ForwardModel.Synthesize(minus, scattered, false, filters, integrated, context, options).Stokes;
            var worst = 0.0;
            for (var b = 0; b < bins; b++) for (var s = 0; s < 4; s++)
            {
                var numeric = (high[b, s] - low[b, s]) / (2.0 * step);
                worst = Math.Max(worst, Math.Abs(numeric - analytic.Derivatives[p, b, s]) / Math.Max(1.0, Math.Abs(numeric)));
            }
            if (worst > 3e-3) throw new Exception($"Parameter {p} relative difference {worst:E3}.");
        }
    }),
    ("Fortran forward-model reference", () =>
    {
        var data = Path.Combine(AppContext.BaseDirectory, "basic_data");
        var calibration = FilterCalibration.Load(data, 1_108_394_640.0);
        var filters = calibration.CreateFilters(6, 149, 2048, 2048, new DetectorGeometry(2048.5, 2048.5, 6.957e8, 1.496e11, 0.5));
        FilterCalibration.NormalizeInPlace(filters);
        double[] model = [7.284453484416009, 113.5370078560405, 78.74062575941178, 0.5, 32.73922770958754,
            420.8529799133463, 33857.13234078139, 42223.847237423746, 10857.344993224579, 1.0];
        double[] expected = [51782.46055804757, 50543.5141001717, 47071.591947547226, 46941.58292518886,
            50632.87466985991, 52137.660963121816, 17.06567461112687, 94.80285987470309,
            49.95096034308748, 42.07855150337425, 104.89811626580989, 19.795436798067342,
            -8.436103439391438, -47.891778806301595, -43.71200442996816, -40.84373571125857,
            -53.223474102277386, -9.785908021380052, -66.83517432049817, -281.2443176581659,
            -321.63470592931685, 309.0175440985604, 309.05931258906634, 73.08060603223593];
        var actual = new VfisvEngine().Synthesize(filters, [], model);
        var maxDifference = actual.Zip(expected).Max(pair => Math.Abs(pair.First - pair.Second));
        if (maxDifference > 0.04) throw new Exception($"Maximum absolute difference {maxDifference:E3}.");
    }),
    ("Low intensity status", () =>
    {
        var request = new InversionRequest { Filters = new double[149, 6], Observed = new double[24] };
        var result = new VfisvEngine().Invert(request);
        if (result.Status != ConvergenceStatus.IntensityTooLow) throw new Exception($"Unexpected status {result.Status}.");
    })
};

var failures = 0;
foreach (var (name, test) in tests)
{
    try { test(); Console.WriteLine($"PASS {name}"); }
    catch (Exception exception) { failures++; Console.Error.WriteLine($"FAIL {name}: {exception.Message}"); }
}
return failures == 0 ? 0 : 1;

static void Equal(double actual, double expected, double tolerance)
{
    if (Math.Abs(actual - expected) > tolerance) throw new Exception($"Expected {expected:R}, got {actual:R}.");
}

static void WriteTinyRiceFits(string path)
{
    static string Card(string key, string value, string? comment = null)
    {
        var text = $"{key,-8}= {value,20}";
        if (comment is not null) text += $" / {comment}";
        return text.PadRight(80)[..80];
    }

    static byte[] Header(params string[] cards)
    {
        var text = string.Concat(cards.Append("END".PadRight(80))).PadRight(2880);
        return Encoding.ASCII.GetBytes(text);
    }

    using var stream = File.Create(path);
    stream.Write(Header(Card("SIMPLE", "T"), Card("BITPIX", "16"), Card("NAXIS", "0"), Card("EXTEND", "T")));
    stream.Write(Header(
        Card("XTENSION", "'BINTABLE'"), Card("BITPIX", "8"), Card("NAXIS", "2"), Card("NAXIS1", "8"),
        Card("NAXIS2", "2"), Card("PCOUNT", "8"), Card("GCOUNT", "1"), Card("TFIELDS", "1"),
        Card("TTYPE1", "'COMPRESSED_DATA'"), Card("TFORM1", "'1PB(5)'"), Card("ZIMAGE", "T"),
        Card("ZBITPIX", "16"), Card("ZNAXIS", "2"), Card("ZNAXIS1", "4"), Card("ZNAXIS2", "2"),
        Card("ZTILE1", "4"), Card("ZTILE2", "1"), Card("ZCMPTYPE", "'RICE_1'"),
        Card("ZNAME1", "'BLOCKSIZE'"), Card("ZVAL1", "4"), Card("ZNAME2", "'BYTEPIX'"), Card("ZVAL2", "2")));

    Span<byte> descriptor = stackalloc byte[8];
    BinaryPrimitives.WriteInt32BigEndian(descriptor, 3);
    BinaryPrimitives.WriteInt32BigEndian(descriptor[4..], 0);
    stream.Write(descriptor);
    BinaryPrimitives.WriteInt32BigEndian(descriptor, 5);
    BinaryPrimitives.WriteInt32BigEndian(descriptor[4..], 3);
    stream.Write(descriptor);
    stream.Write(new byte[] { 0, 100, 0, 0, 100, 0x29, 0x31, 0 });
    stream.Write(new byte[2880 - 24]);
}
