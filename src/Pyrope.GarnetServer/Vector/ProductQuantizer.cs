using System;

namespace Pyrope.GarnetServer.Vector
{
    public class ProductQuantizer
    {
        // Dimensions: [m][k][subDim]
        private float[][][] _centroids;
        private bool _isTrained = false;

        public int Dim { get; }
        public int M { get; }
        public int SubDim { get; }
        public int K { get; }

        public ProductQuantizer(int dim, int m, int k)
        {
            if (dim <= 0) throw new ArgumentOutOfRangeException(nameof(dim), "Dimension must be positive");
            if (m <= 0) throw new ArgumentOutOfRangeException(nameof(m), "M must be positive");
            if (k <= 0) throw new ArgumentOutOfRangeException(nameof(k), "K must be positive");
            if (k > 256) throw new ArgumentOutOfRangeException(nameof(k), "K must be <= 256 for byte encoding");
            if (dim % m != 0) throw new ArgumentException("Dimension must be divisible by M", nameof(dim));

            Dim = dim;
            M = m;
            SubDim = dim / m;
            K = k;
            _centroids = new float[m][][];
        }

        public void Train(float[][] data)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            if (data.Length == 0) return;

            for (int i = 0; i < data.Length; i++)
            {
                if (data[i] == null || data[i].Length != Dim)
                    throw new ArgumentException($"Training vector at index {i} has invalid dimension", nameof(data));
            }

            // Train K-Means for each subspace in parallel
            System.Threading.Tasks.Parallel.For(0, M, mIdx =>
            {
                var subData = new System.Collections.Generic.List<float[]>(data.Length);
                for (int i = 0; i < data.Length; i++)
                {
                    var subVec = new float[SubDim];
                    Array.Copy(data[i], mIdx * SubDim, subVec, 0, SubDim);
                    subData.Add(subVec);
                }

                var trained = KMeansUtils.Train(subData, K, SubDim, VectorMetric.L2, maxIter: 10, seed: 42 + mIdx);

                // Ensure exactly K centroids (pad with duplicates or zeros if trained.Count < K)
                var subspaceCentroids = new float[K][];
                if (trained.Count == 0)
                {
                    for (int kIdx = 0; kIdx < K; kIdx++)
                        subspaceCentroids[kIdx] = new float[SubDim];
                }
                else
                {
                    for (int kIdx = 0; kIdx < K; kIdx++)
                    {
                        var source = trained[kIdx % trained.Count];
                        subspaceCentroids[kIdx] = (float[])source.Clone();
                    }
                }

                _centroids[mIdx] = subspaceCentroids;
            });

            _isTrained = true;
        }

        public byte[] Encode(float[] vector)
        {
            if (!_isTrained) throw new InvalidOperationException("PQ not trained");
            if (vector == null) throw new ArgumentNullException(nameof(vector));
            if (vector.Length != Dim) throw new ArgumentException("Vector dimension mismatch", nameof(vector));

            var codes = new byte[M];

            // For each subspace
            for (int mIdx = 0; mIdx < M; mIdx++)
            {
                var subVec = new float[SubDim];
                Array.Copy(vector, mIdx * SubDim, subVec, 0, SubDim);

                var centroids = _centroids[mIdx];
                int bestK = FindNearest(subVec, centroids);
                codes[mIdx] = (byte)bestK;
            }
            return codes;
        }

        public float[] Decode(byte[] code)
        {
            if (!_isTrained) throw new InvalidOperationException("PQ not trained");
            if (code == null) throw new ArgumentNullException(nameof(code));
            if (code.Length != M) throw new ArgumentException("Code length mismatch", nameof(code));

            var vec = new float[Dim];
            for (int mIdx = 0; mIdx < M; mIdx++)
            {
                int kIdx = code[mIdx];
                if (kIdx >= _centroids[mIdx].Length) kIdx = 0; // Safety
                var c = _centroids[mIdx][kIdx];
                Array.Copy(c, 0, vec, mIdx * SubDim, SubDim);
            }
            return vec;
        }

        public float[][] ComputeDistanceTable(float[] query)
        {
            if (!_isTrained) throw new InvalidOperationException("PQ not trained");
            if (query == null) throw new ArgumentNullException(nameof(query));
            if (query.Length != Dim) throw new ArgumentException("Query dimension mismatch", nameof(query));

            var table = new float[M][];

            // For each subspace
            for (int mIdx = 0; mIdx < M; mIdx++)
            {
                table[mIdx] = new float[_centroids[mIdx].Length];
                var subQuery = new float[SubDim];
                Array.Copy(query, mIdx * SubDim, subQuery, 0, SubDim);

                var centroids = _centroids[mIdx];
                for (int k = 0; k < centroids.Length; k++)
                {
                    table[mIdx][k] = VectorMath.L2SquaredUnsafe(subQuery, centroids[k]);
                }
            }
            return table;
        }

        public float[][] ComputeInnerProductTable(float[] query)
        {
            if (!_isTrained) throw new InvalidOperationException("PQ not trained");
            if (query == null) throw new ArgumentNullException(nameof(query));
            if (query.Length != Dim) throw new ArgumentException("Query dimension mismatch", nameof(query));

            var table = new float[M][];

            // For each subspace
            for (int mIdx = 0; mIdx < M; mIdx++)
            {
                table[mIdx] = new float[_centroids[mIdx].Length];
                var subQuery = new float[SubDim];
                Array.Copy(query, mIdx * SubDim, subQuery, 0, SubDim);

                var centroids = _centroids[mIdx];
                for (int k = 0; k < centroids.Length; k++)
                {
                    table[mIdx][k] = VectorMath.DotProduct(subQuery, centroids[k]);
                }
            }
            return table;
        }

        private int FindNearest(float[] subVec, float[][] centroids)
        {
            float minD = float.MaxValue;
            int bestK = 0;
            for (int k = 0; k < centroids.Length; k++)
            {
                float d = VectorMath.L2SquaredUnsafe(subVec, centroids[k]);
                if (d < minD)
                {
                    minD = d;
                    bestK = k;
                }
            }
            return bestK;
        }
    }
}
