using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Pyrope.Policy;
using Pyrope.GarnetServer.Model;
using Pyrope.GarnetServer.Security;
using Pyrope.GarnetServer.Services;

namespace Pyrope.GarnetServer.Controllers
{
    [ApiController]
    [Route("v1/ai")]
    [RequirePermission(Permission.SystemManage)] // Only system admins can manage AI models
    public class AiController : ControllerBase
    {
        private readonly PolicyService.PolicyServiceClient? _policyClient;
        private readonly ILogger<AiController> _logger;
        private readonly IAuditLogger _auditLogger;

        public AiController(IServiceProvider serviceProvider, ILogger<AiController> logger, IAuditLogger auditLogger)
        {
            _policyClient = serviceProvider.GetService<PolicyService.PolicyServiceClient>();
            _logger = logger;
            _auditLogger = auditLogger;
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
        public async Task<IActionResult> TrainModel([FromBody] TrainRequest? request)
        {
            if (_policyClient == null) return NotFound("AI Sidecar not configured.");

            try
            {
                var payload = request ?? new TrainRequest();
                var response = await _policyClient.TrainModelAsync(payload);

                _auditLogger.Log(new AuditEvent(
                    action: AuditActions.TrainModel,
                    resourceType: AuditResourceTypes.Model,
                    userId: GetCurrentUserId(),
                    resourceId: response.JobId,
                    details: JsonSerializer.Serialize(new { payload.DatasetPath }),
                    ipAddress: GetClientIp(),
                    success: true
                ));

                return Ok(response);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to start training");

                _auditLogger.Log(new AuditEvent(
                    action: AuditActions.TrainModel,
                    resourceType: AuditResourceTypes.Model,
                    userId: GetCurrentUserId(),
                    details: JsonSerializer.Serialize(new { request?.DatasetPath }),
                    ipAddress: GetClientIp(),
                    success: false
                ));

                return StatusCode(500, new { error = "Failed to start training" });
            }
        }

        [HttpPost("models/deploy")]
        public async Task<IActionResult> DeployModel([FromBody] DeployRequest? request)
        {
            if (_policyClient == null) return NotFound("AI Sidecar not configured.");

            if (request == null || string.IsNullOrWhiteSpace(request.Version))
            {
                return BadRequest("version is required.");
            }

            try
            {
                var response = await _policyClient.DeployModelAsync(request);
                if (response.Status.StartsWith("Error", StringComparison.OrdinalIgnoreCase))
                {
                    return BadRequest(new { error = response.Status });
                }

                _auditLogger.Log(new AuditEvent(
                    action: AuditActions.DeployModel,
                    resourceType: AuditResourceTypes.Model,
                    userId: GetCurrentUserId(),
                    resourceId: request.Version,
                    details: JsonSerializer.Serialize(new
                    {
                        request.Canary,
                        CanaryTenants = request.CanaryTenants.ToArray()
                    }),
                    ipAddress: GetClientIp(),
                    success: true
                ));

                return Ok(response);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to deploy model");

                _auditLogger.Log(new AuditEvent(
                    action: AuditActions.DeployModel,
                    resourceType: AuditResourceTypes.Model,
                    userId: GetCurrentUserId(),
                    resourceId: request.Version,
                    details: JsonSerializer.Serialize(new
                    {
                        request.Canary,
                        CanaryTenants = request.CanaryTenants.ToArray()
                    }),
                    ipAddress: GetClientIp(),
                    success: false
                ));

                return StatusCode(500, new { error = "Failed to deploy model" });
            }
        }

        [HttpPost("models/rollback")]
        public async Task<IActionResult> RollbackModel([FromBody] RollbackRequest? request)
        {
            if (_policyClient == null) return NotFound("AI Sidecar not configured.");

            var payload = request ?? new RollbackRequest();

            try
            {
                var response = await _policyClient.RollbackModelAsync(payload);

                _auditLogger.Log(new AuditEvent(
                    action: AuditActions.RollbackModel,
                    resourceType: AuditResourceTypes.Model,
                    userId: GetCurrentUserId(),
                    resourceId: response.ActiveVersion,
                    details: JsonSerializer.Serialize(new { payload.CanaryOnly }),
                    ipAddress: GetClientIp(),
                    success: true
                ));

                return Ok(response);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to rollback model");

                _auditLogger.Log(new AuditEvent(
                    action: AuditActions.RollbackModel,
                    resourceType: AuditResourceTypes.Model,
                    userId: GetCurrentUserId(),
                    details: JsonSerializer.Serialize(new { payload.CanaryOnly }),
                    ipAddress: GetClientIp(),
                    success: false
                ));

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

        private string? GetCurrentUserId() => HttpContext?.Items["PyropeUserId"]?.ToString();

        private string? GetClientIp() => HttpContext?.Connection?.RemoteIpAddress?.ToString();
    }
}
