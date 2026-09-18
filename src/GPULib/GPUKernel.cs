using ILGPU;
using ILGPU.Runtime;

namespace GPULib;

public static class GpuKernel
{
    #region Kernel Launch method
    //TODO: figure out if we should use DenseXY or DenseZY?
    public static void Launch(ArrayView3D<float, Stride3D.DenseZY> inputsAV3, ArrayView3D<float, Stride3D.DenseZY> outputsAV3)
    {
        int blockId = Grid.IdxX;
        int threadId = Group.IdxX;
        int threadsPerBlock = Group.DimX;
        int globalId = blockId * threadsPerBlock + threadId;

        var x = globalId / ImgDim;
        var y = globalId % ImgDim;

        //as a test, we copy first 4 input arrays into output arrays
        for (var s = 0; s < 4; s++)
        {
            outputsAV3.SetPixelValue(x, y, s, inputsAV3.GetPixelValue(x, y, s));
        }
    }
    #endregion

    #region Helper Methods
    private static float GetPixelValue(this ArrayView3D<float, Stride3D.DenseZY> meAV3, int x, int y, int segmentId)
    {
        return meAV3[new Index3D(x, y, segmentId)];
    }
    private static void SetPixelValue(this ArrayView3D<float, Stride3D.DenseZY> meAV3, int x, int y, int segmentId, float value)
    {
        meAV3[new Index3D(x, y, segmentId)] = value;
    }
    #endregion

    #region Constants
    public const int ImgDim = 4096; //image is a square of 4096x4096 pixels
    #endregion
}
