import json
import time
import os


class QueryLogger:
    def __init__(self, log_path: str):
        self.log_path = log_path
        # Ensure directory exists
        os.makedirs(os.path.dirname(os.path.abspath(self.log_path)), exist_ok=True)

    def log_decision(self, tenant_id: str, query_features: dict, system_metrics: dict, decision: dict):
        entry = {
            "timestamp": time.time(),
            "tenant_id": tenant_id,
            "query_features": query_features,
            "system_metrics": system_metrics,
            "decision": decision
        }
        with open(self.log_path, "a") as f:
            f.write(json.dumps(entry) + "\n")
