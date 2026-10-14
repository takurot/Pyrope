using System;
using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Pyrope.GarnetServer.Extensions;
using Xunit;

namespace Pyrope.GarnetServer.Tests.Extensions
{
    public class SidecarExtensionsTests : IDisposable
    {
        private readonly X509Certificate2 _caCert;
        private readonly X509Certificate2 _leafCert;
        private readonly X509Certificate2 _untrustedCert;

        public SidecarExtensionsTests()
        {
#pragma warning disable SYSLIB0057
            var now = DateTimeOffset.UtcNow;
            // Create test CA cert
            using var rsaCa = RSA.Create(2048);
            var caReq = new CertificateRequest("CN=PyropeTestCA", rsaCa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            caReq.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, true, 0, true));
            caReq.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign, true));
            using var caWithKey = caReq.CreateSelfSigned(now.AddMinutes(-5), now.AddDays(1));
            _caCert = new X509Certificate2(caWithKey.Export(X509ContentType.Cert));

            // Create leaf cert signed by test CA
            using var rsaLeaf = RSA.Create(2048);
            var leafReq = new CertificateRequest("CN=sidecar.internal", rsaLeaf, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            leafReq.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
            var serial = new byte[8];
            RandomNumberGenerator.Fill(serial);
            using var leafWithKey = leafReq.Create(caWithKey, now.AddMinutes(-5), now.AddHours(1), serial);
            _leafCert = new X509Certificate2(leafWithKey.Export(X509ContentType.Cert));

            // Create untrusted self-signed cert
            using var rsaUntrusted = RSA.Create(2048);
            var untrustedReq = new CertificateRequest("CN=untrusted.internal", rsaUntrusted, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            using var untrustedWithKey = untrustedReq.CreateSelfSigned(now.AddMinutes(-5), now.AddHours(1));
            _untrustedCert = new X509Certificate2(untrustedWithKey.Export(X509ContentType.Cert));
#pragma warning restore SYSLIB0057
        }

        public void Dispose()
        {
            _caCert.Dispose();
            _leafCert.Dispose();
            _untrustedCert.Dispose();
        }

        [Fact]
        public void ValidateRemoteCertificate_NullCertificate_ReturnsFalse()
        {
            var result = SidecarExtensions.ValidateRemoteCertificate(
                certificate: null,
                sslPolicyErrors: SslPolicyErrors.None,
                caCert: _caCert,
                skipServerNameValidation: false);

            Assert.False(result);
        }

        [Fact]
        public void ValidateRemoteCertificate_CertificateNotAvailable_ReturnsFalse()
        {
            var result = SidecarExtensions.ValidateRemoteCertificate(
                certificate: _leafCert,
                sslPolicyErrors: SslPolicyErrors.RemoteCertificateNotAvailable,
                caCert: _caCert,
                skipServerNameValidation: false);

            Assert.False(result);
        }

        [Fact]
        public void ValidateRemoteCertificate_HostnameMismatch_WhenValidationRequired_ReturnsFalse()
        {
            // Even though the certificate is signed by _caCert, hostname mismatch MUST fail when skipServerNameValidation is false
            var result = SidecarExtensions.ValidateRemoteCertificate(
                certificate: _leafCert,
                sslPolicyErrors: SslPolicyErrors.RemoteCertificateNameMismatch | SslPolicyErrors.RemoteCertificateChainErrors,
                caCert: _caCert,
                skipServerNameValidation: false);

            Assert.False(result);
        }

        [Fact]
        public void ValidateRemoteCertificate_HostnameMismatch_WhenSkipped_EvaluatesChain()
        {
            // When skipServerNameValidation is true, RemoteCertificateNameMismatch should be ignored, and chain validation succeeds
            var result = SidecarExtensions.ValidateRemoteCertificate(
                certificate: _leafCert,
                sslPolicyErrors: SslPolicyErrors.RemoteCertificateNameMismatch | SslPolicyErrors.RemoteCertificateChainErrors,
                caCert: _caCert,
                skipServerNameValidation: true);

            Assert.True(result);
        }

        [Fact]
        public void ValidateRemoteCertificate_ValidCertAndMatchingHost_ReturnsTrue()
        {
            // When hostname matches (no RemoteCertificateNameMismatch, only ChainErrors because it's a private CA), validation succeeds
            var result = SidecarExtensions.ValidateRemoteCertificate(
                certificate: _leafCert,
                sslPolicyErrors: SslPolicyErrors.RemoteCertificateChainErrors,
                caCert: _caCert,
                skipServerNameValidation: false);

            Assert.True(result);
        }

        [Fact]
        public void ValidateRemoteCertificate_UntrustedCert_ReturnsFalse()
        {
            // When cert is not signed by _caCert, custom chain build fails
            var result = SidecarExtensions.ValidateRemoteCertificate(
                certificate: _untrustedCert,
                sslPolicyErrors: SslPolicyErrors.RemoteCertificateChainErrors,
                caCert: _caCert,
                skipServerNameValidation: false);

            Assert.False(result);
        }

        [Fact]
        public void ValidateRemoteCertificate_NullCaCert_FallsBackToSslPolicyErrors()
        {
            // When caCert is null, falls back to sslPolicyErrors == SslPolicyErrors.None
            var validResult = SidecarExtensions.ValidateRemoteCertificate(
                certificate: _leafCert,
                sslPolicyErrors: SslPolicyErrors.None,
                caCert: null,
                skipServerNameValidation: false);
            Assert.True(validResult);

            var invalidResult = SidecarExtensions.ValidateRemoteCertificate(
                certificate: _leafCert,
                sslPolicyErrors: SslPolicyErrors.RemoteCertificateChainErrors,
                caCert: null,
                skipServerNameValidation: false);
            Assert.False(invalidResult);
        }
    }
}
