using System;
using System.Net.Http;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using Grpc.Net.Client;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Pyrope.Policy;
using Pyrope.GarnetServer.Security;

namespace Pyrope.GarnetServer.Extensions
{
    public static class SidecarExtensions
    {
        public static IServiceCollection AddSidecarClient(this IServiceCollection services, IConfiguration configuration)
        {
            var endpoint = configuration["Sidecar:Endpoint"] ?? Environment.GetEnvironmentVariable("PYROPE_SIDECAR_ENDPOINT");

            if (string.IsNullOrWhiteSpace(endpoint))
            {
                // Register a null client or handle gracefully? 
                // For now, let's not register it if not configured, dependencies should handle null or optional.
                // However, DI prefers explicit registration. 
                // We'll register a factory that returns null if not configured, but that's tricky for injection.
                // Better: Register an instance if configured, otherwise do nothing. Consumers should inject PolicyServiceClient? (nullable).
                return services;
            }

            var mtlsEnabled = bool.TryParse(configuration["Sidecar:MtlsEnabled"], out var enabled)
                ? enabled
                : bool.TryParse(Environment.GetEnvironmentVariable("PYROPE_SIDECAR_MTLS_ENABLED"), out var envEnabled) && envEnabled;

            var skipServerNameValidation = bool.TryParse(configuration["Sidecar:MtlsSkipServerNameValidation"], out var skip)
                ? skip
                : bool.TryParse(Environment.GetEnvironmentVariable("PYROPE_SIDECAR_MTLS_SKIP_NAME_VALIDATION"), out var envSkip) && envSkip;

            var caCertPemPath = configuration["Sidecar:CaCertPemPath"] ?? Environment.GetEnvironmentVariable("PYROPE_SIDECAR_CA_CERT_PEM");
            var clientCertPemPath = configuration["Sidecar:ClientCertPemPath"] ?? Environment.GetEnvironmentVariable("PYROPE_SIDECAR_CLIENT_CERT_PEM");
            var clientKeyPemPath = configuration["Sidecar:ClientKeyPemPath"] ?? Environment.GetEnvironmentVariable("PYROPE_SIDECAR_CLIENT_KEY_PEM");

            services.AddSingleton<PolicyService.PolicyServiceClient>(sp =>
            {
                var logger = sp.GetRequiredService<ILogger<PolicyService.PolicyServiceClient>>();

                var uri = new Uri(endpoint);
                if (uri.Scheme.Equals("http", StringComparison.OrdinalIgnoreCase))
                {
                    AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);
                    return new PolicyService.PolicyServiceClient(GrpcChannel.ForAddress(endpoint));
                }

                if (!uri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException($"Unsupported sidecar endpoint scheme: {uri.Scheme}");
                }

                var handler = new SocketsHttpHandler
                {
                    EnableMultipleHttp2Connections = true
                };

                if (mtlsEnabled)
                {
                    if (string.IsNullOrWhiteSpace(caCertPemPath) ||
                        string.IsNullOrWhiteSpace(clientCertPemPath) ||
                        string.IsNullOrWhiteSpace(clientKeyPemPath))
                    {
                        throw new InvalidOperationException("mTLS enabled but cert paths are not configured (CA/client cert/client key).");
                    }

                    var caCert = PemCertificateLoader.LoadCertificateFromPemFile(caCertPemPath);
                    var clientCert = PemCertificateLoader.LoadClientCertificateFromPemFiles(clientCertPemPath, clientKeyPemPath);

                    handler.SslOptions = new SslClientAuthenticationOptions
                    {
                        ClientCertificates = new X509CertificateCollection { clientCert },
                        RemoteCertificateValidationCallback = (sender, certificate, chain, sslPolicyErrors) =>
                        {
                            if (caCert == null) return sslPolicyErrors == SslPolicyErrors.None;
                            if (certificate == null) return false;

                            if (skipServerNameValidation)
                            {
                                sslPolicyErrors &= ~SslPolicyErrors.RemoteCertificateNameMismatch;
                            }

                            try
                            {
                                using var customChain = new X509Chain();
                                customChain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
                                customChain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
                                customChain.ChainPolicy.CustomTrustStore.Add(caCert);

                                var serverCert = new X509Certificate2(certificate);
                                return customChain.Build(serverCert);
                            }
                            catch
                            {
                                return false;
                            }
                        }
                    };
                }

                var channel = GrpcChannel.ForAddress(endpoint, new GrpcChannelOptions { HttpHandler = handler });
                return new PolicyService.PolicyServiceClient(channel);
            });

            return services;
        }
    }
}
