using Microsoft.AspNetCore.Mvc;
using Pyrope.GarnetServer.Extensions;
using Pyrope.GarnetServer.Services;
using Pyrope.GarnetServer.Vector;

namespace Pyrope.GarnetServer.Controllers
{
    [ApiController]
    [Route("v1/indexes")]
    public class IndexController : ControllerBase
    {
        private readonly VectorIndexRegistry _registry;

        public IndexController(VectorIndexRegistry registry)
        {
            _registry = registry;
        }

        [HttpPost]
        public IActionResult CreateIndex([FromBody] CreateIndexRequest request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.TenantId) || string.IsNullOrWhiteSpace(request.IndexName))
                return BadRequest("Invalid request.");

            try
            {
                var metric = Enum.Parse<VectorMetric>(request.Metric, true);
                _registry.GetOrCreate(request.TenantId, request.IndexName, request.Dimension, metric);
                return Created($"/v1/indexes/{request.TenantId}/{request.IndexName}", new { Message = "Index created." });
            }
            catch (Exception ex)
            {
                return BadRequest(ex.Message);
            }
        }

        [HttpPost("{tenantId}/{indexName}/build")]
        public IActionResult BuildIndex(string tenantId, string indexName)
        {
            if (_registry.TryGetIndex(tenantId, indexName, out var index))
            {
                index.Build();
                return Ok(new { Message = "Index build triggered." });
            }
            return NotFound("Index not found.");
        }

        [HttpPost("{tenantId}/{indexName}/snapshot")]
        public IActionResult SnapshotIndex(string tenantId, string indexName, [FromBody] SnapshotRequest request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.Path))
            {
                return BadRequest("Path is required.");
            }

            if (_registry.TryGetIndex(tenantId, indexName, out var index))
            {
                try
                {
                    index.Snapshot(request.Path);
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
        public IActionResult LoadIndex(string tenantId, string indexName, [FromBody] SnapshotRequest request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.Path))
            {
                return BadRequest("Path is required.");
            }

            if (_registry.TryGetIndex(tenantId, indexName, out var index))
            {
                try
                {
                    index.Load(request.Path);
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
        public IActionResult GetStats(string tenantId, string indexName)
        {
            if (_registry.TryGetIndex(tenantId, indexName, out var index))
            {
                return Ok(index.GetStats());
            }
            return NotFound("Index not found.");
        }
    }

    public class CreateIndexRequest
    {
        public string TenantId { get; set; } = "";
        public string IndexName { get; set; } = "";
        public int Dimension { get; set; }
        public string Metric { get; set; } = "L2";
    }

    public class SnapshotRequest
    {
        public string Path { get; set; } = "";
    }
}
