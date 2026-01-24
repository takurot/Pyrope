import argparse
import logging
import sys
import pandas as pd
import numpy as np
from sklearn.model_selection import train_test_split
from sklearn.ensemble import GradientBoostingClassifier
from sklearn.metrics import classification_report

# Import from train_model
from train_model import load_logs, extract_features_and_labels

logging.basicConfig(level=logging.INFO, format="%(asctime)s - %(levelname)s - %(message)s")
logger = logging.getLogger(__name__)

def evaluate_simulation(data: pd.DataFrame, model):
    """
    Simulates the effect of the AI model on the system.
    Compares Baseline (Always Default) vs AI Model.
    """
    X = data[["qps", "miss_rate", "latency", "cpu"]]
    y_true = data["label"]
    
    # AI Predictions
    y_pred = model.predict(X)
    data["ai_decision"] = y_pred

    # Metrics
    total_events = len(data)
    high_load_events = y_true.sum()
    ai_interventions = y_pred.sum()
    
    # Correct Interventions (True Positives)
    correct_interventions = ((y_pred == 1) & (y_true == 1)).sum()
    
    # Missed High Load (False Negatives)
    missed_load = ((y_pred == 0) & (y_true == 1)).sum()
    
    # Unnecessary Interventions (False Positives)
    false_alarms = ((y_pred == 1) & (y_true == 0)).sum()

    print("\n" + "="*40)
    print("       AI MODEL EVALUATION REPORT       ")
    print("="*40)
    print(f"Total Events Evaluated:      {total_events}")
    print(f"High Load Events Detected:   {high_load_events} ({(high_load_events/total_events)*100:.1f}%)")
    print(f"AI Interventions Triggered:  {ai_interventions} ({(ai_interventions/total_events)*100:.1f}%)")
    print("-" * 40)
    print(f"Correct Interventions (TP):  {correct_interventions}")
    print(f"Missed High Load (FN):       {missed_load}")
    print(f"False Alarms (FP):           {false_alarms}")
    print("-" * 40)
    
    # Simulation: Expected Impact
    # Assumption: Aggressive policy reduces P99 by 50% during high load
    # Baseline P99 sum
    baseline_p99_sum = data["latency"].sum()
    
    # AI P99 sum: If High Load AND AI Intervenes, latency is halved (simulated benefit)
    # If High Load AND AI Misses, latency stays high
    # If Normal Load AND AI Intervenes, latency stays same (maybe slight overhead ignored)
    
    def simulate_latency(row):
        if row["label"] == 1 and row["ai_decision"] == 1:
            return row["latency"] * 0.5 # Benefit
        return row["latency"]

    ai_p99_cum = data.apply(simulate_latency, axis=1).sum()
    p99_improvement = ((baseline_p99_sum - ai_p99_cum) / baseline_p99_sum) * 100 if baseline_p99_sum > 0 else 0

    print(f"Baseline Cumulative P99:     {baseline_p99_sum:.2f} ms")
    print(f"AI Simulated Cumulative P99: {ai_p99_cum:.2f} ms")
    print(f"Estimated P99 Improvement:   {p99_improvement:.2f}%")
    print("="*40 + "\n")

def main():
    parser = argparse.ArgumentParser(description="Evaluate AI Sidecar Policy Model")
    parser.add_argument("--log-path", type=str, default="logs/query_log.jsonl", help="Path to query log JSONL")
    args = parser.parse_args()

    logger.info(f"Loading logs from {args.log_path}...")
    logs = load_logs(args.log_path)
    
    if not logs:
        logger.error("No logs found.")
        sys.exit(1)

    logger.info("Extracting features...")
    df = extract_features_and_labels(logs)
    
    # Train a model on the fly for evaluation (in prod we would load the pickle)
    # For now, we split and train/test on the same run
    X = df[["qps", "miss_rate", "latency", "cpu"]]
    y = df["label"]

    X_train, X_test, y_train, y_test = train_test_split(X, y, test_size=0.2, random_state=42)

    logger.info("Training reference model for evaluation...")
    clf = GradientBoostingClassifier(n_estimators=100, learning_rate=0.1, max_depth=3, random_state=42)
    clf.fit(X_train, y_train)

    logger.info("Running evaluation simulation on TEST set...")
    test_df = df.iloc[y_test.index].copy()
    evaluate_simulation(test_df, clf)

if __name__ == "__main__":
    main()
