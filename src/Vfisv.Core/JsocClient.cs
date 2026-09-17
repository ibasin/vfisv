using System.Globalization;
using System.Text.Json;

namespace Vfisv;

public sealed record HmiObservationMetadata(
    string RecordTime, int Width, int Height, double ObserverRadialVelocity,
    double ReferencePixelX, double ReferencePixelY, double PixelScaleX, double PixelScaleY,
    double SolarRadiusMeters, double ObserverDistanceMeters, double RotationDegrees, int CameraId, int Camera);

public sealed record HmiObservationDescriptor(HmiObservationMetadata Metadata, IReadOnlyDictionary<string, Uri> Segments);

/// <summary>Async JSOC record discovery/downloader replacing requests-based Python access.</summary>
public sealed class JsocClient(HttpClient httpClient)
{
    private static readonly string[] SegmentNames =
    [
        "I0", "I1", "I2", "I3", "I4", "I5", "Q0", "Q1", "Q2", "Q3", "Q4", "Q5",
        "U0", "U1", "U2", "U3", "U4", "U5", "V0", "V1", "V2", "V3", "V4", "V5"
    ];
    private readonly HttpClient _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));

    public async Task<HmiObservationDescriptor> DescribeAsync(DateTimeOffset observationTime, CancellationToken cancellationToken = default)
    {
        var record = observationTime.ToString("yyyy.MM.dd_HH:mm:ss", CultureInfo.InvariantCulture) + "_TAI";
        var keys = "T_REC,OBS_VR,CRPIX1,CRPIX2,CDELT1,CDELT2,RSUN_REF,DSUN_OBS,CROTA2,HCAMID,CAMERA";
        var url = "http://jsoc.stanford.edu/cgi-bin/ajax/jsoc_info?ds=" + Uri.EscapeDataString($"hmi.S_720s[{record}]") + "&op=rs_list&key=" + Uri.EscapeDataString(keys) + "&seg=" + Uri.EscapeDataString(string.Join(',', SegmentNames));
        
        using var json = JsonDocument.Parse(await _httpClient.GetStreamAsync(url, cancellationToken));

        var root = json.RootElement;
        if (root.GetProperty("count").GetInt32() != 1) throw new InvalidOperationException("JSOC returned zero or multiple records.");
        var keywords = root.GetProperty("keywords");
        
        string Keyword(int index) => keywords[index].GetProperty("values")[0].GetString() ?? throw new InvalidDataException("Missing JSOC keyword.");
        
        var segments = root.GetProperty("segments");
        var dims = segments[0].GetProperty("dims")[0].GetString()!.Split('x');
        var uris = new Dictionary<string, Uri>(StringComparer.OrdinalIgnoreCase);
        
        for (var i = 0; i < segments.GetArrayLength(); i++)
        {
            var item = segments[i]; var name = item.GetProperty("name").GetString()!; var path = item.GetProperty("values")[0].GetString()!;
            uris[name] = new Uri(new Uri("http://jsoc.stanford.edu"), path);
        }
        
        double Number(int i) => double.Parse(Keyword(i), CultureInfo.InvariantCulture);
        
        var metadata = new HmiObservationMetadata(Keyword(0), int.Parse(dims[0], CultureInfo.InvariantCulture), int.Parse(dims[1], CultureInfo.InvariantCulture),
            Number(1), Number(2), Number(3), Number(4), Number(5), Number(6), Number(7), Number(8),
            int.Parse(Keyword(9), CultureInfo.InvariantCulture), int.Parse(Keyword(10), CultureInfo.InvariantCulture));

        return new HmiObservationDescriptor(metadata, uris);
    }

    public async Task DownloadSegmentsAsync(HmiObservationDescriptor observation, string directory, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(directory);

        foreach (var (name, uri) in observation.Segments)
        {
            await using var input = await _httpClient.GetStreamAsync(uri, cancellationToken);
            await using var output = File.Create(Path.Combine(directory, name + ".fits"));
            await input.CopyToAsync(output, cancellationToken);
        }
    }
}

public static class JsocTime
{
    private static readonly DateTimeOffset Epoch = new(1977, 1, 1, 0, 0, 0, TimeSpan.Zero);
    public static double FromTaiCalendar(DateTimeOffset value) => (value.ToUniversalTime() - Epoch).TotalSeconds;
}
