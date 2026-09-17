namespace FitsLib.Cli;

public class Program
{
    static async Task Main()
    {
        var inputDir = "observation";
        var time = DateTime.Parse("2/15/2012 3:24:00 PM");
        PixelRegion? region = new PixelRegion(1900, 1900, 64, 64);

        var client = new JsocClient(new HttpClient());
        var descriptor = await client.DescribeAsync(time);

        await client.DownloadSegmentsAsync(descriptor, inputDir);


    }
}