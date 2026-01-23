using System;
using System.Numerics;

namespace Pyrope.GarnetServer.Vector
{
    public static class VectorMath
    {
        public static float DotProduct(float[] a, float[] b)
        {
            ValidateInput(a, b);

            int vectorSize = System.Numerics.Vector<float>.Count;
            int i = 0;
            float sum = 0f;

            if (a.Length >= vectorSize)
            {
                var vSum = System.Numerics.Vector<float>.Zero;
                int end = a.Length - vectorSize;

                for (; i <= end; i += vectorSize)
                {
                    var va = new System.Numerics.Vector<float>(a, i);
                    var vb = new System.Numerics.Vector<float>(b, i);
                    vSum += va * vb;
                }

                sum += System.Numerics.Vector.Dot(vSum, System.Numerics.Vector<float>.One);
            }

            for (; i < a.Length; i++)
            {
                sum += a[i] * b[i];
            }

            return sum;
        }

        public static float L2Squared(float[] a, float[] b)
        {
            ValidateInput(a, b);

            int vectorSize = System.Numerics.Vector<float>.Count;
            int i = 0;
            float sum = 0f;

            if (a.Length >= vectorSize)
            {
                var vSum = System.Numerics.Vector<float>.Zero;
                int end = a.Length - vectorSize;

                for (; i <= end; i += vectorSize)
                {
                    var va = new System.Numerics.Vector<float>(a, i);
                    var vb = new System.Numerics.Vector<float>(b, i);
                    var diff = va - vb;
                    vSum += diff * diff;
                }

                sum += System.Numerics.Vector.Dot(vSum, System.Numerics.Vector<float>.One);
            }

            for (; i < a.Length; i++)
            {
                var diff = a[i] - b[i];
                sum += diff * diff;
            }

            return sum;
        }

        public static float ComputeNorm(float[] vector)
        {
            if (vector == null) throw new ArgumentNullException(nameof(vector));

            int vectorSize = System.Numerics.Vector<float>.Count;
            int i = 0;
            float sum = 0f;

            if (vector.Length >= vectorSize)
            {
                var vSum = System.Numerics.Vector<float>.Zero;
                int end = vector.Length - vectorSize;

                for (; i <= end; i += vectorSize)
                {
                    var v = new System.Numerics.Vector<float>(vector, i);
                    vSum += v * v;
                }

                sum += System.Numerics.Vector.Dot(vSum, System.Numerics.Vector<float>.One);
            }

            for (; i < vector.Length; i++)
            {
                sum += vector[i] * vector[i];
            }

            return MathF.Sqrt(sum);
        }

        public static float Cosine(float[] query, float[] vector, float queryNorm, float vectorNorm)
        {
            // Hot path optimization: Skip validation or assume caller handles it
            if (queryNorm < 1e-6f || vectorNorm < 1e-6f) return 0f;

            var dot = DotProduct(query, vector);
            return dot / (queryNorm * vectorNorm);
        }

        public static float Cosine(float[] query, float[] vector, float vectorNorm)
        {
            ValidateInput(query, vector);
            var queryNorm = ComputeNorm(query);
            return Cosine(query, vector, queryNorm, vectorNorm);
        }

        // Overload for cases where we don't have precomputed norm (less efficient)
        public static float Cosine(float[] a, float[] b)
        {
            ValidateInput(a, b);
            var normA = ComputeNorm(a);
            var normB = ComputeNorm(b);
            return Cosine(a, b, normA, normB);
        }


        public static unsafe float DotProductUnsafe(float[] a, float[] b)
        {
            ValidateInput(a, b);

            int vectorSize = Vector<float>.Count;
            int length = a.Length;
            int i = 0;
            float sum = 0f;

            if (length >= vectorSize * 4)
            {
                var acc1 = Vector<float>.Zero;
                var acc2 = Vector<float>.Zero;
                var acc3 = Vector<float>.Zero;
                var acc4 = Vector<float>.Zero;

                int end = length - (vectorSize * 4);

                fixed (float* pA = a)
                fixed (float* pB = b)
                {
                    while (i <= end)
                    {
                        acc1 += *(Vector<float>*)(pA + i) * *(Vector<float>*)(pB + i);
                        acc2 += *(Vector<float>*)(pA + i + vectorSize) * *(Vector<float>*)(pB + i + vectorSize);
                        acc3 += *(Vector<float>*)(pA + i + vectorSize * 2) * *(Vector<float>*)(pB + i + vectorSize * 2);
                        acc4 += *(Vector<float>*)(pA + i + vectorSize * 3) * *(Vector<float>*)(pB + i + vectorSize * 3);
                        i += vectorSize * 4;
                    }
                }

                var finalAcc = acc1 + acc2 + acc3 + acc4;
                sum += System.Numerics.Vector.Dot(finalAcc, Vector<float>.One);
            }

            // Handle remaining vectors (1 to 3 blocks)
            if (i <= length - vectorSize)
            {
                var acc = Vector<float>.Zero;
                fixed (float* pA = a)
                fixed (float* pB = b)
                {
                    while (i <= length - vectorSize)
                    {
                        acc += *(Vector<float>*)(pA + i) * *(Vector<float>*)(pB + i);
                        i += vectorSize;
                    }
                }
                sum += System.Numerics.Vector.Dot(acc, Vector<float>.One);
            }

            // Scalar tail
            for (; i < length; i++)
            {
                sum += a[i] * b[i];
            }

            return sum;
        }

        public static unsafe float L2SquaredUnsafe(float[] a, float[] b)
        {
            ValidateInput(a, b);

            int vectorSize = Vector<float>.Count;
            int length = a.Length;
            int i = 0;
            float sum = 0f;

            if (length >= vectorSize * 4)
            {
                var acc1 = Vector<float>.Zero;
                var acc2 = Vector<float>.Zero;
                var acc3 = Vector<float>.Zero;
                var acc4 = Vector<float>.Zero;

                int end = length - (vectorSize * 4);

                fixed (float* pA = a)
                fixed (float* pB = b)
                {
                    while (i <= end)
                    {
                        var d1 = *(Vector<float>*)(pA + i) - *(Vector<float>*)(pB + i);
                        var d2 = *(Vector<float>*)(pA + i + vectorSize) - *(Vector<float>*)(pB + i + vectorSize);
                        var d3 = *(Vector<float>*)(pA + i + vectorSize * 2) - *(Vector<float>*)(pB + i + vectorSize * 2);
                        var d4 = *(Vector<float>*)(pA + i + vectorSize * 3) - *(Vector<float>*)(pB + i + vectorSize * 3);

                        acc1 += d1 * d1;
                        acc2 += d2 * d2;
                        acc3 += d3 * d3;
                        acc4 += d4 * d4;
                        i += vectorSize * 4;
                    }
                }

                var finalAcc = acc1 + acc2 + acc3 + acc4;
                sum += System.Numerics.Vector.Dot(finalAcc, Vector<float>.One);
            }

            // Handle remaining vectors (1 to 3 blocks)
            if (i <= length - vectorSize)
            {
                var acc = Vector<float>.Zero;
                fixed (float* pA = a)
                fixed (float* pB = b)
                {
                    while (i <= length - vectorSize)
                    {
                        var diff = *(Vector<float>*)(pA + i) - *(Vector<float>*)(pB + i);
                        acc += diff * diff;
                        i += vectorSize;
                    }
                }
                sum += System.Numerics.Vector.Dot(acc, Vector<float>.One);
            }

            // Scalar tail
            for (; i < length; i++)
            {
                var diff = a[i] - b[i];
                sum += diff * diff;
            }

            return sum;
        }

        public static float L2Squared(ReadOnlySpan<float> a, ReadOnlySpan<float> b)
        {
            if (a.Length != b.Length) throw new ArgumentException("Vector dimension mismatch");
            return L2SquaredUnsafe(a, b);
        }

        public static float DotProduct(ReadOnlySpan<float> a, ReadOnlySpan<float> b)
        {
            if (a.Length != b.Length) throw new ArgumentException("Vector dimension mismatch");
            return DotProductUnsafe(a, b);
        }

        public static float Cosine(ReadOnlySpan<float> a, ReadOnlySpan<float> b)
        {
            // Assume normalized if this is called in hot path, or compute norm?
            // Safest to compute norm.
            float nA = ComputeNorm(a);
            float nB = ComputeNorm(b);
            return DotProductUnsafe(a, b) / (nA * nB);
        }

        public static float ComputeNorm(ReadOnlySpan<float> vector)
        {
            // Similar implementation to array version
            int vectorSize = Vector<float>.Count;
            int length = vector.Length;
            int i = 0;
            float sum = 0f;

            if (length >= vectorSize)
            {
                var vSum = Vector<float>.Zero;
                int end = length - vectorSize;

                // Unsafe span access
                unsafe
                {
                    fixed (float* p = vector)
                    {
                        while (i <= end)
                        {
                            var v = *(Vector<float>*)(p + i);
                            vSum += v * v;
                            i += vectorSize;
                        }
                    }
                }
                sum += System.Numerics.Vector.Dot(vSum, System.Numerics.Vector<float>.One);
            }

            for (; i < length; i++)
            {
                sum += vector[i] * vector[i];
            }
            return MathF.Sqrt(sum);
        }

        public static unsafe float L2SquaredUnsafe(ReadOnlySpan<float> a, ReadOnlySpan<float> b)
        {
            int vectorSize = System.Numerics.Vector<float>.Count;
            int length = a.Length;
            int i = 0;
            float sum = 0f;

            if (length >= vectorSize * 4)
            {
                var acc1 = System.Numerics.Vector<float>.Zero;
                var acc2 = System.Numerics.Vector<float>.Zero;
                var acc3 = System.Numerics.Vector<float>.Zero;
                var acc4 = System.Numerics.Vector<float>.Zero;
                int end = length - (vectorSize * 4);

                fixed (float* pA = a)
                fixed (float* pB = b)
                {
                    while (i <= end)
                    {
                        var d1 = *(System.Numerics.Vector<float>*)(pA + i) - *(System.Numerics.Vector<float>*)(pB + i);
                        var d2 = *(System.Numerics.Vector<float>*)(pA + i + vectorSize) - *(System.Numerics.Vector<float>*)(pB + i + vectorSize);
                        var d3 = *(System.Numerics.Vector<float>*)(pA + i + vectorSize * 2) - *(System.Numerics.Vector<float>*)(pB + i + vectorSize * 2);
                        var d4 = *(System.Numerics.Vector<float>*)(pA + i + vectorSize * 3) - *(System.Numerics.Vector<float>*)(pB + i + vectorSize * 3);
                        acc1 += d1 * d1;
                        acc2 += d2 * d2;
                        acc3 += d3 * d3;
                        acc4 += d4 * d4;
                        i += vectorSize * 4;
                    }
                }
                var finalAcc = acc1 + acc2 + acc3 + acc4;
                sum += System.Numerics.Vector.Dot(finalAcc, System.Numerics.Vector<float>.One);
            }

            // Remainder
            if (i <= length - vectorSize)
            {
                var acc = System.Numerics.Vector<float>.Zero;
                fixed (float* pA = a)
                fixed (float* pB = b)
                {
                    while (i <= length - vectorSize)
                    {
                        var diff = *(System.Numerics.Vector<float>*)(pA + i) - *(System.Numerics.Vector<float>*)(pB + i);
                        acc += diff * diff;
                        i += vectorSize;
                    }
                }
                sum += System.Numerics.Vector.Dot(acc, System.Numerics.Vector<float>.One);
            }

            for (; i < length; i++)
            {
                var diff = a[i] - b[i];
                sum += diff * diff;
            }

            return sum;
        }

        public static unsafe float DotProductUnsafe(ReadOnlySpan<float> a, ReadOnlySpan<float> b)
        {
            // Similar to array version but with Span
            int vectorSize = System.Numerics.Vector<float>.Count;
            int length = a.Length;
            int i = 0;
            float sum = 0f;

            if (length >= vectorSize * 4)
            {
                var acc1 = System.Numerics.Vector<float>.Zero; var acc2 = System.Numerics.Vector<float>.Zero;
                var acc3 = System.Numerics.Vector<float>.Zero; var acc4 = System.Numerics.Vector<float>.Zero;
                int end = length - (vectorSize * 4);

                fixed (float* pA = a) fixed (float* pB = b)
                {
                    while (i <= end)
                    {
                        acc1 += *(System.Numerics.Vector<float>*)(pA + i) * *(System.Numerics.Vector<float>*)(pB + i);
                        acc2 += *(System.Numerics.Vector<float>*)(pA + i + vectorSize) * *(System.Numerics.Vector<float>*)(pB + i + vectorSize);
                        acc3 += *(System.Numerics.Vector<float>*)(pA + i + vectorSize * 2) * *(System.Numerics.Vector<float>*)(pB + i + vectorSize * 2);
                        acc4 += *(System.Numerics.Vector<float>*)(pA + i + vectorSize * 3) * *(System.Numerics.Vector<float>*)(pB + i + vectorSize * 3);
                        i += vectorSize * 4;
                    }
                }
                sum += System.Numerics.Vector.Dot(acc1 + acc2 + acc3 + acc4, System.Numerics.Vector<float>.One);
            }
            // Remainder loop ... for brevity, falling back to simple loop for remainder < 4 blocks
            // Actually should implement properly for correctness

            if (i <= length - vectorSize)
            {
                var acc = System.Numerics.Vector<float>.Zero;
                fixed (float* pA = a) fixed (float* pB = b)
                {
                    while (i <= length - vectorSize)
                    {
                        acc += *(System.Numerics.Vector<float>*)(pA + i) * *(System.Numerics.Vector<float>*)(pB + i);
                        i += vectorSize;
                    }
                }
                sum += System.Numerics.Vector.Dot(acc, System.Numerics.Vector<float>.One);
            }

            for (; i < length; i++) sum += a[i] * b[i];
            return sum;
        }

        private static void ValidateInput(float[] a, float[] b)
        {
            if (a == null) throw new ArgumentNullException(nameof(a));
            if (b == null) throw new ArgumentNullException(nameof(b));
            if (a.Length != b.Length) throw new ArgumentException("Vector dimension mismatch");
        }

        private static void ValidateInput(byte[] a, byte[] b)
        {
            if (a == null) throw new ArgumentNullException(nameof(a));
            if (b == null) throw new ArgumentNullException(nameof(b));
            if (a.Length != b.Length) throw new ArgumentException("Vector dimension mismatch");
        }

        public static long L2Squared8Bit(byte[] a, byte[] b)
        {
            ValidateInput(a, b);
            return L2Squared8Bit(new ReadOnlySpan<byte>(a), new ReadOnlySpan<byte>(b));
        }

        public static unsafe long L2Squared8Bit(ReadOnlySpan<byte> a, ReadOnlySpan<byte> b)
        {
            if (a.Length != b.Length) throw new ArgumentException("Vector dimension mismatch");

            long sum = 0;
            int length = a.Length;
            int i = 0;

            if (System.Numerics.Vector.IsHardwareAccelerated && length >= System.Numerics.Vector<byte>.Count)
            {
                int vectorCount = System.Numerics.Vector<byte>.Count;
                int stride = vectorCount * 4;
                int end = length - stride;

                var vSumInt = System.Numerics.Vector<int>.Zero;

                fixed (byte* pA = a)
                fixed (byte* pB = b)
                {
                    while (i <= end)
                    {
                        {
                            var va = *(System.Numerics.Vector<byte>*)(pA + i);
                            var vb = *(System.Numerics.Vector<byte>*)(pB + i);
                            System.Numerics.Vector.Widen(va, out var vaLow, out var vaHigh);
                            System.Numerics.Vector.Widen(vb, out var vbLow, out var vbHigh);

                            var diffLow = System.Numerics.Vector.Max(vaLow, vbLow) - System.Numerics.Vector.Min(vaLow, vbLow);
                            var diffHigh = System.Numerics.Vector.Max(vaHigh, vbHigh) - System.Numerics.Vector.Min(vaHigh, vbHigh);

                            var sqLow = diffLow * diffLow;
                            var sqHigh = diffHigh * diffHigh;

                            System.Numerics.Vector.Widen(sqLow, out var sLow1, out var sLow2);
                            System.Numerics.Vector.Widen(sqHigh, out var sHigh1, out var sHigh2);

                            vSumInt += System.Numerics.Vector.AsVectorInt32(sLow1);
                            vSumInt += System.Numerics.Vector.AsVectorInt32(sLow2);
                            vSumInt += System.Numerics.Vector.AsVectorInt32(sHigh1);
                            vSumInt += System.Numerics.Vector.AsVectorInt32(sHigh2);
                        }
                        {
                            var va = *(System.Numerics.Vector<byte>*)(pA + i + vectorCount);
                            var vb = *(System.Numerics.Vector<byte>*)(pB + i + vectorCount);
                            System.Numerics.Vector.Widen(va, out var vaLow, out var vaHigh);
                            System.Numerics.Vector.Widen(vb, out var vbLow, out var vbHigh);
                            var diffLow = System.Numerics.Vector.Max(vaLow, vbLow) - System.Numerics.Vector.Min(vaLow, vbLow);
                            var diffHigh = System.Numerics.Vector.Max(vaHigh, vbHigh) - System.Numerics.Vector.Min(vaHigh, vbHigh);
                            var sqLow = diffLow * diffLow;
                            var sqHigh = diffHigh * diffHigh;
                            System.Numerics.Vector.Widen(sqLow, out var sLow1, out var sLow2);
                            System.Numerics.Vector.Widen(sqHigh, out var sHigh1, out var sHigh2);
                            vSumInt += System.Numerics.Vector.AsVectorInt32(sLow1);
                            vSumInt += System.Numerics.Vector.AsVectorInt32(sLow2);
                            vSumInt += System.Numerics.Vector.AsVectorInt32(sHigh1);
                            vSumInt += System.Numerics.Vector.AsVectorInt32(sHigh2);
                        }
                        {
                            var va = *(System.Numerics.Vector<byte>*)(pA + i + vectorCount * 2);
                            var vb = *(System.Numerics.Vector<byte>*)(pB + i + vectorCount * 2);
                            System.Numerics.Vector.Widen(va, out var vaLow, out var vaHigh);
                            System.Numerics.Vector.Widen(vb, out var vbLow, out var vbHigh);
                            var diffLow = System.Numerics.Vector.Max(vaLow, vbLow) - System.Numerics.Vector.Min(vaLow, vbLow);
                            var diffHigh = System.Numerics.Vector.Max(vaHigh, vbHigh) - System.Numerics.Vector.Min(vaHigh, vbHigh);
                            var sqLow = diffLow * diffLow;
                            var sqHigh = diffHigh * diffHigh;
                            System.Numerics.Vector.Widen(sqLow, out var sLow1, out var sLow2);
                            System.Numerics.Vector.Widen(sqHigh, out var sHigh1, out var sHigh2);
                            vSumInt += System.Numerics.Vector.AsVectorInt32(sLow1);
                            vSumInt += System.Numerics.Vector.AsVectorInt32(sLow2);
                            vSumInt += System.Numerics.Vector.AsVectorInt32(sHigh1);
                            vSumInt += System.Numerics.Vector.AsVectorInt32(sHigh2);
                        }
                        {
                            var va = *(System.Numerics.Vector<byte>*)(pA + i + vectorCount * 3);
                            var vb = *(System.Numerics.Vector<byte>*)(pB + i + vectorCount * 3);
                            System.Numerics.Vector.Widen(va, out var vaLow, out var vaHigh);
                            System.Numerics.Vector.Widen(vb, out var vbLow, out var vbHigh);
                            var diffLow = System.Numerics.Vector.Max(vaLow, vbLow) - System.Numerics.Vector.Min(vaLow, vbLow);
                            var diffHigh = System.Numerics.Vector.Max(vaHigh, vbHigh) - System.Numerics.Vector.Min(vaHigh, vbHigh);
                            var sqLow = diffLow * diffLow;
                            var sqHigh = diffHigh * diffHigh;
                            System.Numerics.Vector.Widen(sqLow, out var sLow1, out var sLow2);
                            System.Numerics.Vector.Widen(sqHigh, out var sHigh1, out var sHigh2);
                            vSumInt += System.Numerics.Vector.AsVectorInt32(sLow1);
                            vSumInt += System.Numerics.Vector.AsVectorInt32(sLow2);
                            vSumInt += System.Numerics.Vector.AsVectorInt32(sHigh1);
                            vSumInt += System.Numerics.Vector.AsVectorInt32(sHigh2);
                        }

                        i += stride;
                    }

                    while (i <= length - vectorCount)
                    {
                        var va = *(System.Numerics.Vector<byte>*)(pA + i);
                        var vb = *(System.Numerics.Vector<byte>*)(pB + i);
                        System.Numerics.Vector.Widen(va, out var vaLow, out var vaHigh);
                        System.Numerics.Vector.Widen(vb, out var vbLow, out var vbHigh);
                        var diffLow = System.Numerics.Vector.Max(vaLow, vbLow) - System.Numerics.Vector.Min(vaLow, vbLow);
                        var diffHigh = System.Numerics.Vector.Max(vaHigh, vbHigh) - System.Numerics.Vector.Min(vaHigh, vbHigh);
                        var sqLow = diffLow * diffLow;
                        var sqHigh = diffHigh * diffHigh;
                        System.Numerics.Vector.Widen(sqLow, out var sLow1, out var sLow2);
                        System.Numerics.Vector.Widen(sqHigh, out var sHigh1, out var sHigh2);
                        vSumInt += System.Numerics.Vector.AsVectorInt32(sLow1);
                        vSumInt += System.Numerics.Vector.AsVectorInt32(sLow2);
                        vSumInt += System.Numerics.Vector.AsVectorInt32(sHigh1);
                        vSumInt += System.Numerics.Vector.AsVectorInt32(sHigh2);
                        i += vectorCount;
                    }
                }

                sum += System.Numerics.Vector.Dot(vSumInt, System.Numerics.Vector<int>.One);
            }

            for (; i < length; i++)
            {
                int diff = a[i] - b[i];
                sum += diff * diff;
            }

            return sum;
        }

        public static long DotProduct8Bit(byte[] a, byte[] b)
        {
            ValidateInput(a, b);
            return DotProduct8Bit(new ReadOnlySpan<byte>(a), new ReadOnlySpan<byte>(b));
        }

        public static unsafe long DotProduct8Bit(ReadOnlySpan<byte> a, ReadOnlySpan<byte> b)
        {
            if (a.Length != b.Length) throw new ArgumentException("Vector dimension mismatch");

            long sum = 0;
            int length = a.Length;
            int i = 0;

            if (System.Numerics.Vector.IsHardwareAccelerated && length >= System.Numerics.Vector<byte>.Count)
            {
                int vectorCount = System.Numerics.Vector<byte>.Count;
                var vSumInt = System.Numerics.Vector<int>.Zero;
                int stride = vectorCount * 4;
                int end = length - stride;

                fixed (byte* pA = a)
                fixed (byte* pB = b)
                {
                    while (i <= end)
                    {
                        {
                            var va = *(System.Numerics.Vector<byte>*)(pA + i);
                            var vb = *(System.Numerics.Vector<byte>*)(pB + i);
                            System.Numerics.Vector.Widen(va, out var vaLow, out var vaHigh);
                            System.Numerics.Vector.Widen(vb, out var vbLow, out var vbHigh);

                            var mulLow = vaLow * vbLow;
                            var mulHigh = vaHigh * vbHigh;

                            System.Numerics.Vector.Widen(mulLow, out var sLow1, out var sLow2);
                            System.Numerics.Vector.Widen(mulHigh, out var sHigh1, out var sHigh2);

                            vSumInt += System.Numerics.Vector.AsVectorInt32(sLow1);
                            vSumInt += System.Numerics.Vector.AsVectorInt32(sLow2);
                            vSumInt += System.Numerics.Vector.AsVectorInt32(sHigh1);
                            vSumInt += System.Numerics.Vector.AsVectorInt32(sHigh2);
                        }
                        {
                            var va = *(System.Numerics.Vector<byte>*)(pA + i + vectorCount);
                            var vb = *(System.Numerics.Vector<byte>*)(pB + i + vectorCount);
                            System.Numerics.Vector.Widen(va, out var vaLow, out var vaHigh);
                            System.Numerics.Vector.Widen(vb, out var vbLow, out var vbHigh);
                            var mulLow = vaLow * vbLow;
                            var mulHigh = vaHigh * vbHigh;
                            System.Numerics.Vector.Widen(mulLow, out var sLow1, out var sLow2);
                            System.Numerics.Vector.Widen(mulHigh, out var sHigh1, out var sHigh2);
                            vSumInt += System.Numerics.Vector.AsVectorInt32(sLow1);
                            vSumInt += System.Numerics.Vector.AsVectorInt32(sLow2);
                            vSumInt += System.Numerics.Vector.AsVectorInt32(sHigh1);
                            vSumInt += System.Numerics.Vector.AsVectorInt32(sHigh2);
                        }
                        {
                            var va = *(System.Numerics.Vector<byte>*)(pA + i + vectorCount * 2);
                            var vb = *(System.Numerics.Vector<byte>*)(pB + i + vectorCount * 2);
                            System.Numerics.Vector.Widen(va, out var vaLow, out var vaHigh);
                            System.Numerics.Vector.Widen(vb, out var vbLow, out var vbHigh);
                            var mulLow = vaLow * vbLow;
                            var mulHigh = vaHigh * vbHigh;
                            System.Numerics.Vector.Widen(mulLow, out var sLow1, out var sLow2);
                            System.Numerics.Vector.Widen(mulHigh, out var sHigh1, out var sHigh2);
                            vSumInt += System.Numerics.Vector.AsVectorInt32(sLow1);
                            vSumInt += System.Numerics.Vector.AsVectorInt32(sLow2);
                            vSumInt += System.Numerics.Vector.AsVectorInt32(sHigh1);
                            vSumInt += System.Numerics.Vector.AsVectorInt32(sHigh2);
                        }
                        {
                            var va = *(System.Numerics.Vector<byte>*)(pA + i + vectorCount * 3);
                            var vb = *(System.Numerics.Vector<byte>*)(pB + i + vectorCount * 3);
                            System.Numerics.Vector.Widen(va, out var vaLow, out var vaHigh);
                            System.Numerics.Vector.Widen(vb, out var vbLow, out var vbHigh);
                            var mulLow = vaLow * vbLow;
                            var mulHigh = vaHigh * vbHigh;
                            System.Numerics.Vector.Widen(mulLow, out var sLow1, out var sLow2);
                            System.Numerics.Vector.Widen(mulHigh, out var sHigh1, out var sHigh2);
                            vSumInt += System.Numerics.Vector.AsVectorInt32(sLow1);
                            vSumInt += System.Numerics.Vector.AsVectorInt32(sLow2);
                            vSumInt += System.Numerics.Vector.AsVectorInt32(sHigh1);
                            vSumInt += System.Numerics.Vector.AsVectorInt32(sHigh2);
                        }

                        i += stride;
                    }

                    while (i <= length - vectorCount)
                    {
                        var va = *(System.Numerics.Vector<byte>*)(pA + i);
                        var vb = *(System.Numerics.Vector<byte>*)(pB + i);
                        System.Numerics.Vector.Widen(va, out var vaLow, out var vaHigh);
                        System.Numerics.Vector.Widen(vb, out var vbLow, out var vbHigh);
                        var mulLow = vaLow * vbLow;
                        var mulHigh = vaHigh * vbHigh;
                        System.Numerics.Vector.Widen(mulLow, out var sLow1, out var sLow2);
                        System.Numerics.Vector.Widen(mulHigh, out var sHigh1, out var sHigh2);
                        vSumInt += System.Numerics.Vector.AsVectorInt32(sLow1);
                        vSumInt += System.Numerics.Vector.AsVectorInt32(sLow2);
                        vSumInt += System.Numerics.Vector.AsVectorInt32(sHigh1);
                        vSumInt += System.Numerics.Vector.AsVectorInt32(sHigh2);
                        i += vectorCount;
                    }
                }

                sum += System.Numerics.Vector.Dot(vSumInt, System.Numerics.Vector<int>.One);
            }

            for (; i < length; i++)
            {
                sum += a[i] * b[i];
            }
            return sum;
        }
    }
}