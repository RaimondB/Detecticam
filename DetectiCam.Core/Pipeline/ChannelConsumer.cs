using Microsoft.Extensions.Logging;
using System;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using static DetectiCam.Core.Common.ExceptionFilterUtility;

namespace DetectiCam.Core.VideoCapturing
{
    public abstract class ChannelConsumer<TInput> : IDisposable
    {
        private readonly ChannelReader<TInput> _inputReader;
        private readonly CancellationTokenSource _internalCts;
        private Task? _processorTask;

        protected ILogger Logger { get; }

        protected ChannelConsumer(ChannelReader<TInput> inputReader,
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

                await foreach (var inputValue in _inputReader.ReadAllAsync(linkedToken))
                {
                    await ExecuteProcessorAsync(inputValue, linkedToken).ConfigureAwait(false);
                }
            }
#pragma warning disable S2737 // "catch" clauses should do more than rethrow
            catch (OperationCanceledException) when (False(() =>
                 Logger.LogWarning("Consume operation cancelled")))
            {
                throw;
            }
#pragma warning restore S2737 // "catch" clauses should do more than rethrow
            finally
            {
                Logger.LogInformation("Stopping:completing consumer channel!");
            }
        }

        public virtual async Task StopProcessingAsync(CancellationToken cancellationToken)
        {
            try
            {
                _internalCts.Cancel();
            }
            finally
            {
                if (_processorTask != null)
                {
                    await _processorTask.ConfigureAwait(false);
                    _processorTask = null;
                }
            }
        }


        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            _internalCts?.Dispose();
        }
    }
}
