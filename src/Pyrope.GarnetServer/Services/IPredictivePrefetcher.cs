
namespace Pyrope.GarnetServer.Services
{
    public interface IPredictivePrefetcher
    {
        void RecordInteraction(string tenantId, string indexName, int clusterId);
        int GetPrediction(string tenantId, string indexName, int currentClusterId);
    }
}
