using System.Text.Json;
using System.Text.Json.Serialization;

namespace FitsLib;

public static class Driver
{
    public static async Task RunAsync(string inputDir, DateTime time, PixelRegion? region)
    {
        var jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true,
            NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals
        };
        var cancellationToken = CancellationToken.None;

        var client = new JsocClient(new HttpClient());
        var descriptor = await client.DescribeAsync(time, cancellationToken);

        await client.DownloadSegmentsAsync(descriptor, inputDir, cancellationToken);

        var json = JsonSerializer.Serialize(descriptor.Metadata, jsonOptions);
        await File.WriteAllTextAsync(Path.Combine(inputDir, "metadata.json"), json, cancellationToken);



    }
}