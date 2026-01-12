from collections import defaultdict, Counter
import logging

logger = logging.getLogger(__name__)


class PredictionEngine:
    def __init__(self, max_tenants=1000, max_clusters_per_tenant=500):
        # transitions[tenant_index][current_cluster] = Counter(next_cluster)
        self.transitions = defaultdict(lambda: defaultdict(Counter))
        self.rules = {}  # cache for serving: { "tenant:index": { current: next } }
        self.last_cluster = {}  # { "tenant:index": last_cluster_id }
        self.max_tenants = max_tenants
        self.max_clusters_per_tenant = max_clusters_per_tenant

    def record_interaction(self, tenant_id: str, index_name: str, cluster_id: int):
        key = f"{tenant_id}:{index_name}"

        # Prune if too many tenants
        if len(self.transitions) >= self.max_tenants and key not in self.transitions:
            self._prune_tenants()

        last = self.last_cluster.get(key)
        if last is not None and last != cluster_id:
            # Prune if too many clusters for this tenant
            if len(self.transitions[key]) >= self.max_clusters_per_tenant and last not in self.transitions[key]:
                self._prune_clusters(key)

            # Record transition
            self.transitions[key][last][cluster_id] += 1

        self.last_cluster[key] = cluster_id

    def _prune_tenants(self):
        # Remove random or LRU tenant (random for simplicity here, or converting to OrderedDict for LRU)
        # For MVP, just clear 10%
        keys = list(self.transitions.keys())
        to_remove = keys[: max(1, len(keys) // 10)]
        for k in to_remove:
            del self.transitions[k]
            if k in self.last_cluster:
                del self.last_cluster[k]

    def _prune_clusters(self, key):
        clusters = list(self.transitions[key].keys())
        to_remove = clusters[: max(1, len(clusters) // 10)]
        for c in to_remove:
            del self.transitions[key][c]

    def train_model(self):
        """
        Convert transition counts into deterministic rules (most likely next cluster).
        """
        new_rules = {}
        for key, clusters in self.transitions.items():
            key_rules = {}
            for current, next_counts in clusters.items():
                if not next_counts:
                    continue
                # Get the most frequent next cluster
                most_common = next_counts.most_common(1)[0]
                next_cluster, count = most_common

                # Simple threshold: at least 3 occurrences to form a rule
                if count >= 3:
                    key_rules[current] = next_cluster

            if key_rules:
                new_rules[key] = key_rules

        self.rules = new_rules
        logger.info(f"Retrained prediction model. Generated rules for {len(self.rules)} indexes.")

    def get_prediction(self, tenant_id: str, index_name: str, current_cluster_id: int) -> int:
        key = f"{tenant_id}:{index_name}"
        index_rules = self.rules.get(key)
        if index_rules:
            return index_rules.get(current_cluster_id, -1)
        return -1
