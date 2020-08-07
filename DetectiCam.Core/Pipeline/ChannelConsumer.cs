using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace DetectiCam.Core.VideoCapturing
{
    public abstract class ChannelConsumer<TInput> : IDisposable
    {
        private readonly ChannelReader<TInput> _inputReader;
        private readonly CancellationTokenSource _internalCts;
        private Task? _processorTask = null;

        protected ILogger Logger { get; }

        public ChannelConsumer(ChannelReader<TInput> inputReader,
            ILogger logger)
        {
            _inputReader = inputReader;
            Logger = logger;
            _internalCts = new CancellationTokenSource();
        }

        protected abstract Task ExecuteProcessorAsync(TInput input, CancellationToken cancellationToken);

        public async Task ExecuteProcessingAsync(CancellationToken stoppingToken)
        {
            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(
                    _internalCts.Token, stoppingToken);
                var linkedToken = cts.Token;

                await foreach(var inputValue in _inputReader.ReadAllAsync(linkedToken))
                {
                    await ExecuteProcessorAsync(inputValue, linkedToken).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                Logger.LogWarning("Consume operation cancelled");
                throw;
            }
            finally
            {
                Logger.LogInformation("Stopping:completing consumer channel!");
            }
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

        public void Dispose()
        {
            _internalCts?.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}
