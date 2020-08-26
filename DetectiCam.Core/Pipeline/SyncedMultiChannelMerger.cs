using DetectiCam.Core.Pipeline;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace DetectiCam.Core.VideoCapturing
{
    public class SyncedMultiChannelMerger<T> : IDisposable where T : ISyncTokenProvider
    {
        private readonly List<ChannelReader<T>?> _inputReaders;
        private readonly ChannelWriter<IList<T>> _outputWriter;
        private readonly ILogger _logger;
        //private Task? _mergeTask = null;
        private readonly CancellationTokenSource _internalCts;

        public SyncedMultiChannelMerger(IEnumerable<ChannelReader<T>> inputReaders, ChannelWriter<IList<T>> outputWriter,
            ILogger logger)
        {
            _inputReaders = new List<ChannelReader<T>?>(inputReaders);
            _outputWriter = outputWriter;
            _logger = logger;
            _internalCts = new CancellationTokenSource();
            //_internalCts.CancelAfter(2000);
        }

        public async Task ExecuteProcessingAsync(CancellationToken stoppingToken)
        {
            //_mergeTask = Task.Run(async () =>
            //{
            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(
                    _internalCts.Token, stoppingToken);
                var linkedToken = cts.Token;

                while (!linkedToken.IsCancellationRequested)
                {
                    T[] results = new T[_inputReaders.Count];

                    _logger.LogDebug("Merging frames start batch");

                    int? maxToken = null;
                    int resultCount = 0;

                    for (var pass = 0; pass <= 1; pass++)
                    {
                        resultCount = 0;
                        for (var index = 0; index < _inputReaders.Count; index++)
                        {
                            try
                            {
                                maxToken = await ReadInputAtIndex(results, index, maxToken, linkedToken).ConfigureAwait(false);
                                if (maxToken.HasValue) resultCount++;
                            }
                            catch (ChannelClosedException)
                            {
                                _logger.LogWarning("Channel closed");
                                _inputReaders[index] = null;
                            }
                        }
                    }

                    _logger.LogDebug("Merging frames end batch:{resultCount}", resultCount);

                    if (resultCount > 0)
                    {
                        //Only provide output when we have some results.

                        if (resultCount < _inputReaders.Count)
                        {
                            //Filter out empty results when we dont have all
                            results = results.Where(r => r != null).ToArray();
                        }

                        if (!_outputWriter.TryWrite(results))
                        {
                            _logger.LogWarning("Could not write merged result!");
                        }
                        else
                        {
                            _logger.LogDebug("New Merged result available for token: {triggerId}", maxToken);
                        }
                    }
                    else
                    {
                        //Stop when all channels have been closed.
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in merger");
                throw;
            }
            finally
            {
                _logger.LogInformation("Stopping:completing merge channel!");
                //Complete the channel since nothing to be read anymore
                _outputWriter.TryComplete();
            }
            //}, stoppingToken);

            //return _mergeTask;
        }

        private async ValueTask<int?> ReadInputAtIndex(IList<T> results, int index, int? maxToken, CancellationToken cancellationToken)
        {
            var curResult = results[index];

            if (curResult != null && curResult.SyncToken == maxToken)
            {
                return curResult.SyncToken;
            }
            else
            {
                var curReader = _inputReaders[index];
                if (curReader != null)
                {
                    //Read results until in sync
                    do
                    {
                        curResult = await curReader.ReadAsync(cancellationToken).ConfigureAwait(false);
                        results[index] = curResult;
                    } while (curResult.SyncToken < maxToken);
                    return curResult.SyncToken;
                }
                else
                {
                    return null;
                }
            }
        }

        public Task StopProcessingAsync()
        {
            _internalCts.Cancel();
            //if (_mergeTask != null)
            //{
            //    await _mergeTask.ConfigureAwait(false);
            //    _mergeTask = null;
            //}
            return Task.CompletedTask;
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
