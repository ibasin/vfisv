using System.Text.Json;
using System.Text.Json.Serialization;

namespace FitsLib;

public static class FitsDriver
{
    public static async Task<FloatImage[]> Get24Images(string inputDir, DateTime time, PixelRegion? region, bool downloadFitsFiles = true)
    {
        var jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true,
            NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals
        };
        var cancellationToken = CancellationToken.None;

        HmiObservationMetadata metadata;
        if (downloadFitsFiles)
        {
            var client = new JsocClient(new HttpClient());
            var descriptor = await client.DescribeAsync(time, cancellationToken);
            metadata = descriptor.Metadata;

            await client.DownloadSegmentsAsync(descriptor, inputDir, cancellationToken);

            Console.Write("Saving metadata.json... ");
            var json = JsonSerializer.Serialize(metadata, jsonOptions);
            await File.WriteAllTextAsync(Path.Combine(inputDir, "metadata.json"), json, cancellationToken);
            Console.WriteLine("Done!");
        }
        else
        {
            var metadataPath = Path.Combine(inputDir, "metadata.json");
            metadata = JsonSerializer.Deserialize<HmiObservationMetadata>(await File.ReadAllTextAsync(metadataPath, cancellationToken), jsonOptions) ?? throw new InvalidDataException("Observation metadata is empty.");
        }

        var images = FitsFloatImageReader.ReadFitsImagesInDirectory(inputDir, metadata);
        return images;
    }
}