using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace DetectiCam.Core.VideoCapturing
{
    public abstract class ChannelTransformer<TInput, TOutput> : IDisposable
    {
        private readonly ChannelReader<TInput> _inputReader;
        private readonly ChannelWriter<TOutput> _outputWriter;
        private readonly ILogger _logger;
        private readonly CancellationTokenSource _internalCts;

        private Task? _processorTask = null;

        protected ILogger Logger => _logger;

        public ChannelTransformer(ChannelReader<TInput> inputReader, ChannelWriter<TOutput> outputWriter,
            ILogger logger)
        {
            _inputReader = inputReader;
            _outputWriter = outputWriter;
            _logger = logger;
            _internalCts = new CancellationTokenSource();
        }

        protected abstract Task<TOutput> ExecuteTransform(TInput input, CancellationToken cancellationToken);

        public Task ExecuteProcessingAsync(CancellationToken stoppingToken)
        {
            _processorTask = Task.Run(async () =>
            {
                try
                {
                    using var cts = CancellationTokenSource.CreateLinkedTokenSource(
                        _internalCts.Token, stoppingToken);
                    var linkedToken = cts.Token;

                    await foreach(var inputValue in _inputReader.ReadAllAsync(linkedToken))
                    {
                        var outputValue = await ExecuteTransform(inputValue, linkedToken).ConfigureAwait(false);

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

        public void Dispose()
        {
            // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
            _internalCts?.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}
