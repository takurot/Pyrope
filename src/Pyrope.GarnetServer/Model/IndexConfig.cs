using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Pyrope.GarnetServer.Model
{
    public class IndexConfig
    {
        [JsonPropertyName("dim")]
        public int Dimension { get; set; }

        [JsonPropertyName("metric")]
        public string Metric { get; set; } = "L2"; // L2, IP, COSINE

        [JsonPropertyName("algo")]
        public string Algorithm { get; set; } = "HNSW"; // HNSW, FLAT

        [JsonPropertyName("params")]
        public Dictionary<string, object> Parameters { get; set; } = new Dictionary<string, object>();
    }
}
