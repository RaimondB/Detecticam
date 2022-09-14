using Microsoft.Extensions.Logging;
using System;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using static DetectiCam.Core.Common.ExceptionFilterUtility;

namespace DetectiCam.Core.VideoCapturing
{
    public abstract class ChannelTransformer<TInput, TOutput> : IDisposable
    {
        private readonly ChannelReader<TInput> _inputReader;
        private readonly ChannelWriter<TOutput> _outputWriter;
        private readonly ILogger _logger;
        private readonly CancellationTokenSource _internalCts;

        private Task? _processorTask;

        protected ILogger Logger => _logger;

        protected ChannelTransformer(ChannelReader<TInput> inputReader, ChannelWriter<TOutput> outputWriter,
            ILogger logger)
        {
            _inputReader = inputReader;
            _outputWriter = outputWriter;
            _logger = logger;
            _internalCts = new CancellationTokenSource();
        }

        protected abstract ValueTask<TOutput> ExecuteTransform(TInput input, CancellationToken cancellationToken);

        public Task ExecuteProcessingAsync(CancellationToken stoppingToken)
        {
            _processorTask = Task.Run(async () =>
            {
                try
                {
                    using var cts = CancellationTokenSource.CreateLinkedTokenSource(
                        _internalCts.Token, stoppingToken);
                    var linkedToken = cts.Token;

                    await foreach (var inputValue in _inputReader.ReadAllAsync(linkedToken))
                    {
                        var outputValue = await ExecuteTransform(inputValue, linkedToken).ConfigureAwait(false);

                        if (!_outputWriter.TryWrite(outputValue))
                        {
                            _logger.LogWarning("Could not write transformed result!");
                        }
                    }
                }
                catch (OperationCanceledException) when (False(() =>
                    Logger.LogWarning("Transform operation cancelled")))
                {
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
