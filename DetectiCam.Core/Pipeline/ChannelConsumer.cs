using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace DetectiCam.Core.VideoCapturing
{
    public class ChannelConsumer<TInput> : IDisposable
    {
        private readonly ChannelReader<TInput> _inputReader;
        private readonly ILogger _logger;
        private readonly CancellationTokenSource _internalCts;

        private Func<TInput, CancellationToken, Task>? _processor;
        private Task? _processorTask = null;
        private bool disposedValue;

        protected ILogger Logger => _logger;

        public ChannelConsumer(ChannelReader<TInput> inputReader,
            ILogger logger,
            Func<TInput, CancellationToken, Task>? processor = default)
        {
            _inputReader = inputReader;
            _logger = logger;
            _processor = processor;
            _internalCts = new CancellationTokenSource();
        }

        protected void SetProcessor(Func<TInput, CancellationToken, Task>? processor)
        {
            _processor = processor;
        }

        public Task ExecuteProcessingAsync(CancellationToken stoppingToken)
        {
            if (_processor is null) throw new InvalidOperationException("Processor function not set");

            _processorTask = Task.Run(async () =>
            {
                try
                {
                    using var cts = CancellationTokenSource.CreateLinkedTokenSource(
                        _internalCts.Token, stoppingToken);
                    var linkedToken = cts.Token;

                    await foreach(var inputValue in _inputReader.ReadAllAsync(linkedToken))
                    {
                        await _processor(inputValue, linkedToken).ConfigureAwait(false);
                    }
                }
                catch (OperationCanceledException)
                {
                    _logger.LogWarning("Consume operation cancelled");
                    throw;
                }
                finally
                {
                    _logger.LogInformation("Stopping:completing consumer channel!");
                }
            }, stoppingToken);

            return _processorTask;
        }

        public virtual async Task StopProcessingAsync(CancellationToken cancellationToken)
        {
            _internalCts.Cancel();
            if (_processorTask != null)
            {
                await _processorTask.ConfigureAwait(false);
                _processorTask = null;
            }
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    _internalCts?.Dispose();

                    // TODO: dispose managed state (managed objects)
                }

                // TODO: free unmanaged resources (unmanaged objects) and override finalizer
                // TODO: set large fields to null
                disposedValue = true;
            }
        }

        // // TODO: override finalizer only if 'Dispose(bool disposing)' has code to free unmanaged resources
        // ~MultiChannelMerger()
        // {
        //     // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
        //     Dispose(disposing: false);
        // }

        public void Dispose()
        {
            // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
    }
}
