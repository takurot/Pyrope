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
            if (dim % m != 0) throw new ArgumentException("Dimension must be divisible by M");
            if (k > 256) throw new ArgumentException("K must be <= 256 for byte encoding");

            Dim = dim;
            M = m;
            SubDim = dim / m;
            K = k;
            _centroids = new float[m][][];
        }

        public void Train(float[][] data)
        {
            if (data.Length == 0) return;

            // Train K-Means for each subspace
            // Parallelize across subspaces? Yes.

            System.Threading.Tasks.Parallel.For(0, M, mIdx =>
            {
                // Extract sub-vectors
                var subData = new System.Collections.Generic.List<float[]>(data.Length);
                for (int i = 0; i < data.Length; i++)
                {
                    var subVec = new float[SubDim];
                    Array.Copy(data[i], mIdx * SubDim, subVec, 0, SubDim);
                    subData.Add(subVec);
                }

                // Train
                // Note: PQ typically uses L2 for subspace quantization even if global metric is Cosine?
                // Actually, minimizing L2 in subspace minimizes L2 in full space.
                // For Cosine, usually vectors are normalized, and L2 approx is fine.
                var trained = KMeansUtils.Train(subData, K, SubDim, VectorMetric.L2, maxIter: 10, seed: 42 + mIdx);

                // Ensure we have exactly K centroids (fill with zeros or duplicates if not enough data)
                // KMeansUtils returns simpler list.
                _centroids[mIdx] = trained.ToArray();
            });

            _isTrained = true;
        }

        public byte[] Encode(float[] vector)
        {
            if (!_isTrained) throw new InvalidOperationException("PQ not trained");
            if (vector.Length != Dim) throw new ArgumentException("Vector dimension mismatch");

            var codes = new byte[M];

            // For each subspace
            for (int mIdx = 0; mIdx < M; mIdx++)
            {
                // Extract sub-vector (optimization: use Span/ArraySegment logic inside FindNearest)
                // Ideally prevent allow alloc. For now, Alloc is safest.
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
            if (code.Length != M) throw new ArgumentException("Code length mismatch");

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
                    // Subspace distance is always L2 squared usually for PQ ADC
                    // d(x, y) = sum(d_sub(x_i, y_i))
                    table[mIdx][k] = VectorMath.L2SquaredUnsafe(subQuery, centroids[k]);
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
