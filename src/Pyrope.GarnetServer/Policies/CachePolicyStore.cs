using System.Threading;
using Pyrope.GarnetServer.Model;

namespace Pyrope.GarnetServer.Policies
{
    public class CachePolicyStore
    {
        private CachePolicyConfig _config = new();

        public CachePolicyConfig Current => _config;

        public void Update(CachePolicyConfig config)
        {
            Interlocked.Exchange(ref _config, config);
        }
    }
}
