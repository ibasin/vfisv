using System.Text.Json;
using System.Text.Json.Serialization;
using Utils;

namespace FitsLib;

public static class FitsProvider
{
    public static async Task<FloatImage[]> Load24InputImages(string inputDir, DateTime time, bool downloadFitsFiles = true)
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

            using (new ConsoleTimer("Saving metadata.json"))
            {
                var json = JsonSerializer.Serialize(metadata, jsonOptions);
                await File.WriteAllTextAsync(Path.Combine(inputDir, "metadata.json"), json, cancellationToken);
            }
        }
        else
        {
            var metadataPath = Path.Combine(inputDir, "metadata.json");
            metadata = JsonSerializer.Deserialize<HmiObservationMetadata>(await File.ReadAllTextAsync(metadataPath, cancellationToken), jsonOptions) ?? throw new InvalidDataException("Observation metadata is empty.");
        }

        var images = FitsFloatImageReaderWriter.Read24InputFitsImagesInDirectory(inputDir, metadata);
        return images;
    }

    public static Task Save4OutputImages(string outputDir, string prefix, FloatImage[] images)
    {
        using (new ConsoleTimer("Saving output images"))
        {
            if (images.Length != 4) throw new ArgumentException("Expected exactly 4 images.", nameof(images));

            Directory.CreateDirectory(outputDir);
            var names = new[] { "Inclination", "Azimuth", "Temperature", "Pressure" };
            Parallel.For(0, images.Length, i =>
            {
                var path = Path.Combine(outputDir, $"{prefix}.{names[i]}.fits");
                FitsFloatImageReaderWriter.Write(path, images[i]);
            });

            return Task.CompletedTask;
        }
    }
}