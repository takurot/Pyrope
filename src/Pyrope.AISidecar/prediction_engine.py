from collections import defaultdict, Counter
import logging

logger = logging.getLogger(__name__)


class PredictionEngine:
    def __init__(self):
        # transitions[tenant_index][current_cluster] = Counter(next_cluster)
        self.transitions = defaultdict(lambda: defaultdict(Counter))
        self.rules = {}  # cache for serving: { "tenant:index": { current: next } }
        self.last_cluster = {}  # { "tenant:index": last_cluster_id }

    def record_interaction(self, tenant_id: str, index_name: str, cluster_id: int):
        key = f"{tenant_id}:{index_name}"

        last = self.last_cluster.get(key)
        if last is not None and last != cluster_id:
            # Record transition
            self.transitions[key][last][cluster_id] += 1

        self.last_cluster[key] = cluster_id

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
