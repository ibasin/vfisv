using System.Reflection.Metadata;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FitsLib;

public static class FitsProvider
{
    public static async Task<FloatImage[]> Load24InputImages(string inputDir, DateTime time, PixelRegion? region, bool downloadFitsFiles = true)
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

        var images = FitsFloatImageReaderWriter.Read24InputFitsImagesInDirectory(inputDir, metadata);
        return images;
    }

    public static Task Save4OutputImages(string outputDir, string prefix, FloatImage[] images)
    {
        if (images.Length != 4) throw new ArgumentException("Expected exactly 4 images.", nameof(images));
        
        var inclination = images[0];
        var azimuth = images[1];
        var temperature = images[2];
        var pressure = images[3];
        
        Directory.CreateDirectory(outputDir);

        var inclinationPath = Path.Combine(outputDir, $"{prefix}.Inclination.fits");
        FitsFloatImageReaderWriter.Write(inclinationPath, inclination);
        
        var azimuthPath = Path.Combine(outputDir, $"{prefix}.Azimuth.fits");
        FitsFloatImageReaderWriter.Write(azimuthPath, azimuth);

        var temperaturePath = Path.Combine(outputDir, $"{prefix}.Temperature.fits");
        FitsFloatImageReaderWriter.Write(temperaturePath, temperature);

        var pressurePath = Path.Combine(outputDir, $"{prefix}.Pressure.fits");
        FitsFloatImageReaderWriter.Write(pressurePath, pressure);

        return Task.CompletedTask;
    }
}