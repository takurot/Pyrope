using System.Collections.Generic;

namespace Pyrope.GarnetServer.Vector
{
    /// <summary>
    /// Provides access to centroids after an index has been built.
    /// Used to automatically sync centroids to SemanticClusterRegistry.
    /// </summary>
    public interface ICentroidsProvider
    {
        /// <summary>
        /// Returns the centroids computed during the last Build(), or null if not yet built.
        /// </summary>
        IReadOnlyList<float[]>? GetCentroids();
    }
}
