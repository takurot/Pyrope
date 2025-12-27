using Pyrope.GarnetServer.Services;
using Pyrope.GarnetServer.Vector;
using Xunit;

namespace Pyrope.GarnetServer.Tests.Services
{
    public class VectorIndexRegistryTests
    {
        [Fact]
        public void IncrementEpoch_UpdatesIndexEpoch()
        {
            var registry = new VectorIndexRegistry();
            registry.GetOrCreate("tenant", "index", 2, VectorMetric.L2);

            Assert.Equal(0, registry.GetEpoch("tenant", "index"));

            registry.IncrementEpoch("tenant", "index");
            registry.IncrementEpoch("tenant", "index");

            Assert.Equal(2, registry.GetEpoch("tenant", "index"));
        }
    }
}
