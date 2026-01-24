import argparse
import json
import logging
import os
from typing import List, Dict, Any

import numpy as np
import pandas as pd
import onnx
from sklearn.ensemble import GradientBoostingClassifier
from sklearn.model_selection import train_test_split
from sklearn.metrics import classification_report, confusion_matrix
from skl2onnx import to_onnx
from skl2onnx.common.data_types import FloatTensorType

logging.basicConfig(level=logging.INFO, format="%(asctime)s - %(levelname)s - %(message)s")
logger = logging.getLogger(__name__)

def load_logs(log_path: str) -> List[Dict[str, Any]]:
    """Loads query logs from a JSONL file."""
    logs = []
    if not os.path.exists(log_path):
        logger.warning(f"Log file not found: {log_path}")
        return logs

    with open(log_path, "r") as f:
        for line in f:
            try:
                logs.append(json.loads(line))
            except json.JSONDecodeError:
                continue
    return logs

def extract_features_and_labels(logs: List[Dict[str, Any]]) -> pd.DataFrame:
    """
    Extracts features and generates labels based on heuristics.
    
    Heuristic Labeling:
    - Label 1 (Aggressive): miss_rate > 0.3 OR cpu > 80% OR p99 > 50ms
    - Label 0 (Default): Otherwise
    """
    data = []
    for entry in logs:
        sys = entry.get("system_metrics", {})
        
        # Features
        qps = float(sys.get("qps", 0.0))
        miss_rate = float(sys.get("miss_rate", 0.0))
        latency = float(sys.get("latency_p99_ms", 0.0))
        cpu = float(sys.get("cpu_utilization", 0.0))
        
        # Heuristic Label Generation
        # If system is under stress or missing cache often, we want Aggressive policy
        if miss_rate > 0.3 or cpu > 80.0 or latency > 50.0:
            label = 1 # Aggressive
        else:
            label = 0 # Default

        data.append({
            "qps": qps,
            "miss_rate": miss_rate,
            "latency": latency,
            "cpu": cpu,
            "label": label
        })
    
    return pd.DataFrame(data)

def train_and_export(data: pd.DataFrame, output_onnx: str):
    if data.empty:
        logger.error("No data to train on.")
        return

    X = data[["qps", "miss_rate", "latency", "cpu"]]
    y = data["label"]

    logger.info(f"Dataset size: {len(X)}")
    logger.info(f"Label distribution:\n{y.value_counts()}")

    if y.nunique() < 2:
        logger.warning("Data contains only one class. Skipping training to avoid errors.")
        return

    # Train/Test Split
    X_train, X_test, y_train, y_test = train_test_split(X, y, test_size=0.2, random_state=42, stratify=y)

    # Train GBDT
    logger.info("Training GBDT model...")
    clf = GradientBoostingClassifier(n_estimators=100, learning_rate=0.1, max_depth=3, random_state=42)
    clf.fit(X_train, y_train)

    # Evaluation
    y_pred = clf.predict(X_test)
    logger.info("Model Evaluation:")
    logger.info("\n" + classification_report(y_test, y_pred))
    
    # Export to ONNX
    logger.info(f"Exporting to ONNX: {output_onnx}")
    initial_type = [('float_input', FloatTensorType([None, 4]))]
    onx = to_onnx(clf, X_train, initial_types=initial_type, target_opset=12)
    
    with open(output_onnx, "wb") as f:
        f.write(onx.SerializeToString())
    
    # Verify ONNX model
    try:
        onnx.checker.check_model(onx)
        logger.info("ONNX model structure verification passed.")
        
        # Runtime Verification (if onnxruntime is available)
        try:
            import onnxruntime as ort
            sess = ort.InferenceSession(output_onnx)
            input_name = sess.get_inputs()[0].name
            # Test with a dummy input
            dummy_input = X_test.iloc[:1].to_numpy().astype(np.float32)
            res = sess.run(None, {input_name: dummy_input})
            logger.info(f"ONNX runtime inference verification passed. Output shape: {res[0].shape}")
        except ImportError:
            logger.warning("onnxruntime not installed. Skipping runtime inference verification.")
        except Exception as e:
            logger.error(f"ONNX runtime inference failed: {e}")

    except Exception as e:
        logger.error(f"ONNX model verification failed: {e}")

    logger.info("Export complete.")

def main():
    parser = argparse.ArgumentParser(description="Train AI Sidecar Policy Model")
    parser.add_argument("--log-path", type=str, default="logs/query_log.jsonl", help="Path to query log JSONL")
    parser.add_argument("--output", type=str, default="policy_model.onnx", help="Output ONNX file path")
    args = parser.parse_args()

    logger.info(f"Loading logs from {args.log_path}...")
    logs = load_logs(args.log_path)
    
    logger.info("Extracting features...")
    df = extract_features_and_labels(logs)
    
    train_and_export(df, args.output)

if __name__ == "__main__":
    main()
