namespace Pyrope.GarnetServer.Extensions
{
    internal static class VectorErrorCodes
    {
        public const string Ok = "VEC_OK";
        public const string DimMismatch = "VEC_ERR_DIM";
        public const string NotFound = "VEC_ERR_NOT_FOUND";
        public const string Quota = "VEC_ERR_QUOTA";
        public const string Busy = "VEC_ERR_BUSY";
        public const string Auth = "VEC_ERR_AUTH";
    }
}
