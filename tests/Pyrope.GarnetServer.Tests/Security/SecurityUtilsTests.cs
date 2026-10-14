using Pyrope.GarnetServer.Security;
using Xunit;

namespace Pyrope.GarnetServer.Tests.Security
{
    public class SecurityUtilsTests
    {
        [Theory]
        [InlineData("secret123", "secret123", true)]
        [InlineData("secret123", "secret124", false)]
        [InlineData("secret", "secret123", false)]
        [InlineData("", "", true)]
        [InlineData(null, "secret", false)]
        [InlineData("secret", null, false)]
        [InlineData(null, null, false)]
        public void FixedTimeEquals_ValidatesCorrectly(string? a, string? b, bool expected)
        {
            var result = SecurityUtils.FixedTimeEquals(a, b);
            Assert.Equal(expected, result);
        }
    }
}
