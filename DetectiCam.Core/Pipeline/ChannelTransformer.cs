using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace DetectiCam.Core.VideoCapturing
{
    public class ChannelTransformer<TInput,TOutput> : IDisposable
    {
        private readonly ChannelReader<TInput> _inputReader;
        private readonly ChannelWriter<TOutput> _outputWriter;
        private readonly ILogger _logger;
        private readonly CancellationTokenSource _internalCts;

        private Func<TInput, CancellationToken, Task<TOutput>>? _transformer;
        private Task? _processorTask = null;
        private bool disposedValue;

        protected ILogger Logger => _logger;

        public ChannelTransformer(ChannelReader<TInput> inputReader, ChannelWriter<TOutput> outputWriter,
            ILogger logger,
            Func<TInput, CancellationToken, Task<TOutput>>? transformer = default)
        {
            _inputReader = inputReader;
            _outputWriter = outputWriter;
            _logger = logger;
            _transformer = transformer;
            _internalCts = new CancellationTokenSource();
        }

        protected void SetTransformer(Func<TInput, CancellationToken, Task<TOutput>>? transformer)
        {
            _transformer = transformer;
        }

        public Task ExecuteProcessingAsync(CancellationToken stoppingToken)
        {
            if (_transformer is null) throw new InvalidOperationException("Transformer function not set");

            _processorTask = Task.Run(async () =>
            {
                try
                {
                    using var cts = CancellationTokenSource.CreateLinkedTokenSource(
                        _internalCts.Token, stoppingToken);
                    var linkedToken = cts.Token;

                    await foreach(var inputValue in _inputReader.ReadAllAsync(linkedToken))
                    {
                        var outputValue = await _transformer(inputValue, linkedToken).ConfigureAwait(false);

                        if (!_outputWriter.TryWrite(outputValue))
                        {
                            _logger.LogWarning("Could not write transformed result!");
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    _logger.LogWarning("Transform operation cancelled");
                    throw;
                }
                finally
                {
                    _logger.LogInformation("Stopping:completing transform channel!");
                    //Complete the channel since nothing to be read anymore
                    _outputWriter.TryComplete();
                }
            }, stoppingToken);

            return _processorTask;
        }

        public async Task StopProcessingAsync()
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
