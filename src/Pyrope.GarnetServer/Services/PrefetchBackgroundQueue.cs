using System;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Pyrope.GarnetServer.Services
{
    public interface IPrefetchBackgroundQueue
    {
        bool TryQueuePrefetch(Func<CancellationToken, Task> workItem);
    }

    public class PrefetchBackgroundQueue : BackgroundService, IPrefetchBackgroundQueue
    {
        private readonly Channel<Func<CancellationToken, Task>> _queue;
        private readonly ILogger<PrefetchBackgroundQueue> _logger;

        public PrefetchBackgroundQueue(ILogger<PrefetchBackgroundQueue> logger)
        {
            _logger = logger;
            // Bounded queue to protect memory and thread pool
            var options = new BoundedChannelOptions(1024)
            {
                FullMode = BoundedChannelFullMode.DropWrite // Drop new prefetch if queue is full
            };
            _queue = Channel.CreateBounded<Func<CancellationToken, Task>>(options);
        }

        public bool TryQueuePrefetch(Func<CancellationToken, Task> workItem)
        {
            if (workItem == null) throw new ArgumentNullException(nameof(workItem));

            bool success = _queue.Writer.TryWrite(workItem);
            if (!success)
            {
                _logger.LogWarning("Prefetch queue is full. Dropping prefetch task.");
            }
            return success;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Prefetch background queue starting.");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var workItem = await _queue.Reader.ReadAsync(stoppingToken);

                    // Execute prefetch in the background
                    // We catch exceptions here so one bad prefetch doesn't kill the worker
                    try
                    {
                        await workItem(stoppingToken);
                    }
                    catch (Exception ex) when (!(ex is OperationCanceledException))
                    {
                        _logger.LogError(ex, "Error executing background prefetch task.");
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error reading from prefetch queue.");
                }
            }

            _logger.LogInformation("Prefetch background queue stopping.");
        }
    }
}
