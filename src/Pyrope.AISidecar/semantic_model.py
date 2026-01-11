
import numpy as np
import requests
import logging
from sklearn.cluster import KMeans

logger = logging.getLogger(__name__)

class SemanticModelTrainer:
    def __init__(self, n_clusters=256, random_state=42):
        self.n_clusters = n_clusters
        self.random_state = random_state

    def train_centroids(self, vectors: np.ndarray) -> np.ndarray:
        """
        Train KMeans on the provided vectors and return centroids.
        vectors: (N, D) numpy array
        returns: (K, D) centroids
        """
        if len(vectors) < self.n_clusters:
            logger.warning(f"Not enough vectors ({len(vectors)}) for {self.n_clusters} clusters. Reducing clusters.")
            n_clusters = max(1, len(vectors))
        else:
            n_clusters = self.n_clusters

        kmeans = KMeans(n_clusters=n_clusters, random_state=self.random_state, n_init=10)
        kmeans.fit(vectors)
        return kmeans.cluster_centers_

    def push_centroids(self, centroids: np.ndarray, tenant_id: str, index_name: str, garnet_base_url: str, api_key: str):
        """
        Push centroids to Garnet Server.
        """
        url = f"{garnet_base_url.rstrip('/')}/v1/indexes/{tenant_id}/{index_name}/centroids"
        dim = centroids.shape[1]
        
        # Convert to list of lists for JSON
        centroids_list = centroids.tolist()
        
        payload = {
            "dimension": dim,
            "centroids": centroids_list
        }
        
        headers = {
            "X-API-KEY": api_key
        }
        
        try:
            logger.info(f"Pushing {len(centroids_list)} centroids (dim={dim}) to {url}")
            resp = requests.post(url, json=payload, headers=headers, timeout=10)
            resp.raise_for_status()
            logger.info("Successfully pushed centroids.")
        except Exception as e:
            logger.error(f"Failed to push centroids: {e}")
            raise

if __name__ == "__main__":
    # Test execution
    logging.basicConfig(level=logging.INFO)
    
    # Generate dummy data
    data = np.random.rand(1000, 32).astype(np.float32)
    trainer = SemanticModelTrainer(n_clusters=10)
    centroids = trainer.train_centroids(data)
    
    # Uncomment to push if server is running
    trainer.push_centroids(centroids, "tenant_bench", "idx_bench", "http://localhost:5000", "admin123")
