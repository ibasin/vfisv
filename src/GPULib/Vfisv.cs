using FitsLib;
using ILGPU;
using ILGPU.Runtime;

namespace GPULib;

public static class Vfisv
{
    public static FloatImage[] Process(FloatImage[] inputImages, bool forceCpuAccelerator = false)
    {
        Console.Write("\nLaunching GPU Kernel... ");
        if (inputImages.Length != 24) throw new ArgumentException("Input images array must have exactly 24 elements");

        using var context = Context.Create(builder => builder.Default().StaticFields(StaticFieldMode.MutableStaticFields).EnableAlgorithms());
        
        //Get CUDA devices. If CUDA device does not exist, OpenCL devices, otherwise CPU device
        var firstDevice =
            context.Devices.FirstOrDefault(x => x.AcceleratorType == AcceleratorType.Cuda) ??
            context.Devices.FirstOrDefault(x => x.AcceleratorType == AcceleratorType.OpenCL) ??
            context.Devices.Single(x => x.AcceleratorType == AcceleratorType.CPU);
        if (forceCpuAccelerator) firstDevice = context.Devices.Single(x => x.AcceleratorType == AcceleratorType.CPU);

        using var accelerator = firstDevice.CreateAccelerator(context);
        using var stream = accelerator.CreateStream();

        //prepare inputs to be copied to GPU memory
        var inputs = new float[GpuKernel.ImgDim, GpuKernel.ImgDim, 24];
        Parallel.For(0, GpuKernel.ImgDim, x =>
        {
            for (var y = 0; y < GpuKernel.ImgDim; y++)
            {
                for (var s = 0; s < 24; s++)
                {
                    inputs[x, y, s] = inputImages[s][x, y];
                }
            }
        });

        //TODO: check if I should use DenseXY or DenseZY
        using var inputsMB = accelerator.Allocate3DDenseXY<float>(new Index3D(GpuKernel.ImgDim, GpuKernel.ImgDim, 24));
        inputsMB.CopyFromCPU(stream, inputs);

        //TODO: check if I should use DenseXY or DenseZY
        using var outputsMB = accelerator.Allocate3DDenseXY<float>(new Index3D(GpuKernel.ImgDim, GpuKernel.ImgDim, 4));
        outputsMB.MemSetToZero(stream);

        //TODO: try 128 and 512 and benchmark performance
        const int threadsPerBlock = 256; 
        
        //TODO: figure out if we need to configure SharedMemory here too
        var launchDimension = new KernelConfig(new Index1D(GpuKernel.ImgDim * GpuKernel.ImgDim / threadsPerBlock), new Index1D(threadsPerBlock));

        var kernel = accelerator.LoadKernel<ArrayView3D<float, Stride3D.DenseXY>, ArrayView3D<float,Stride3D.DenseXY>>(GpuKernel.Launch);

        kernel(stream, launchDimension, inputsMB.View, outputsMB.View);

        stream.Synchronize();

        var outputs = new float[GpuKernel.ImgDim, GpuKernel.ImgDim, 4];
        outputsMB.CopyToCPU(stream, outputs);

        var outputImages = new FloatImage[4];
        for (var s = 0; s < 4; s++)
        {
            outputImages[s] = new FloatImage(GpuKernel.ImgDim, GpuKernel.ImgDim);
        }

        Parallel.For(0, GpuKernel.ImgDim, x =>
        {
            for (var y = 0; y < GpuKernel.ImgDim; y++)
            {
                for (var s = 0; s < 4; s++)
                {
                    outputImages[s][x, y] = outputs[x, y, s];
                }
            }
        });

        Console.WriteLine("Done!");
        return outputImages;
    }
}
