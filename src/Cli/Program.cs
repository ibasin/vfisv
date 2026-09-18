using FitsLib;
using GPULib;
using System.Diagnostics;
using System.Globalization;
using Utils;

namespace Cli;

public class Program
{
    static async Task Main()
    {
        //pick a date and time for which to download the 24 input images
        var time = DateTime.Parse("2/15/2012 3:24:00 PM");

        //download images from the web, save them in inputs directory and load them into memory
        var inputImages = await FitsProvider.Load24InputImages("input", time, false);

        //process the images on GPU
        var outputImages = Vfisv.ProcessOnGpu(inputImages);

        #if DEBUG
        //validate the output images by checking a pixel value at (2000, 2000) for each of the 4 output images
        const float tolerance = 0.0001f;
        using (new ConsoleTimer("Validating output images"))
        {
            Parallel.For(0, GpuKernel.ImgDimX, x =>
            {
                for (var y = 0; y < GpuKernel.ImgDimY; y++)
                {
                    var sum = 0f;
                    for (var i = 0; i < 24; i++)
                    {
                        sum += inputImages[i][x, y];
                    }
                    Debug.Assert((float.IsNaN(outputImages[0][x, y]) && float.IsNaN(sum)) || Math.Abs(outputImages[0][x, y] - sum) < tolerance);
                    Debug.Assert((float.IsNaN(outputImages[1][x, y]) && float.IsNaN(sum + 1)) || Math.Abs(outputImages[1][x, y] - sum - 1) < tolerance);
                    Debug.Assert((float.IsNaN(outputImages[2][x, y]) && float.IsNaN(sum + 2)) || Math.Abs(outputImages[2][x, y] - sum - 2) < tolerance);
                    Debug.Assert((float.IsNaN(outputImages[3][x, y]) && float.IsNaN(sum + 3)) || Math.Abs(outputImages[3][x, y] - sum - 3) < tolerance);
                }
            });
        }
        #endif

        //calculate prefix and save the 4 output images as files in output directory
        var prefix = time.ToString(CultureInfo.InvariantCulture).Replace("/", "-").Replace("\\", "-").Replace(" ", "-").Replace(":", "-");
        await FitsProvider.Save4OutputImages("output", prefix, outputImages);
    }
}