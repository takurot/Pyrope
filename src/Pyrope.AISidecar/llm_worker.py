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
    - Queue limit to prevent memory bloat
    - Proper API key validation
    - Configurable model name via env var
    - Robust error handling
    """

    # Default budget limits
    DEFAULT_MAX_REQUESTS_PER_MINUTE = 60
    DEFAULT_MAX_TOKENS_PER_MINUTE = 100000
    DEFAULT_MONTHLY_TOKEN_BUDGET = 10_000_000
    DEFAULT_MAX_RETRIES = 3
    DEFAULT_QUEUE_SIZE = 10  # [Review] Queue limit

    def __init__(
        self,
        api_key=None,
        model_name=None,
        max_requests_per_minute=None,
        max_tokens_per_minute=None,
        monthly_token_budget=None,
        max_retries=None,
    ):
        self.api_key = api_key or os.getenv("GEMINI_API_KEY")
        # [Review] Allow env override or default
        # [Review] Prioritize constructor arg, then env var, then default
        self.model_name = model_name or os.getenv("GEMINI_MODEL_ID", "gemini-2.5-flash-lite")
        
        self.queue: Optional[asyncio.Queue] = None
        self.running = False
        self._worker_task: Optional[asyncio.Task] = None
        self._loop: Optional[asyncio.AbstractEventLoop] = None

        # P6-12: Budgeting configuration
        self.max_requests_per_minute = max_requests_per_minute or self.DEFAULT_MAX_REQUESTS_PER_MINUTE
        self.max_tokens_per_minute = max_tokens_per_minute or self.DEFAULT_MAX_TOKENS_PER_MINUTE
        self.monthly_token_budget = monthly_token_budget or self.DEFAULT_MONTHLY_TOKEN_BUDGET
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
        self._is_disabled = False

        if self.api_key:
            try:
                genai.configure(api_key=self.api_key)
                self.model = genai.GenerativeModel(self.model_name)
                logger.info(f"LLMWorker initialized with model {self.model_name}")
            except Exception as e:
                logger.error(f"Failed to initialize Gemini model: {e}")
                self._is_disabled = True
        else:
            self.model = None
            self._is_disabled = True
            logger.warning("GEMINI_API_KEY not found. LLMWorker is disabled.")

    async def start(self):
        """Starts the background worker loop."""
        self._loop = asyncio.get_running_loop()
        # [Review] Add maxsize to prevent unbounded growth
        self.queue = asyncio.Queue(maxsize=self.DEFAULT_QUEUE_SIZE)
        self.running = True
        self._worker_task = asyncio.create_task(self._process_queue())
        logger.info("LLMWorker started.")

    async def stop(self):
        """Stops the background worker loop cleanly."""
        self.running = False

        if self.queue:
            try:
                # Use nowait to avoid blocking on full queue during shutdown
                self.queue.put_nowait((_SHUTDOWN_SENTINEL, None, 0))
            except asyncio.QueueFull:
                pass

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

        while self._request_timestamps and self._request_timestamps[0] < window_start:
            self._request_timestamps.popleft()
        while self._token_window and self._token_window[0][0] < window_start:
            self._token_window.popleft()

        if len(self._request_timestamps) >= self.max_requests_per_minute:
            return True

        tokens_in_window = sum(t[1] for t in self._token_window)
        if tokens_in_window >= self.max_tokens_per_minute:
            return True

        return False

    def is_over_budget(self) -> bool:
        return self.stats["monthly_tokens_used"] >= self.monthly_token_budget

    def get_stats(self) -> dict:
        return dict(self.stats)

    async def submit_task(self, prompt, callback=None, priority=0) -> bool:
        """Submits a prompt to the queue."""
        if not self.queue:
            logger.error("LLMWorker: Not started. Call start() first.")
            return False
            
        if self._is_disabled:
             logger.warning("LLMWorker is disabled (missing API key or init failure). Rejecting task.")
             if callback:
                 await callback(None)
             return False

        if self.is_over_budget():
            self.stats["requests_budget_exceeded"] += 1
            logger.warning("LLMWorker: Monthly budget exceeded. Task rejected.")
            if callback:
                await callback(None)
            return False

        try:
            # [Review] Don't block if queue is full, just reject (Fail fast strategy)
            self.queue.put_nowait((prompt, callback, 0))
            return True
        except asyncio.QueueFull:
            logger.warning("LLMWorker: Queue full. Rejecting task.")
            return False

    async def _process_queue(self):
        while self.running:
            try:
                try:
                    item = await asyncio.wait_for(self.queue.get(), timeout=1.0)
                except asyncio.TimeoutError:
                    continue

                if item[0] is _SHUTDOWN_SENTINEL:
                    self.queue.task_done()
                    break

                prompt, callback, retry_count = item

                if self._is_disabled or not self.model:
                    self.stats["requests_failed"] += 1
                    self.queue.task_done()
                    if callback: await callback(None) # Callback right away
                    continue

                if self.is_rate_limited():
                    self.stats["requests_rate_limited"] += 1
                    if retry_count >= self.max_retries:
                        logger.warning(f"LLMWorker: Max retries ({self.max_retries}) exceeded. Dropping task.")
                        self.stats["requests_dropped_max_retry"] += 1
                        if callback:
                            await callback(None)
                        self.queue.task_done()
                        continue

                    logger.warning(f"LLMWorker: Rate limited. Retry {retry_count + 1}/{self.max_retries}")
                    await asyncio.sleep(1)
                    try:
                        self.queue.put_nowait((prompt, callback, retry_count + 1))
                    except asyncio.QueueFull:
                        if callback: await callback(None)
                    
                    self.queue.task_done()
                    continue

                if self.is_over_budget():
                    self.stats["requests_budget_exceeded"] += 1
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

                    input_tokens = len(prompt.split()) * 1.3 
                    output_tokens = len(text.split()) * 1.3 if text else 0
                    total_tokens = int(input_tokens + output_tokens)

                    now = time.time()
                    self._request_timestamps.append(now)
                    self._token_window.append((now, total_tokens))

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
