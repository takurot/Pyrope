using System;
using System.Linq;
using Xunit;
using Pyrope.GarnetServer.Model;
using Pyrope.GarnetServer.Services;

namespace Pyrope.GarnetServer.Tests.Services
{
    public class AuditLoggerTests
    {
        [Fact]
        public void Log_AddsEventToLogger()
        {
            // Arrange
            var logger = new AuditLogger();
            var evt = new AuditEvent(
                action: AuditActions.CreateIndex,
                resourceType: AuditResourceTypes.Index,
                tenantId: "tenant1",
                resourceId: "index1"
            );

            // Act
            logger.Log(evt);

            // Assert
            Assert.Equal(1, logger.Count);
        }

        [Fact]
        public void Query_ReturnsAllEvents_WhenNoFilters()
        {
            // Arrange
            var logger = new AuditLogger();
            logger.Log(new AuditEvent(AuditActions.CreateIndex, AuditResourceTypes.Index, tenantId: "t1"));
            logger.Log(new AuditEvent(AuditActions.DeleteIndex, AuditResourceTypes.Index, tenantId: "t2"));
            logger.Log(new AuditEvent(AuditActions.FlushCache, AuditResourceTypes.Cache));

            // Act
            var results = logger.Query().ToList();

            // Assert
            Assert.Equal(3, results.Count);
        }

        [Fact]
        public void Query_FiltersByTenantId()
        {
            // Arrange
            var logger = new AuditLogger();
            logger.Log(new AuditEvent(AuditActions.CreateIndex, AuditResourceTypes.Index, tenantId: "tenant1"));
            logger.Log(new AuditEvent(AuditActions.CreateIndex, AuditResourceTypes.Index, tenantId: "tenant2"));
            logger.Log(new AuditEvent(AuditActions.BuildIndex, AuditResourceTypes.Index, tenantId: "tenant1"));

            // Act
            var results = logger.Query(tenantId: "tenant1").ToList();

            // Assert
            Assert.Equal(2, results.Count);
            Assert.All(results, e => Assert.Equal("tenant1", e.TenantId));
        }

        [Fact]
        public void Query_FiltersByAction()
        {
            // Arrange
            var logger = new AuditLogger();
            logger.Log(new AuditEvent(AuditActions.CreateIndex, AuditResourceTypes.Index));
            logger.Log(new AuditEvent(AuditActions.DeleteIndex, AuditResourceTypes.Index));
            logger.Log(new AuditEvent(AuditActions.CreateIndex, AuditResourceTypes.Index));

            // Act
            var results = logger.Query(action: AuditActions.CreateIndex).ToList();

            // Assert
            Assert.Equal(2, results.Count);
            Assert.All(results, e => Assert.Equal(AuditActions.CreateIndex, e.Action));
        }

        [Fact]
        public void Query_RespectsLimit()
        {
            // Arrange
            var logger = new AuditLogger();
            for (int i = 0; i < 100; i++)
            {
                logger.Log(new AuditEvent(AuditActions.CreateIndex, AuditResourceTypes.Index));
            }

            // Act
            var results = logger.Query(limit: 10).ToList();

            // Assert
            Assert.Equal(10, results.Count);
        }

        [Fact]
        public void Query_ReturnsInReverseChronologicalOrder()
        {
            // Arrange
            var logger = new AuditLogger();
            logger.Log(new AuditEvent(AuditActions.CreateIndex, AuditResourceTypes.Index, resourceId: "first"));
            System.Threading.Thread.Sleep(10); // Ensure different timestamps
            logger.Log(new AuditEvent(AuditActions.CreateIndex, AuditResourceTypes.Index, resourceId: "second"));

            // Act
            var results = logger.Query().ToList();

            // Assert
            Assert.Equal("second", results[0].ResourceId);
            Assert.Equal("first", results[1].ResourceId);
        }

        [Fact]
        public void Log_EvictsOldEvents_WhenMaxReached()
        {
            // Arrange
            var logger = new AuditLogger(maxInMemoryEvents: 5);
            for (int i = 0; i < 10; i++)
            {
                logger.Log(new AuditEvent(AuditActions.CreateIndex, AuditResourceTypes.Index, resourceId: $"event{i}"));
            }

            // Assert
            Assert.Equal(5, logger.Count);
        }

        [Fact]
        public void AuditEvent_GeneratesUniqueEventId()
        {
            // Arrange & Act
            var evt1 = new AuditEvent(AuditActions.CreateIndex, AuditResourceTypes.Index);
            var evt2 = new AuditEvent(AuditActions.CreateIndex, AuditResourceTypes.Index);

            // Assert
            Assert.NotEqual(evt1.EventId, evt2.EventId);
        }

        [Fact]
        public void AuditEvent_SetsTimestampAutomatically()
        {
            // Arrange
            var before = DateTimeOffset.UtcNow;

            // Act
            var evt = new AuditEvent(AuditActions.CreateIndex, AuditResourceTypes.Index);

            // Assert
            Assert.True(evt.Timestamp >= before);
            Assert.True(evt.Timestamp <= DateTimeOffset.UtcNow);
        }
    }
}
