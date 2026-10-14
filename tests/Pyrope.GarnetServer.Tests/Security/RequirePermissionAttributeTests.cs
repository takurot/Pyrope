using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Pyrope.GarnetServer.Model;
using Pyrope.GarnetServer.Security;
using Xunit;

namespace Pyrope.GarnetServer.Tests.Security
{
    public class RequirePermissionAttributeTests
    {
        private readonly Mock<IAuthorizationService> _mockAuthService = new();

        private ActionExecutingContext CreateContext(
            string? apiKey = "valid-key",
            string? routeTenantId = null,
            string? queryTenantId = null,
            string? itemTenantId = null,
            bool isAdmin = false,
            object? actionArg = null)
        {
            var services = new ServiceCollection();
            services.AddSingleton(_mockAuthService.Object);
            var serviceProvider = services.BuildServiceProvider();

            var httpContext = new DefaultHttpContext
            {
                RequestServices = serviceProvider
            };

            if (isAdmin)
            {
                httpContext.Items["IsAdmin"] = true;
            }
            if (apiKey != null)
            {
                httpContext.Items["PyropeApiKey"] = apiKey;
            }
            if (itemTenantId != null)
            {
                httpContext.Items["PyropeTenantId"] = itemTenantId;
            }
            if (queryTenantId != null)
            {
                httpContext.Request.QueryString = new QueryString($"?tenantId={queryTenantId}");
            }

            var routeData = new RouteData();
            if (routeTenantId != null)
            {
                routeData.Values["tenantId"] = routeTenantId;
            }

            var actionContext = new ActionContext(
                httpContext,
                routeData,
                new ActionDescriptor()
            );

            var actionArguments = new Dictionary<string, object?>();
            if (actionArg != null)
            {
                actionArguments["request"] = actionArg;
            }

            return new ActionExecutingContext(
                actionContext,
                new List<IFilterMetadata>(),
                actionArguments,
                new object()
            );
        }

        [Fact]
        public async Task GlobalAdmin_BypassesPermissionCheck()
        {
            var filter = new PermissionFilter(Permission.IndexCreate);
            var context = CreateContext(isAdmin: true);
            bool nextCalled = false;

            await filter.OnActionExecutionAsync(context, () =>
            {
                nextCalled = true;
                return Task.FromResult<ActionExecutedContext>(null!);
            });

            Assert.True(nextCalled);
            Assert.Null(context.Result);
        }

        [Fact]
        public async Task MissingApiKey_ReturnsUnauthorized()
        {
            var filter = new PermissionFilter(Permission.IndexRead);
            var context = CreateContext(apiKey: null, routeTenantId: "tenant1");

            await filter.OnActionExecutionAsync(context, () => Task.FromResult<ActionExecutedContext>(null!));

            Assert.IsType<UnauthorizedResult>(context.Result);
        }

        [Fact]
        public async Task NonAdmin_MissingTenantId_FailsClosedWithForbidden_NoSystemFallback()
        {
            var filter = new PermissionFilter(Permission.IndexRead);
            // Neither route, query, nor actionArg has tenantId
            var context = CreateContext(apiKey: "valid-key", routeTenantId: null, queryTenantId: null);

            await filter.OnActionExecutionAsync(context, () => Task.FromResult<ActionExecutedContext>(null!));

            Assert.IsType<ForbidResult>(context.Result);
            // Verify HasPermission was NEVER called with "system"
            _mockAuthService.Verify(a => a.HasPermission("system", It.IsAny<string>(), It.IsAny<Permission>()), Times.Never);
        }

        [Fact]
        public async Task NonAdmin_CrossTenantAttempt_ReturnsForbidden()
        {
            var filter = new PermissionFilter(Permission.IndexRead);
            // Authenticated as tenant1 in middleware, but route requests tenant2
            var context = CreateContext(apiKey: "user-key", routeTenantId: "tenant2", itemTenantId: "tenant1");

            await filter.OnActionExecutionAsync(context, () => Task.FromResult<ActionExecutedContext>(null!));

            Assert.IsType<ForbidResult>(context.Result);
        }

        public class SampleRequestWithTenant
        {
            public string? TenantId { get; set; }
        }

        [Fact]
        public async Task NonAdmin_TenantIdInBodyArgument_ExtractsAndChecksPermission()
        {
            var filter = new PermissionFilter(Permission.IndexCreate);
            var bodyArg = new SampleRequestWithTenant { TenantId = "tenant1" };
            var context = CreateContext(apiKey: "user-key", actionArg: bodyArg, itemTenantId: "tenant1");

            _mockAuthService.Setup(a => a.HasPermission("tenant1", "user-key", Permission.IndexCreate)).Returns(true);
            _mockAuthService.Setup(a => a.GetUserId("tenant1", "user-key")).Returns("user1");
            _mockAuthService.Setup(a => a.GetRole("tenant1", "user-key")).Returns(Role.TenantAdmin);

            bool nextCalled = false;
            await filter.OnActionExecutionAsync(context, () =>
            {
                nextCalled = true;
                return Task.FromResult<ActionExecutedContext>(null!);
            });

            Assert.True(nextCalled);
            Assert.Equal("user1", context.HttpContext.Items["PyropeUserId"]);
            Assert.Equal("TenantAdmin", context.HttpContext.Items["PyropeUserRole"]);
        }
    }
}
