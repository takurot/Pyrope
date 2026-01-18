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

        private static void ValidateInput(float[] a, float[] b)
        {
            if (a == null) throw new ArgumentNullException(nameof(a));
            if (b == null) throw new ArgumentNullException(nameof(b));
            if (a.Length != b.Length) throw new ArgumentException("Vector dimension mismatch");
        }
    }
}
