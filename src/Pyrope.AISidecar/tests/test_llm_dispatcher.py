import unittest
from unittest.mock import AsyncMock, MagicMock, patch
import asyncio
import json
import os

from llm_dispatcher import LLMPrefetchDispatcher, LLMTTLAdvisor


class TestLLMPrefetchDispatcher(unittest.TestCase):

    async def async_test_dispatch_valid(self):
        callback = AsyncMock()
        dispatcher = LLMPrefetchDispatcher(prefetch_callback=callback)
        
        response = json.dumps({"prediction": "shoes", "confidence": 0.9, "cluster_id": 42})
        await dispatcher.dispatch_prefetch_prediction("tenant1", "idx1", response)
        
        callback.assert_called_once_with("tenant1", "idx1", 42)
        self.assertEqual(dispatcher.stats["dispatched_total"], 1)

    def test_dispatch_valid(self):
        asyncio.run(self.async_test_dispatch_valid())

    async def async_test_dispatch_low_confidence(self):
        callback = AsyncMock()
        dispatcher = LLMPrefetchDispatcher(prefetch_callback=callback)
        
        response = json.dumps({"prediction": "shoes", "confidence": 0.3, "cluster_id": 42})
        await dispatcher.dispatch_prefetch_prediction("tenant1", "idx1", response)
        
        callback.assert_not_called()

    def test_dispatch_low_confidence(self):
        asyncio.run(self.async_test_dispatch_low_confidence())


class TestLLMTTLAdvisor(unittest.TestCase):

    async def async_test_apply_shorten(self):
        callback = AsyncMock()
        advisor = LLMTTLAdvisor(policy_update_callback=callback)
        
        response = json.dumps({"action": "shorten", "ttl_seconds": 10, "cluster_id": 5})
        await advisor.apply_ttl_advice("tenant1", "idx1", response)
        
        callback.assert_called_once_with("tenant1", "idx1", 5, 10)
        self.assertEqual(advisor.stats["overrides_applied"], 1)
        self.assertEqual(advisor.get_ttl_override("tenant1", "idx1", 5), 10)

    def test_apply_shorten(self):
        asyncio.run(self.async_test_apply_shorten())

    async def async_test_apply_evict(self):
        advisor = LLMTTLAdvisor()
        
        response = json.dumps({"action": "evict", "cluster_id": 99})
        await advisor.apply_ttl_advice("tenant1", "idx1", response)
        
        self.assertEqual(advisor.get_ttl_override("tenant1", "idx1", 99), 0)

    def test_apply_evict(self):
        asyncio.run(self.async_test_apply_evict())


if __name__ == '__main__':
    unittest.main()
