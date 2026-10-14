using System;
using System.Security.Cryptography;
using System.Text;

namespace Pyrope.GarnetServer.Security
{
    public static class SecurityUtils
    {
        /// <summary>
        /// Compares two strings in constant time to prevent timing attacks.
        /// Returns false if either string is null.
        /// </summary>
        public static bool FixedTimeEquals(string? a, string? b)
        {
            if (a == null || b == null)
            {
                return false;
            }

            var aBytes = Encoding.UTF8.GetBytes(a);
            var bBytes = Encoding.UTF8.GetBytes(b);

            return CryptographicOperations.FixedTimeEquals(aBytes, bBytes);
        }
    }
}
