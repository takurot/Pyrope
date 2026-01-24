using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Pyrope.Policy;
using Pyrope.GarnetServer.Security;

namespace Pyrope.GarnetServer.Controllers
{
    [ApiController]
    [Route("v1/ai")]
    [RequirePermission(Permission.SystemManage)] // Only system admins can manage AI models
    public class AiController : ControllerBase
    {
        private readonly PolicyService.PolicyServiceClient? _policyClient;
        private readonly ILogger<AiController> _logger;

        public AiController(IServiceProvider serviceProvider, ILogger<AiController> logger)
        {
            _policyClient = serviceProvider.GetService<PolicyService.PolicyServiceClient>();
            _logger = logger;
        }

        [HttpGet("models")]
        public async Task<IActionResult> ListModels()
        {
            if (_policyClient == null) return NotFound("AI Sidecar not configured.");

            try
            {
                var response = await _policyClient.ListModelsAsync(new Empty());
                return Ok(response);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to list models");
                return StatusCode(500, new { error = "Failed to communicate with AI Sidecar" });
            }
        }

        [HttpPost("models/train")]
        public async Task<IActionResult> TrainModel([FromBody] TrainRequest request)
        {
            if (_policyClient == null) return NotFound("AI Sidecar not configured.");

            try
            {
                var response = await _policyClient.TrainModelAsync(request ?? new TrainRequest());
                return Ok(response);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to start training");
                return StatusCode(500, new { error = "Failed to start training" });
            }
        }

        [HttpPost("models/deploy")]
        public async Task<IActionResult> DeployModel([FromBody] DeployRequest request)
        {
            if (_policyClient == null) return NotFound("AI Sidecar not configured.");

            try
            {
                var response = await _policyClient.DeployModelAsync(request);
                return Ok(response);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to deploy model");
                return StatusCode(500, new { error = "Failed to deploy model" });
            }
        }

        [HttpPost("models/rollback")]
        public async Task<IActionResult> RollbackModel([FromBody] RollbackRequest request)
        {
            if (_policyClient == null) return NotFound("AI Sidecar not configured.");

            try
            {
                var response = await _policyClient.RollbackModelAsync(request ?? new RollbackRequest());
                return Ok(response);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to rollback model");
                return StatusCode(500, new { error = "Failed to rollback model" });
            }
        }

        [HttpGet("evaluations")]
        public async Task<IActionResult> GetEvaluations()
        {
            if (_policyClient == null) return NotFound("AI Sidecar not configured.");

            try
            {
                var response = await _policyClient.GetEvaluationsAsync(new Empty());
                return Ok(response);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get evaluations");
                return StatusCode(500, new { error = "Failed to communicate with AI Sidecar" });
            }
        }
    }
}
