using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace DetectiCam.Core.VideoCapturing
{
    public class MultiChannelMerger<T> : IDisposable
    {
        private readonly List<ChannelReader<T>?> _inputReaders;
        private readonly ChannelWriter<IList<T>> _outputWriter;
        private readonly ILogger _logger;
        private Task? _mergeTask = null;
        private readonly CancellationTokenSource _internalCts;

        public MultiChannelMerger(IEnumerable<ChannelReader<T>> inputReaders, ChannelWriter<IList<T>> outputWriter,
            ILogger logger)
        {
            _inputReaders = new List<ChannelReader<T>?>(inputReaders);
            _outputWriter = outputWriter;
            _logger = logger;
            _internalCts = new CancellationTokenSource();
        }

        public Task ExecuteProcessingAsync(CancellationToken stoppingToken)
        { 
            _mergeTask = Task.Run(async () =>
            {
                try
                {
                    using var cts = CancellationTokenSource.CreateLinkedTokenSource(
                        _internalCts.Token, stoppingToken);
                    var linkedToken = cts.Token;

                    while (true)
                    {
                        linkedToken.ThrowIfCancellationRequested();
                        List<T> results = new List<T>();

                        _logger.LogDebug("Merging frames start batch");

                        for (var index = 0; index < _inputReaders.Count; index++)
                        {
                            try
                            {
                                var curReader = _inputReaders[index];
                                if (curReader != null)
                                {
                                    var result = await curReader.ReadAsync(linkedToken).ConfigureAwait(false);
                                    results.Add(result);
                                }
                            }
                            catch (OperationCanceledException)
                            {
                                //Operation has timed out
                                _logger.LogWarning("Reading from source has timed out");
                            }
                            catch (ChannelClosedException)
                            {
                                _logger.LogWarning("Channel closed");
                                _inputReaders[index] = null;
                            }
                        }
                        _logger.LogDebug("Merging frames end batch");

                        if (results.Count == _inputReaders.Count)
                        {
                            if (!_outputWriter.TryWrite(results))
                            {
                                _logger.LogWarning("Could not write merged result!");
                            }
                            else
                            {
                                _logger.LogDebug("New Merged result available");
                            }
                        }
                        else
                        {
                            if (_inputReaders.All(r => r == null))
                            {
                                //Stop when all channels have been closed.
                                break;
                            }
                        }
                    }
                }
                finally
                {
                    _logger.LogInformation("Stopping:completing merge channel!");
                    //Complete the channel since nothing to be read anymore
                    _outputWriter.TryComplete();
                }
            }, stoppingToken);

            return _mergeTask;
        }

        public async Task StopProcessingAsync()
        {
            _internalCts.Cancel();
            if (_mergeTask != null)
            {
                await _mergeTask.ConfigureAwait(false);
                _mergeTask = null;
            }
        }

        public void Dispose()
        {
            _internalCts?.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}
