import logging
import pickle
import os
import numpy as np
from sklearn.linear_model import SGDClassifier

logger = logging.getLogger(__name__)


class ContextualBanditEngine:
    def __init__(self, model_path="models/bandit.pkl", epsilon=0.1):
        self.model_path = model_path
        self.epsilon = epsilon
        self.learner = SGDClassifier(loss="log_loss", penalty="l2", random_state=42)
        self.classes = [0, 1]  # 0 = Normal, 1 = Aggressive
        self.initialized = False
        self._load()

    def _load(self):
        if os.path.exists(self.model_path):
            try:
                with open(self.model_path, "rb") as f:
                    self.learner = pickle.load(f)
                    self.initialized = True
                logger.info("Loaded bandit model")
            except Exception as e:
                logger.error(f"Failed to load bandit model: {e}")

    def save(self):
        try:
            with open(self.model_path, "wb") as f:
                pickle.dump(self.learner, f)
        except Exception as e:
            logger.error(f"Failed to save bandit model: {e}")

    def select_action(self, features: np.ndarray) -> int:
        """
        Selects action (0 or 1).
        features: shape (1, n_features)
        """
        # Exploration
        if np.random.rand() < self.epsilon or not self.initialized:
            return np.random.choice(self.classes)

        # Exploitation
        try:
            return self.learner.predict(features)[0]
        except Exception as e:
            logger.warning(f"Prediction failed, falling back to random: {e}")
            return np.random.choice(self.classes)

    def update(self, features: np.ndarray, action: int, reward: float):
        """
        Updates the model.
        reward: >0 if good, <0 if bad.
        SGDClassifier expects labels. We treat this as:
        If reward is positive -> verify action (train with label=action).
        If reward is negative -> penalize action (train with label!=action)?

        Better approach for reduction to classification:
        Only train if we differ from the counterfactual?
        Actually, standard reduction:
        If Reward=1 (Success), train (X, action).
        If Reward=0 (Fail), train (X, other_action)? Or just ignore?

        Simple heuristic for MVP:
        We assume we receive a binary "Was this good?" signal.
        If Good: train (X, action).
        If Bad: train (X, 1-action).
        """
        label = action if reward > 0 else (1 - action)

        # Incremental learning
        try:
            self.learner.partial_fit(features, [label], classes=self.classes)
            self.initialized = True
        except Exception as e:
            logger.error(f"Partial fit failed: {e}")

    def get_features(self, system_metrics) -> np.ndarray:
        # Match features in train_model.py
        # "qps", "miss_rate", "latency", "cpu"
        qps = float(system_metrics.qps)
        miss_rate = float(system_metrics.miss_rate)
        latency = float(system_metrics.latency_p99_ms)
        cpu = float(system_metrics.cpu_utilization)
        return np.array([[qps, miss_rate, latency, cpu]])
