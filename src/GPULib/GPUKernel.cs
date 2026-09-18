using ILGPU;
using ILGPU.Runtime;
using System.Diagnostics;

namespace GPULib;

public static class GpuKernel
{
    #region Kernel Launch method
    public static void Launch(ArrayView3D<float, Stride3D.DenseZY> inputsAV3, ArrayView3D<float, Stride3D.DenseZY> outputsAV3)
    {
        int blockId = Grid.IdxX;
        int threadId = Group.IdxX;
        int threadsPerBlock = Group.DimX;
        int globalId = blockId * threadsPerBlock + threadId;

        var x = globalId % ImgDimX;
        var y = globalId / ImgDimX;

        //as a test, we copy first 4 input arrays into output arrays
        //for (var s = 0; s < 4; s++)
        //{
        //    outputsAV3.SetPixelValue(x, y, s, inputsAV3.GetPixelValue(x, y, s));
        //}

        //set up arrays for i, q, u, v values for this pixel
        var i = new float[6];
        var q = new float[6];
        var u = new float[6];
        var v = new float[6];

        //fill arrays with values from input ArrayView for this pixel
        for (var s = 0; s < 6; s++)
        {
            i[s] = inputsAV3.GetPixelValue(x, y, s);
            q[s] = inputsAV3.GetPixelValue(x, y, s + 6);
            u[s] = inputsAV3.GetPixelValue(x, y, s + 12);
            v[s] = inputsAV3.GetPixelValue(x, y, s + 18);
        }

        //process a single pixel and get the output values
        var (iOut, aOut, tOut, pOut) = RunSinglePixel(i, q, u, v);

        //move output values into output ArrayView for this pixel
        outputsAV3.SetPixelValue(x, y, 0, iOut);
        outputsAV3.SetPixelValue(x, y, 1, aOut);
        outputsAV3.SetPixelValue(x, y, 2, tOut);
        outputsAV3.SetPixelValue(x, y, 3, pOut);
    }
    #endregion

    #region Helper Methods
    //returns inclination, azimuth, temperature, pressure for a single pixel
    private static (float, float, float, float) RunSinglePixel(float[] i, float[] q, float[] u, float[] v)
    {
        Debug.Assert(i.Length == 6 && q.Length == 6 && u.Length == 6 && v.Length == 6);
        
        //TODO: this is where the actual VFISV algorithm will be implemented. For now, we just return some dummy values based on the input arrays.
        var sum = 0f;
        for (var j = 0; j < 6; j++)
        {
            sum += i[j] + q[j] + u[j] + v[j];
        }
        return (sum, sum + 1, sum + 2, sum + 3);
    }
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
    //image is a square of 4096x4096 pixels
    public const int ImgDimX = 4096; 
    public const int ImgDimY = 4096;
    #endregion
}
