using Microsoft.AspNetCore.Mvc;
using Pyrope.GarnetServer.Security;
using Pyrope.GarnetServer.Extensions;
using Pyrope.GarnetServer.Model;
using Pyrope.GarnetServer.Services;
using Pyrope.GarnetServer.Utils;
using Pyrope.GarnetServer.Vector;
using System.Text.Json;

namespace Pyrope.GarnetServer.Controllers
{
    [ApiController]
    [Route("v1/indexes")]
    public class IndexController : ControllerBase
    {
        private readonly VectorIndexRegistry _registry;
        private readonly IAuditLogger _auditLogger;
        private readonly IBillingMeter _billingMeter;

        public IndexController(VectorIndexRegistry registry, IAuditLogger auditLogger, IBillingMeter billingMeter)
        {
            _registry = registry;
            _auditLogger = auditLogger;
            _billingMeter = billingMeter;
        }

        [HttpPost]
        [RequirePermission(Permission.IndexCreate)]
        public IActionResult CreateIndex([FromBody] CreateIndexRequest request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.TenantId) || string.IsNullOrWhiteSpace(request.IndexName))
                return BadRequest("Invalid request.");

            if (!TenantNamespace.TryValidateTenantId(request.TenantId, out var tenantError))
            {
                return BadRequest(tenantError);
            }

            if (!TenantNamespace.TryValidateIndexName(request.IndexName, out var indexError))
            {
                return BadRequest(indexError);
            }

            try
            {
                var metric = Enum.Parse<VectorMetric>(request.Metric, true);

                var config = new IndexConfig
                {
                    Dimension = request.Dimension,
                    Metric = request.Metric,
                    Algorithm = request.Algorithm,
                    Parameters = request.Parameters
                };

                _registry.GetOrCreate(request.TenantId, request.IndexName, request.Dimension, metric, config);

                // Audit log
                _auditLogger.Log(new AuditEvent(
                    action: AuditActions.CreateIndex,
                    resourceType: AuditResourceTypes.Index,
                    tenantId: request.TenantId,
                    userId: GetCurrentUserId(),
                    resourceId: request.IndexName,
                    details: JsonSerializer.Serialize(new { request.Dimension, request.Metric, request.Algorithm, request.Parameters }),
                    ipAddress: GetClientIp(),
                    success: true
                ));

                return Created($"/v1/indexes/{request.TenantId}/{request.IndexName}", new { Message = "Index created." });
            }
            catch (Exception ex)
            {
                return BadRequest(ex.Message);
            }
        }

        [HttpPost("{tenantId}/{indexName}/build")]
        [RequirePermission(Permission.IndexBuild)]
        public IActionResult BuildIndex(string tenantId, string indexName)
        {
            if (!TenantNamespace.TryValidateTenantId(tenantId, out var tenantError))
            {
                return BadRequest(tenantError);
            }

            if (!TenantNamespace.TryValidateIndexName(indexName, out var indexError))
            {
                return BadRequest(indexError);
            }

            if (_registry.TryGetIndex(tenantId, indexName, out var index))
            {
                index.Build();

                // Audit log
                _auditLogger.Log(new AuditEvent(
                    action: AuditActions.BuildIndex,
                    resourceType: AuditResourceTypes.Index,
                    tenantId: tenantId,
                    userId: GetCurrentUserId(),
                    resourceId: indexName,
                    ipAddress: GetClientIp(),
                    success: true
                ));

                return Ok(new { Message = "Index build triggered." });
            }
            return NotFound("Index not found.");
        }

        [HttpPost("{tenantId}/{indexName}/snapshot")]
        [RequirePermission(Permission.IndexSnapshot)]
        public IActionResult SnapshotIndex(string tenantId, string indexName, [FromBody] SnapshotRequest request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.Path))
            {
                return BadRequest("Path is required.");
            }

            if (!IsSafePath(request.Path))
            {
                return BadRequest("Invalid or unsafe path.");
            }

            if (!TenantNamespace.TryValidateTenantId(tenantId, out var tenantError))
            {
                return BadRequest(tenantError);
            }

            if (!TenantNamespace.TryValidateIndexName(indexName, out var indexError))
            {
                return BadRequest(indexError);
            }

            if (_registry.TryGetIndex(tenantId, indexName, out var index))
            {
                try
                {
                    index.Snapshot(request.Path);
                    _billingMeter.RecordSnapshot(tenantId, indexName, request.Path);

                    // Audit log
                    _auditLogger.Log(new AuditEvent(
                        action: AuditActions.SnapshotIndex,
                        resourceType: AuditResourceTypes.Index,
                        tenantId: tenantId,
                        userId: GetCurrentUserId(),
                        resourceId: indexName,
                        details: JsonSerializer.Serialize(new { request.Path }),
                        ipAddress: GetClientIp(),
                        success: true
                    ));

                    return Ok(new { Message = "Snapshot created." });
                }
                catch (Exception ex)
                {
                    return StatusCode(500, ex.Message);
                }
            }
            return NotFound("Index not found.");
        }

        [HttpPost("{tenantId}/{indexName}/load")]
        [RequirePermission(Permission.IndexLoad)]
        public IActionResult LoadIndex(string tenantId, string indexName, [FromBody] SnapshotRequest request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.Path))
            {
                return BadRequest("Path is required.");
            }

            if (!IsSafePath(request.Path))
            {
                return BadRequest("Invalid or unsafe path.");
            }

            if (!TenantNamespace.TryValidateTenantId(tenantId, out var tenantError))
            {
                return BadRequest(tenantError);
            }

            if (!TenantNamespace.TryValidateIndexName(indexName, out var indexError))
            {
                return BadRequest(indexError);
            }

            if (_registry.TryGetIndex(tenantId, indexName, out var index))
            {
                try
                {
                    index.Load(request.Path);

                    // Audit log
                    _auditLogger.Log(new AuditEvent(
                        action: AuditActions.LoadIndex,
                        resourceType: AuditResourceTypes.Index,
                        tenantId: tenantId,
                        userId: GetCurrentUserId(),
                        resourceId: indexName,
                        details: JsonSerializer.Serialize(new { request.Path }),
                        ipAddress: GetClientIp(),
                        success: true
                    ));

                    return Ok(new { Message = "Index loaded from snapshot." });
                }
                catch (Exception ex)
                {
                    return StatusCode(500, ex.Message);
                }
            }
            return NotFound("Index not found. Please create it first.");
        }

        [HttpGet("{tenantId}/{indexName}/stats")]
        [RequirePermission(Permission.IndexRead)]
        public IActionResult GetStats(string tenantId, string indexName)
        {
            if (!TenantNamespace.TryValidateTenantId(tenantId, out var tenantError))
            {
                return BadRequest(tenantError);
            }

            if (!TenantNamespace.TryValidateIndexName(indexName, out var indexError))
            {
                return BadRequest(indexError);
            }

            if (_registry.TryGetIndex(tenantId, indexName, out var index))
            {
                return Ok(index.GetStats());
            }
            return NotFound("Index not found.");
        }

        [HttpPost("{tenantId}/{indexName}/centroids")]
        [RequirePermission(Permission.IndexBuild)] // Re-using IndexBuild permission for now
        public IActionResult UpdateCentroids(string tenantId, string indexName, [FromBody] UpdateCentroidsRequest request, [FromServices] SemanticClusterRegistry clusterRegistry)
        {
            if (request == null || request.Centroids == null || request.Centroids.Count == 0)
                return BadRequest("Centroids are required.");

            if (!TenantNamespace.TryValidateTenantId(tenantId, out var tenantError)) return BadRequest(tenantError);
            if (!TenantNamespace.TryValidateIndexName(indexName, out var indexError)) return BadRequest(indexError);

            if (!_registry.TryGetIndex(tenantId, indexName, out var index))
                return NotFound("Index not found.");

            if (request.Dimension != index.Dimension)
            {
                return BadRequest($"Dimension mismatch. Index: {index.Dimension}, Request: {request.Dimension}");
            }

            // Validate centroid dimensions
            foreach (var c in request.Centroids)
            {
                if (c.Length != index.Dimension)
                    return BadRequest("Centroid dimension mismatch.");
            }

            clusterRegistry.UpdateCentroids(tenantId, indexName, request.Centroids);
            _registry.IncrementEpoch(tenantId, indexName); // Invalidate cache (L2 clusters might change)

            // Audit
            _auditLogger.Log(new AuditEvent(
                 action: "UPDATE_CENTROIDS",
                 resourceType: AuditResourceTypes.Index,
                 tenantId: tenantId,
                 userId: GetCurrentUserId(),
                 resourceId: indexName,
                 details: $"{request.Centroids.Count} centroids",
                 ipAddress: GetClientIp(),
                 success: true
             ));

            return Ok(new { Message = "Centroids updated." });
        }

        private string? GetCurrentUserId() => HttpContext?.Items["PyropeUserId"]?.ToString();

        private string? GetClientIp() => HttpContext?.Connection?.RemoteIpAddress?.ToString();

        private bool IsSafePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return false;
            var normalized = path.Replace("\\", "/");
            if (normalized.Contains("..")) return false;
            // Block absolute system paths but allow /var/folders (macOS temp)
            if (normalized.StartsWith("/etc") ||
                (normalized.StartsWith("/var") && !normalized.StartsWith("/var/folders")) ||
                normalized.StartsWith("/usr") ||
                normalized.StartsWith("/bin")) return false;
            return true;
        }
    }

    public class CreateIndexRequest
    {
        public string TenantId { get; set; } = "";
        public string IndexName { get; set; } = "";
        public int Dimension { get; set; }
        public string Metric { get; set; } = "L2";
        public string Algorithm { get; set; } = "IVF_FLAT";
        public Dictionary<string, object> Parameters { get; set; } = new();
    }

    public class SnapshotRequest
    {
        public string Path { get; set; } = "";
    }

    public class UpdateCentroidsRequest
    {
        public int Dimension { get; set; }
        public List<float[]> Centroids { get; set; } = new();
    }
}
