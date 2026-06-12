using VSAI.AILogic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.CompilerServices;

namespace AILogic
{
    public static class MathUtil
    {
        public static Func<double[], double[], double> L2Norm_Squared_Double = (x, y) =>
        {
            double dist = 0f;
            for (int i = 0; i < x.Length; i++)
            {
                dist += (x[i] - y[i]) * (x[i] - y[i]);
            }

            return dist;
        };
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float Distance(Prediction a, Prediction b)
        {
            float dx = a.ScreenCenterX - b.ScreenCenterX;
            float dy = a.ScreenCenterY - b.ScreenCenterY;
            return dx * dx + dy * dy;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float CalculateTargetScore(
            Prediction candidate,
            Prediction? currentTarget,
            float predictedX,
            float predictedY,
            float currentLockScore,
            float maxLockScore,
            float threshold)
        {
            // Base score from distance to predicted position (where we expect current target to be)
            float dx = candidate.ScreenCenterX - predictedX;
            float dy = candidate.ScreenCenterY - predictedY;
            float distSq = dx * dx + dy * dy;

            // Normalize distance score (0 = far, 1 = close)
            float thresholdSq = threshold * threshold;
            float distanceScore = Math.Max(0f, 1f - (distSq / thresholdSq));

            // Confidence bonus (0-0.3 range)
            float confidenceBonus = candidate.Confidence * 0.3f;

            // Size bonus - larger targets are more stable (0-0.2 range)
            float area = candidate.Rectangle.Width * candidate.Rectangle.Height;
            float sizeBonus = Math.Min(0.2f, area / 50000f);

            // Lock bonus for current target (0-0.5 range based on accumulated score)
            float lockBonus = (currentTarget != null && distanceScore > 0.3f)
                ? (currentLockScore / maxLockScore) * 0.5f
                : 0f;

            return distanceScore + confidenceBonus + sizeBonus + lockBonus;
        }
        public static int CalculateNumDetections(int imageSize)
        {
            // YOLOv8 detection calculation: (size/8)² + (size/16)² + (size/32)²
            int stride8 = imageSize / 8;
            int stride16 = imageSize / 16;
            int stride32 = imageSize / 32;

            return (stride8 * stride8) + (stride16 * stride16) + (stride32 * stride32);
        }
        // LUT = look up table
        // REFERENCE: https://stackoverflow.com/questions/1089235/where-can-i-find-a-byte-to-float-lookup-table
        // "In this case, the lookup table should be faster than using direct calculation. The more complex the math (trigonometry, etc.), the bigger the performance gain."
        // although we used small calculations, something is better than nothing.
        private static readonly float[] _byteToFloatLut = CreateByteToFloatLut();
        private static float[] CreateByteToFloatLut()
        {
            var lut = new float[256];
            for (int i = 0; i < 256; i++)
                lut[i] = i / 255f;
            return lut;
        }

        // this new function reduces gc pressure as i stopped using array.copy
        // REFERENCE: https://www.codeproject.com/Articles/617613/Fast-Pixel-Operations-in-NET-With-and-Without-unsa
        public static unsafe void BitmapToFloatArrayInPlace(Bitmap image, float[] result, int IMAGE_SIZE)
        {
            if (image == null) throw new ArgumentNullException(nameof(image));
            if (result == null) throw new ArgumentNullException(nameof(result));

            int width = IMAGE_SIZE;
            int height = IMAGE_SIZE;
            int totalPixels = width * height;

            // check if it has the right size
            if (result.Length != 3 * totalPixels)
                throw new ArgumentException($"result must be length {3 * totalPixels}", nameof(result));

            var rect = new Rectangle(0, 0, width, height);

            // Lock the bitmap
            var bmpData = image.LockBits(rect, ImageLockMode.ReadOnly, image.PixelFormat);
            try
            {
                byte* basePtr = (byte*)bmpData.Scan0;
                int stride = Math.Abs(bmpData.Stride); //handle negative stride, topdown vs bottomup

                // array offsets for the three color channels
                const int bytesPerPixel = 4;
                const int pixelsPerIteration = 4; // process 4 pixels at a time

                int rOffset = 0; // Red channel starts at index 0
                int gOffset = totalPixels; // Green channel starts after red
                int bOffset = totalPixels * 2; // Blue channel starts after green

                // prevent gc from moving the array while we are using it
                fixed (float* dest = result)
                {
                    float* rPtr = dest + rOffset;
                    float* gPtr = dest + gOffset;
                    float* bPtr = dest + bOffset;

                    // Sequential processing is faster due to 0 thread scheduling overhead on small images
                    for (int y = 0; y < height; y++)
                    {
                        byte* p = basePtr + (long)y * stride;
                        int rowStart = y * width;
                        int x = 0;

                        int widthLimit = width - pixelsPerIteration + 1;
                        for (; x < widthLimit; x += pixelsPerIteration)
                        {
                            int baseIdx = rowStart + x;

                            // process 1st pixel / pixel 0
                            bPtr[baseIdx] = _byteToFloatLut[p[0]];
                            gPtr[baseIdx] = _byteToFloatLut[p[1]];
                            rPtr[baseIdx] = _byteToFloatLut[p[2]];

                            // pixel 1
                            bPtr[baseIdx + 1] = _byteToFloatLut[p[4]];
                            gPtr[baseIdx + 1] = _byteToFloatLut[p[5]];
                            rPtr[baseIdx + 1] = _byteToFloatLut[p[6]];

                            // pixel 2
                            bPtr[baseIdx + 2] = _byteToFloatLut[p[8]];
                            gPtr[baseIdx + 2] = _byteToFloatLut[p[9]];
                            rPtr[baseIdx + 2] = _byteToFloatLut[p[10]];

                            // pixel 3
                            bPtr[baseIdx + 3] = _byteToFloatLut[p[12]];
                            gPtr[baseIdx + 3] = _byteToFloatLut[p[13]];
                            rPtr[baseIdx + 3] = _byteToFloatLut[p[14]];

                            p += 16;
                        }

                        for (; x < width; x++)
                        {
                            int idx = rowStart + x;
                            bPtr[idx] = _byteToFloatLut[p[0]];
                            gPtr[idx] = _byteToFloatLut[p[1]];
                            rPtr[idx] = _byteToFloatLut[p[2]];
                            p += 4;
                        }
                    }
                }
            }
            finally
            {
                image.UnlockBits(bmpData);
            }
        }
    }
}
