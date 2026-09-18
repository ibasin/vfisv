using FitsLib;
using ILGPU;
using ILGPU.Runtime;
using Utils;

namespace GPULib;

public static class Vfisv
{
    public static FloatImage[] ProcessOnGpu(FloatImage[] inputImages, bool forceCpuAccelerator = false)
    {
        if (inputImages.Length != 24) throw new ArgumentException("Input images array must have exactly 24 elements");


        using var context = Context.Create(builder => builder.Default().StaticFields(StaticFieldMode.MutableStaticFields).EnableAlgorithms());

        //Get CUDA devices. If CUDA device does not exist, OpenCL devices, otherwise CPU device
        //var firstDevice =
        //    context.Devices.FirstOrDefault(x => x.AcceleratorType == AcceleratorType.Cuda) ??
        //    context.Devices.FirstOrDefault(x => x.AcceleratorType == AcceleratorType.OpenCL) ??
        //    context.Devices.Single(x => x.AcceleratorType == AcceleratorType.CPU);
        //if (forceCpuAccelerator) firstDevice = context.Devices.Single(x => x.AcceleratorType == AcceleratorType.CPU);

        var devices = context.Devices.Where(x => x.AcceleratorType == AcceleratorType.Cuda).ToArray();

        //prepare inputs to be copied to GPU memory
        var inputs = new float[devices.Length][,,];
        var outputs = new float[devices.Length][,,];
        for (var i = 0; i < devices.Length; i++)
        {
            var (start, end) = GetStartAndEnd(i, devices.Length);
            inputs[i] = new float[end - start, GpuKernel.ImgDimY, 24];
            outputs[i] = new float[end - start, GpuKernel.ImgDimY, 4];
        }

        Parallel.For(0, GpuKernel.ImgDimY, y =>
        //for(var y = 0; y < GpuKernel.ImgDimY; y++)
        {
            for (var i = 0; i < devices.Length; i++)
            {
                var (start, end) = GetStartAndEnd(i, devices.Length);
                for (var x = start; x < end; x++)
                {
                    for (var s = 0; s < 24; s++)
                    {
                        inputs[i][x - start, y, s] = inputImages[s][x, y];
                    }
                }
            }
        });

        using (new ConsoleTimer("In GPU"))
        {
            Parallel.For(0, devices.Length, i =>
            {
                var (start, end) = GetStartAndEnd(i, devices.Length);
                var size = end - start;

                // ReSharper disable AccessToDisposedClosure
                using var accelerator = devices[i].CreateAccelerator(context);
                // ReSharper restore AccessToDisposedClosure

                using var stream = accelerator.CreateStream();

                using var inputsMB = accelerator.Allocate3DDenseZY<float>(new Index3D(size, GpuKernel.ImgDimY, 24));
                inputsMB.View.AsGeneral().CopyFromCPU(stream, inputs[i]);

                using var outputsMB = accelerator.Allocate3DDenseZY<float>(new Index3D(size, GpuKernel.ImgDimY, 4));

                //TODO: try 128 and 512 and benchmark performance when kernel is complete
                const int threadsPerBlock = 256;

                //TODO: figure out if we need to configure SharedMemory here too
                var launchDimension = new KernelConfig(new Index1D(size * GpuKernel.ImgDimY / threadsPerBlock), new Index1D(threadsPerBlock));

                var kernel = accelerator.LoadKernel<ArrayView3D<float, Stride3D.DenseZY>, ArrayView3D<float, Stride3D.DenseZY>>(GpuKernel.Launch);

                kernel(stream, launchDimension, inputsMB.View, outputsMB.View);
                stream.Synchronize();

                outputsMB.View.AsGeneral().CopyToCPU(stream, outputs[i]);
            });
        }

        var outputImages = new FloatImage[4];
        for (var s = 0; s < 4; s++)
        {
            outputImages[s] = new FloatImage(GpuKernel.ImgDimX, GpuKernel.ImgDimY);
        }

        Parallel.For(0, GpuKernel.ImgDimY, y =>
        //for(var y = 0; y < GpuKernel.ImgDimY; y++)
        {
            for (var i = 0; i < devices.Length; i++)
            {
                var (start, end) = GetStartAndEnd(i, devices.Length);
                for (var x = start; x < end; x++)
                {
                    for (var s = 0; s < 4; s++)
                    {
                        outputImages[s][x, y] = outputs[i][x - start, y, s];
                    }
                }
            }
        });

        return outputImages;

        (int, int) GetStartAndEnd(int i, int numDevices)
        {
            var sizeX = GpuKernel.ImgDimX / numDevices;
            var start = sizeX * i;
            var end = i == numDevices - 1 ? GpuKernel.ImgDimX : start + sizeX;
            return (start, end);
        }
    }
}
