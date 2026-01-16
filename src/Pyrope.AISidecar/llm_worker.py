import os
import asyncio
import logging
import google.generativeai as genai
from collections import deque
import time
from typing import Optional

logger = logging.getLogger(__name__)

# Sentinel to signal shutdown
_SHUTDOWN_SENTINEL = object()


class LLMWorker:
    """
    P6-8 + P6-12: LLM Worker with Gemini integration and budgeting.

    Fixes from Codex review:
    - Queue created in start() to bind to correct event loop
    - Proper shutdown with sentinel and task cancellation
    - Max retry count for rate-limited requests
    """

    # Default budget limits
    DEFAULT_MAX_REQUESTS_PER_MINUTE = 60
    DEFAULT_MAX_TOKENS_PER_MINUTE = 100000
    DEFAULT_MONTHLY_TOKEN_BUDGET = 10_000_000
    DEFAULT_MAX_RETRIES = 3

    def __init__(
        self,
        api_key=None,
        model_name="gemini-1.5-flash",
        max_requests_per_minute=None,
        max_tokens_per_minute=None,
        monthly_token_budget=None,
        max_retries=None,
    ):
        self.api_key = api_key or os.getenv("GEMINI_API_KEY")
        self.model_name = model_name
        # FIX: Queue created in start() to bind to correct event loop
        self.queue: Optional[asyncio.Queue] = None
        self.running = False
        self._worker_task: Optional[asyncio.Task] = None
        self._loop: Optional[asyncio.AbstractEventLoop] = None

        # P6-12: Budgeting configuration
        self.max_requests_per_minute = max_requests_per_minute or self.DEFAULT_MAX_REQUESTS_PER_MINUTE
        self.max_tokens_per_minute = max_tokens_per_minute or self.DEFAULT_MAX_TOKENS_PER_MINUTE
        self.monthly_token_budget = monthly_token_budget or self.DEFAULT_MONTHLY_TOKEN_BUDGET
        # FIX: Max retries for rate-limited requests
        self.max_retries = max_retries or self.DEFAULT_MAX_RETRIES

        # P6-12: Rate limiting state
        self._request_timestamps = deque(maxlen=1000)
        self._token_window = deque(maxlen=1000)

        # P6-12: Stats and metering
        self.stats = {
            "requests_total": 0,
            "requests_succeeded": 0,
            "requests_failed": 0,
            "requests_rate_limited": 0,
            "requests_budget_exceeded": 0,
            "requests_dropped_max_retry": 0,
            "tokens_total": 0,
            "tokens_input": 0,
            "tokens_output": 0,
            "monthly_tokens_used": 0,
            "errors_total": 0,
            "last_request_latency": 0,
            "avg_latency": 0,
        }
        self._latencies = deque(maxlen=100)

        if self.api_key:
            genai.configure(api_key=self.api_key)
            self.model = genai.GenerativeModel(self.model_name)
            logger.info(f"LLMWorker initialized with model {self.model_name}")
        else:
            self.model = None
            logger.warning("GEMINI_API_KEY not found. LLMWorker will fail requests.")

    async def start(self):
        """Starts the background worker loop."""
        # FIX: Create queue in the running event loop
        self._loop = asyncio.get_running_loop()
        self.queue = asyncio.Queue()
        self.running = True
        self._worker_task = asyncio.create_task(self._process_queue())
        logger.info("LLMWorker started.")

    async def stop(self):
        """Stops the background worker loop cleanly."""
        self.running = False

        # FIX: Push sentinel to unblock queue.get()
        if self.queue:
            await self.queue.put((_SHUTDOWN_SENTINEL, None, 0))

        # FIX: Cancel and wait for worker task
        if self._worker_task:
            self._worker_task.cancel()
            try:
                await self._worker_task
            except asyncio.CancelledError:
                pass

        logger.info("LLMWorker stopped.")

    def is_rate_limited(self) -> bool:
        """P6-12: Check if we're hitting rate limits."""
        now = time.time()
        window_start = now - 60

        # Clean old entries
        while self._request_timestamps and self._request_timestamps[0] < window_start:
            self._request_timestamps.popleft()
        while self._token_window and self._token_window[0][0] < window_start:
            self._token_window.popleft()

        # Check request rate
        if len(self._request_timestamps) >= self.max_requests_per_minute:
            return True

        # Check token rate
        tokens_in_window = sum(t[1] for t in self._token_window)
        if tokens_in_window >= self.max_tokens_per_minute:
            return True

        return False

    def is_over_budget(self) -> bool:
        """P6-12: Check if monthly budget is exceeded."""
        return self.stats["monthly_tokens_used"] >= self.monthly_token_budget

    def get_stats(self) -> dict:
        """P6-12: Get stats for Prometheus metrics."""
        return dict(self.stats)

    async def submit_task(self, prompt, callback=None, priority=0) -> bool:
        """Submits a prompt to the queue.
        callback: async function(response_text)
        priority: 0=normal, 1=high (not implemented yet)
        Returns: True if queued, False if rejected
        """
        if not self.queue:
            logger.error("LLMWorker: Not started. Call start() first.")
            return False

        if self.is_over_budget():
            self.stats["requests_budget_exceeded"] += 1
            logger.warning("LLMWorker: Monthly budget exceeded. Task rejected.")
            if callback:
                await callback(None)
            return False

        # FIX: Include retry count in queue item
        await self.queue.put((prompt, callback, 0))  # 0 = initial retry count
        return True

    async def _process_queue(self):
        while self.running:
            try:
                # Wait for a task with timeout to allow checking running flag
                try:
                    item = await asyncio.wait_for(self.queue.get(), timeout=1.0)
                except asyncio.TimeoutError:
                    continue

                # FIX: Check for shutdown sentinel
                if item[0] is _SHUTDOWN_SENTINEL:
                    self.queue.task_done()
                    break

                prompt, callback, retry_count = item

                if not self.model:
                    logger.error("LLMWorker: Model not initialized (missing API key). Skipping task.")
                    self.stats["requests_failed"] += 1
                    self.queue.task_done()
                    continue

                # P6-12: Rate limiting check with retry cap
                if self.is_rate_limited():
                    self.stats["requests_rate_limited"] += 1

                    # FIX: Check max retries
                    if retry_count >= self.max_retries:
                        logger.warning(f"LLMWorker: Max retries ({self.max_retries}) exceeded. Dropping task.")
                        self.stats["requests_dropped_max_retry"] += 1
                        if callback:
                            await callback(None)
                        self.queue.task_done()
                        continue

                    logger.warning(f"LLMWorker: Rate limited. Retry {retry_count + 1}/{self.max_retries}")
                    await asyncio.sleep(1)
                    # Re-queue with incremented retry count
                    await self.queue.put((prompt, callback, retry_count + 1))
                    self.queue.task_done()
                    continue

                # P6-12: Budget check
                if self.is_over_budget():
                    self.stats["requests_budget_exceeded"] += 1
                    logger.warning("LLMWorker: Budget exceeded. Skipping task.")
                    if callback:
                        await callback(None)
                    self.queue.task_done()
                    continue

                start_time = time.time()
                try:
                    # Execute LLM call
                    response = await self.model.generate_content_async(prompt)

                    text = response.text
                    latency = time.time() - start_time

                    # P6-12: Token metering (estimate if not available)
                    input_tokens = len(prompt.split()) * 1.3  # rough estimate
                    output_tokens = len(text.split()) * 1.3 if text else 0
                    total_tokens = int(input_tokens + output_tokens)

                    # Update rate limiting state
                    now = time.time()
                    self._request_timestamps.append(now)
                    self._token_window.append((now, total_tokens))

                    # Update stats
                    self.stats["requests_total"] += 1
                    self.stats["requests_succeeded"] += 1
                    self.stats["tokens_total"] += total_tokens
                    self.stats["tokens_input"] += int(input_tokens)
                    self.stats["tokens_output"] += int(output_tokens)
                    self.stats["monthly_tokens_used"] += total_tokens
                    self.stats["last_request_latency"] = latency

                    self._latencies.append(latency)
                    self.stats["avg_latency"] = sum(self._latencies) / len(self._latencies)

                    if callback:
                        await callback(text)

                except Exception as e:
                    logger.error(f"LLMWorker Error: {e}")
                    self.stats["requests_total"] += 1
                    self.stats["requests_failed"] += 1
                    self.stats["errors_total"] += 1
                    if callback:
                        await callback(None)
                finally:
                    self.queue.task_done()

            except asyncio.CancelledError:
                break
            except Exception as e:
                logger.error(f"LLMWorker Loop Error: {e}")
                await asyncio.sleep(1)
