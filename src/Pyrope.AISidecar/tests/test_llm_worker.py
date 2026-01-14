import unittest
from unittest.mock import MagicMock, patch, AsyncMock
import asyncio
import os
from llm_worker import LLMWorker

class TestLLMWorker(unittest.TestCase):

    def setUp(self):
        # Prevent real API key reading or configuration
        self.patcher = patch.dict(os.environ, {"GEMINI_API_KEY": "fake_key"})
        self.patcher.start()
        
        # Mock genai.configure and GenerativeModel
        self.genai_patcher = patch('llm_worker.genai')
        self.mock_genai = self.genai_patcher.start()
        
        self.mock_model = MagicMock()
        self.mock_genai.GenerativeModel.return_value = self.mock_model
        
        # Setup AsyncMock for generate_content_async
        self.mock_response = MagicMock()
        self.mock_response.text = "Mocked Response"
        self.mock_model.generate_content_async = AsyncMock(return_value=self.mock_response)

    def tearDown(self):
        self.patcher.stop()
        self.genai_patcher.stop()

    def test_init(self):
        worker = LLMWorker(api_key="test_key")
        self.mock_genai.configure.assert_called_with(api_key="test_key")
        self.assertIsNotNone(worker.model)

    def test_init_no_key(self):
        with patch.dict(os.environ, {}, clear=True):
            worker = LLMWorker(api_key=None)
            self.assertIsNone(worker.model)

    async def async_test_process_task(self):
        worker = LLMWorker(api_key="test_key")
        await worker.start()
        
        callback_result = []
        async def callback(text):
            callback_result.append(text)
            
        await worker.submit_task("Hello", callback)
        
        # Give it a moment to process
        await asyncio.sleep(0.1)
        
        await worker.stop()
        
        self.mock_model.generate_content_async.assert_called_with("Hello")
        self.assertEqual(callback_result, ["Mocked Response"])
        self.assertEqual(worker.stats["requests_total"], 1)

    def test_process_task(self):
        asyncio.run(self.async_test_process_task())

if __name__ == '__main__':
    unittest.main()
