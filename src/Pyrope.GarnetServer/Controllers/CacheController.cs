using Microsoft.AspNetCore.Mvc;
using Pyrope.GarnetServer.Model;
using Pyrope.GarnetServer.Policies;
using Pyrope.GarnetServer.Utils;

namespace Pyrope.GarnetServer.Controllers
{
    [ApiController]
    [Route("v1/cache")]
    public class CacheController : ControllerBase
    {
        private readonly CachePolicyStore _policyStore;
        private readonly ICacheAdmin _cacheAdmin;

        public CacheController(CachePolicyStore policyStore, ICacheAdmin cacheAdmin)
        {
            _policyStore = policyStore;
            _cacheAdmin = cacheAdmin;
        }

        [HttpGet("policies")]
        public IActionResult GetPolicies()
        {
            return Ok(_policyStore.Current);
        }

        [HttpPut("policies")]
        public IActionResult UpdatePolicies([FromBody] CachePolicyConfig request)
        {
            if (request == null)
            {
                return BadRequest("Invalid request.");
            }

            if (request.DefaultTtlSeconds < 0)
            {
                return BadRequest("DefaultTtlSeconds must be >= 0.");
            }

            _policyStore.Update(request);
            return Ok(request);
        }

        [HttpPost("flush")]
        public IActionResult Flush()
        {
            var removed = _cacheAdmin.Clear();
            return Ok(new { Removed = removed });
        }

        [HttpPost("invalidate")]
        public IActionResult Invalidate([FromBody] CacheInvalidateRequest request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.TenantId) || string.IsNullOrWhiteSpace(request.IndexName))
            {
                return BadRequest("TenantId and IndexName are required.");
            }

            var prefix = KeyUtils.GetCacheKeyPrefix(request.TenantId, request.IndexName);
            var removed = _cacheAdmin.RemoveByPrefix(prefix);
            return Ok(new { Removed = removed });
        }
    }

    public sealed class CacheInvalidateRequest
    {
        public string TenantId { get; set; } = "";
        public string IndexName { get; set; } = "";
    }
}
