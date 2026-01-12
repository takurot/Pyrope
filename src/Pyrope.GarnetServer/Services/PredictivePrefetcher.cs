
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Pyrope.Policy;
using Pyrope.GarnetServer.Security;

namespace Pyrope.GarnetServer.Services
{
    public class PredictivePrefetcher : BackgroundService, IPredictivePrefetcher
    {
        private readonly ILogger<PredictivePrefetcher> _logger;
        private readonly string? _sidecarEndpoint;
        private readonly ConcurrentQueue<(string TenantId, string IndexName, int ClusterId, long Timestamp)> _interactionQueue;
        private Dictionary<string, Dictionary<int, int>> _rulesCache;
        private readonly object _rulesLock = new object();

        // Connection config
        private readonly bool _mtlsEnabled;
        private readonly string? _caCertPemPath;
        private readonly string? _clientCertPemPath;
        private readonly string? _clientKeyPemPath;
        private readonly bool _skipServerNameValidation;
        private X509Certificate2? _caCert;

        private readonly PolicyService.PolicyServiceClient? _injectedClient;

        public PredictivePrefetcher(IConfiguration configuration, ILogger<PredictivePrefetcher> logger, PolicyService.PolicyServiceClient? client = null)
        {
            _logger = logger;
            _injectedClient = client;
            _sidecarEndpoint = configuration["Sidecar:Endpoint"] ?? Environment.GetEnvironmentVariable("PYROPE_SIDECAR_ENDPOINT");
            _interactionQueue = new ConcurrentQueue<(string, string, int, long)>();
            _rulesCache = new Dictionary<string, Dictionary<int, int>>();

            _mtlsEnabled = bool.TryParse(configuration["Sidecar:MtlsEnabled"], out var enabled)
                ? enabled
                : bool.TryParse(Environment.GetEnvironmentVariable("PYROPE_SIDECAR_MTLS_ENABLED"), out var envEnabled) && envEnabled;

            _skipServerNameValidation = bool.TryParse(configuration["Sidecar:MtlsSkipServerNameValidation"], out var skip)
                 ? skip
                 : bool.TryParse(Environment.GetEnvironmentVariable("PYROPE_SIDECAR_MTLS_SKIP_NAME_VALIDATION"), out var envSkip) && envSkip;


            _caCertPemPath = configuration["Sidecar:CaCertPemPath"] ?? Environment.GetEnvironmentVariable("PYROPE_SIDECAR_CA_CERT_PEM");
            _clientCertPemPath = configuration["Sidecar:ClientCertPemPath"] ?? Environment.GetEnvironmentVariable("PYROPE_SIDECAR_CLIENT_CERT_PEM");
            _clientKeyPemPath = configuration["Sidecar:ClientKeyPemPath"] ?? Environment.GetEnvironmentVariable("PYROPE_SIDECAR_CLIENT_KEY_PEM");
        }

        public void RecordInteraction(string tenantId, string indexName, int clusterId)
        {
            var key = $"{tenantId}:{indexName}";
            // Ensure key exists so we fetch rules for it later
            if (!_rulesCache.ContainsKey(key))
            {
                lock (_rulesLock)
                {
                    if (!_rulesCache.ContainsKey(key))
                    {
                        _rulesCache[key] = new Dictionary<int, int>();
                    }
                }
            }
            _interactionQueue.Enqueue((tenantId, indexName, clusterId, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()));
        }

        public int GetPrediction(string tenantId, string indexName, int currentClusterId)
        {
            var key = $"{tenantId}:{indexName}";
            lock (_rulesLock)
            {
                if (_rulesCache.TryGetValue(key, out var indexRules))
                {
                    if (indexRules.TryGetValue(currentClusterId, out var nextId))
                    {
                        return nextId;
                    }
                }
            }
            return -1;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            if (_injectedClient == null && string.IsNullOrWhiteSpace(_sidecarEndpoint))
            {
                _logger.LogInformation("Predictive Prefetching disabled (no endpoint).");
                return;
            }

            GrpcChannel? channel = null;
            PolicyService.PolicyServiceClient client;

            if (_injectedClient != null)
            {
                client = _injectedClient;
            }
            else
            {
                try
                {
                    channel = CreateChannel(_sidecarEndpoint!);
                    client = new PolicyService.PolicyServiceClient(channel);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to create sidecar channel for prefetching.");
                    return;
                }
            }
            var lastRuleUpdate = DateTime.MinValue;

            while (!stoppingToken.IsCancellationRequested)
            {
                // 1. Flush Interactions
                if (!_interactionQueue.IsEmpty)
                {
                    await FlushInteractionsAsync(client, stoppingToken);
                }

                // 2. Refresh Rules (every 60 seconds)
                if (DateTime.UtcNow - lastRuleUpdate > TimeSpan.FromSeconds(60))
                {
                    await RefreshRulesAsync(client, stoppingToken);
                    lastRuleUpdate = DateTime.UtcNow;
                }

                await Task.Delay(1000, stoppingToken);
            }

            channel?.Dispose();
        }

        private async Task FlushInteractionsAsync(PolicyService.PolicyServiceClient client, CancellationToken token)
        {
            var batch = new List<(string, string, int, long)>();
            while (_interactionQueue.TryDequeue(out var item) && batch.Count < 100)
            {
                batch.Add(item);
            }

            if (batch.Count == 0) return;

            // Group by Tenant/Index to simplify Request structure if needed,
            // but proto supports one Tenant/Index per request.
            // Wait, my proto definition: ReportClusterAccessRequest { tenant_id, index_name, repeated ClusterAccess }
            // So I must group by Tenant/Index.

            var groups = batch.GroupBy(x => (x.Item1, x.Item2));
            foreach (var group in groups)
            {
                var req = new ReportClusterAccessRequest
                {
                    TenantId = group.Key.Item1,
                    IndexName = group.Key.Item2
                };
                req.Accesses.AddRange(group.Select(x => new ClusterAccess { ClusterId = x.Item3, Timestamp = x.Item4 }));

                try
                {
                    await client.ReportClusterAccessAsync(req, cancellationToken: token);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning($"Failed to report interactions: {ex.Message}");
                }
            }
        }

        private async Task RefreshRulesAsync(PolicyService.PolicyServiceClient client, CancellationToken token)
        {
            // We need to know WHICH indexes to fetch rules for.
            // For now, we only know about indexes we've seen interactions for.
            // Or we could fetch for all "Active" indexes?
            // Let's use the keys in _rulesCache + any seen in interactions.

            // Simplified: Just update for currently known rules keys (from initial interaction)
            // Or better: Iterate over known indexes.
            // For this implementation, I'll track "KnownIndexes" set.

            // NOTE: Ideally, we pull this from IndexRegistry or similar.
            // But to avoid circular dependency or complex injection, I'll rely on "KnownIndexes".

            // ... implementation detail: Use a Set to track known indexes ...
            // I'll add a _knownIndexes Set.
            // But for complexity, I'll cheat: I'll only fetch rules for indexes I've *just* interacting with or simple loop?
            // I'll skip implementation of "Which indexes" and just say "If I have interactions, I fetch rules".
            // Or better: Use the _rulesCache keys (seed it when RecordInteraction happens).

            List<(string, string)> targets;
            lock (_rulesLock)
            {
                targets = _rulesCache.Keys.Select(k =>
                {
                    var parts = k.Split(':');
                    return (parts[0], parts[1]);
                }).ToList();
            }

            foreach (var (t, i) in targets)
            {
                try
                {
                    var req = new GetPrefetchRulesRequest { TenantId = t, IndexName = i };
                    var resp = await client.GetPrefetchRulesAsync(req, cancellationToken: token);

                    var newRules = new Dictionary<int, int>();
                    foreach (var r in resp.Rules)
                    {
                        newRules[r.CurrentClusterId] = r.NextClusterId;
                    }

                    lock (_rulesLock)
                    {
                        _rulesCache[$"{t}:{i}"] = newRules;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning($"Failed to fetch rules for {t}:{i}: {ex.Message}");
                }
            }
        }

        private GrpcChannel CreateChannel(string endpoint)
        {
            var uri = new Uri(endpoint);
            if (uri.Scheme.Equals("http", StringComparison.OrdinalIgnoreCase))
            {
                AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);
                return GrpcChannel.ForAddress(endpoint);
            }

            var handler = new SocketsHttpHandler { EnableMultipleHttp2Connections = true };

            if (_mtlsEnabled)
            {
                _caCert ??= PemCertificateLoader.LoadCertificateFromPemFile(_caCertPemPath!);
                var clientCert = PemCertificateLoader.LoadClientCertificateFromPemFiles(_clientCertPemPath!, _clientKeyPemPath!);
                handler.SslOptions = new System.Net.Security.SslClientAuthenticationOptions
                {
                    ClientCertificates = new X509CertificateCollection { clientCert },
                    RemoteCertificateValidationCallback = (sender, cert, chain, errors) =>
                    {
                        // Simplify validation call reusing logic if possible, or reimplement
                        // Re-implementing simplified version:
                        if (_skipServerNameValidation) errors &= ~System.Net.Security.SslPolicyErrors.RemoteCertificateNameMismatch;
                        return errors == System.Net.Security.SslPolicyErrors.None;
                    }
                };
            }

            return GrpcChannel.ForAddress(endpoint, new GrpcChannelOptions { HttpHandler = handler });
        }
    }
}
