using System.Collections.Generic;
using Pyrope.GarnetServer.Extensions;
using Xunit;

namespace Pyrope.GarnetServer.Tests.Vector
{
    public class VectorCommandParserTests
    {
        [Fact]
        public void Parse_WithMetaTagsAndNumericFields()
        {
            var args = new List<string>
            {
                "tenantA",
                "indexA",
                "doc1",
                "VECTOR",
                "[1.0,2.0,3.0]",
                "META",
                "{\"source\":\"news\"}",
                "TAGS",
                "[\"hot\",\"fresh\"]",
                "NUMERIC_FIELDS",
                "{\"score\":1.5}"
            };

            var request = VectorCommandParser.Parse(args);

            Assert.Equal("tenantA", request.TenantId);
            Assert.Equal("indexA", request.IndexName);
            Assert.Equal("doc1", request.Id);
            Assert.Equal(3, request.Vector.Length);
            Assert.Equal("{\"source\":\"news\"}", request.MetaJson);
            Assert.Equal(new[] { "hot", "fresh" }, request.Tags);
            Assert.Equal(1.5, request.NumericFields["score"]);
        }
    }
}
