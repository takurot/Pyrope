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

        private static void ValidateInput(float[] a, float[] b)
        {
            if (a == null) throw new ArgumentNullException(nameof(a));
            if (b == null) throw new ArgumentNullException(nameof(b));
            if (a.Length != b.Length) throw new ArgumentException("Vector dimension mismatch");
        }
    }
}
