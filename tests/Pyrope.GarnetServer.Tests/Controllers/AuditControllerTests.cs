using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Pyrope.GarnetServer.Controllers;
using Pyrope.GarnetServer.Model;
using Pyrope.GarnetServer.Services;
using Xunit;

namespace Pyrope.GarnetServer.Tests.Controllers
{
    public class AuditControllerTests
    {
        private readonly AuditLogger _auditLogger;
        private readonly AuditController _controller;

        public AuditControllerTests()
        {
            _auditLogger = new AuditLogger(maxInMemoryEvents: 100);
            _controller = new AuditController(_auditLogger);

            // Populate some events for testing
            _auditLogger.Log(new AuditEvent("CreateTenant", "Tenant", "tenant1", "admin", "tenant1", success: true));
            _auditLogger.Log(new AuditEvent("CreateIndex", "Index", "tenant1", "user1", "idx1", success: true));
            _auditLogger.Log(new AuditEvent("CreateIndex", "Index", "tenant2", "user2", "idx2", success: true));
            _auditLogger.Log(new AuditEvent("CreateTenant", "Tenant", "tenant2", "admin", "tenant2", success: true));
        }

        private void SetCallerContext(bool isAdmin, string? tenantId)
        {
            var httpContext = new DefaultHttpContext();
            if (isAdmin)
            {
                httpContext.Items["IsAdmin"] = true;
            }
            if (tenantId != null)
            {
                httpContext.Items["PyropeTenantId"] = tenantId;
            }
            _controller.ControllerContext = new ControllerContext
            {
                HttpContext = httpContext
            };
        }

        private static T GetProperty<T>(object obj, string propertyName)
        {
            var prop = obj.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
            Assert.NotNull(prop);
            return (T)prop.GetValue(obj)!;
        }

        [Fact]
        public void GetLogs_NonAdmin_DifferentTenant_ReturnsForbid()
        {
            SetCallerContext(isAdmin: false, tenantId: "tenant1");

            var result = _controller.GetLogs(tenantId: "tenant2");

            Assert.IsType<ForbidResult>(result);
        }

        [Fact]
        public void GetLogs_NonAdmin_NoTenantParam_DefaultsToOwnTenant()
        {
            SetCallerContext(isAdmin: false, tenantId: "tenant1");

            var result = _controller.GetLogs(tenantId: null);

            var okResult = Assert.IsType<OkObjectResult>(result);
            var count = GetProperty<int>(okResult.Value!, "Count");
            Assert.Equal(2, count);
        }

        [Fact]
        public void GetLogs_NonAdmin_OwnTenant_ReturnsOk()
        {
            SetCallerContext(isAdmin: false, tenantId: "tenant1");

            var result = _controller.GetLogs(tenantId: "tenant1");

            var okResult = Assert.IsType<OkObjectResult>(result);
            var count = GetProperty<int>(okResult.Value!, "Count");
            Assert.Equal(2, count);
        }

        [Fact]
        public void GetLogs_InvalidTenantFormat_ReturnsBadRequest()
        {
            SetCallerContext(isAdmin: false, tenantId: "tenant1");

            var result = _controller.GetLogs(tenantId: "invalid..tenant");

            var badRequest = Assert.IsType<BadRequestObjectResult>(result);
            Assert.NotNull(badRequest.Value);
        }

        [Fact]
        public void GetStats_NonAdmin_DifferentTenant_ReturnsForbid()
        {
            SetCallerContext(isAdmin: false, tenantId: "tenant1");

            var result = _controller.GetStats(tenantId: "tenant2");

            Assert.IsType<ForbidResult>(result);
        }

        [Fact]
        public void GetStats_NonAdmin_DefaultsToOwnTenant()
        {
            SetCallerContext(isAdmin: false, tenantId: "tenant1");

            var result = _controller.GetStats(tenantId: null);

            var okResult = Assert.IsType<OkObjectResult>(result);
            var totalEvents = GetProperty<int>(okResult.Value!, "TotalEvents");
            Assert.Equal(2, totalEvents);
        }

        [Fact]
        public void GetStats_InvalidTenantFormat_ReturnsBadRequest()
        {
            SetCallerContext(isAdmin: false, tenantId: "tenant1");

            var result = _controller.GetStats(tenantId: "invalid..tenant");

            var badRequest = Assert.IsType<BadRequestObjectResult>(result);
            Assert.NotNull(badRequest.Value);
        }

        [Fact]
        public void GetLogs_Admin_CanQueryAnyOrAllTenants()
        {
            SetCallerContext(isAdmin: true, tenantId: null);

            // Query all
            var allResult = _controller.GetLogs(tenantId: null);
            var okAll = Assert.IsType<OkObjectResult>(allResult);
            var allCount = GetProperty<int>(okAll.Value!, "Count");
            Assert.Equal(4, allCount);

            // Query tenant2
            var tenant2Result = _controller.GetLogs(tenantId: "tenant2");
            var okTenant2 = Assert.IsType<OkObjectResult>(tenant2Result);
            var tenant2Count = GetProperty<int>(okTenant2.Value!, "Count");
            Assert.Equal(2, tenant2Count);
        }

        [Fact]
        public void GetStats_Admin_GlobalAndScoped()
        {
            SetCallerContext(isAdmin: true, tenantId: null);

            // Global stats
            var globalResult = _controller.GetStats(tenantId: null);
            var okGlobal = Assert.IsType<OkObjectResult>(globalResult);
            var globalCount = GetProperty<int>(okGlobal.Value!, "TotalEvents");
            Assert.Equal(4, globalCount);

            // Tenant1 stats
            var tenant1Result = _controller.GetStats(tenantId: "tenant1");
            var okTenant1 = Assert.IsType<OkObjectResult>(tenant1Result);
            var tenant1Count = GetProperty<int>(okTenant1.Value!, "TotalEvents");
            Assert.Equal(2, tenant1Count);
        }
    }
}
