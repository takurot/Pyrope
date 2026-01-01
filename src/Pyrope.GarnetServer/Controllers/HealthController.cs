using Microsoft.AspNetCore.Mvc;
using Pyrope.GarnetServer.Services;

namespace Pyrope.GarnetServer.Controllers
{
    [ApiController]
    public class HealthController : ControllerBase
    {
        private readonly IMetricsCollector _metrics;

        public HealthController(IMetricsCollector metrics)
        {
            _metrics = metrics;
        }

        [HttpGet("v1/health")]
        public IActionResult GetHealth()
        {
            return Ok(new { Status = "ok" });
        }

        [HttpGet("v1/metrics")]
        public IActionResult GetMetrics()
        {
            var payload = _metrics.GetStats();
            return Content(payload, "text/plain; charset=utf-8");
        }
    }
}
