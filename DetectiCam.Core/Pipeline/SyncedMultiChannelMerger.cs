using DetectiCam.Core.Pipeline;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Diagnostics;
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
        private CancellationTokenSource? _internalCts = null;
        private CancellationTokenSource? _linkedCts = null;
        private CancellationToken? _internalToken = null;
        private CancellationToken? _externalToken = null;
        private Task? _currentTask;
        private bool _isCleaned = true;

        public SyncedMultiChannelMerger(IEnumerable<ChannelReader<T>> inputReaders, ChannelWriter<IList<T>> outputWriter,
            ILogger logger)
        {
            _inputReaders = new List<ChannelReader<T>?>(inputReaders);
            _outputWriter = outputWriter;
            _logger = logger;
        }

                /// <summary>
        /// Starts processing the inputchannels, merging them into a single output channel
        /// </summary>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        /// <remarks>Will throw an OperationCancelledException when cancelled with the token</remarks>
        public Task ExecuteProcessingAsync(CancellationToken stoppingToken)
        {
            _currentTask = ExecuteProcessingInternalAsync(stoppingToken);
            return _currentTask;
        }

        /// <summary>
        /// Starts processing the inputchannels, merging them into a single output channel
        /// </summary>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        /// <remarks>Will throw an OperationCancelledException when cancelled with the token</remarks>
        private async Task ExecuteProcessingInternalAsync(CancellationToken cancellationToken)
        {
            //TODO: validate performance difference of using Task.Run or not.
            //_mergeTask = Task.Run(async () =>
            //{
            try
            {
                Initialize(cancellationToken);

                var linkedToken = _linkedCts.Token;

                while (true)
                {
                    linkedToken.ThrowIfCancellationRequested();

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
                                maxToken = await ReadInputAtIndex(results, index, maxToken, _linkedCts.Token).ConfigureAwait(false);
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

                        await _outputWriter.WriteAsync(results, _linkedCts.Token).ConfigureAwait(false);
                        // if (!_outputWriter.TryWrite(results))
                        // {
                        //     _logger.LogWarning("Could not write merged result!");
                        // }
                        // else
                        // {
                        //     _logger.LogDebug("New Merged result available for token: {triggerId}", maxToken);
                        // }
                    }
                    else
                    {
                        //Stop when all channels have been closed.
                        break;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                if (_internalToken!.Value.IsCancellationRequested)
                {
                    //Operation has timed out
                    _logger.LogWarning("Reading from source was cancelled explicitly");
                }
                else if (_externalToken!.Value.IsCancellationRequested)
                {
                    _logger.LogWarning("Reading from source was cancelled externally");
                    cancellationToken.ThrowIfCancellationRequested();
                }
            }
            finally
            {
                Cleanup();
            }
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

        private void Initialize(CancellationToken cancellationToken)
        {
            if(!_isCleaned)
            {
                Cleanup();
            }
            _internalCts = new CancellationTokenSource();
            _internalToken = _internalCts.Token;
            _externalToken = cancellationToken;
            _linkedCts = CancellationTokenSource.CreateLinkedTokenSource(_internalToken.Value, _externalToken.Value);
            _isCleaned = false;
        }

       private void Cleanup()
        {
            if(!_isCleaned)
            {
                _logger.LogDebug("Stopping:completing merge channel!");
                //Complete the channel since nothing to be read anymore
                if (!_outputWriter.TryComplete())
                {
                    _logger.LogWarning("Could not complete output channel!");
                }
                _internalCts?.Dispose();
                _internalCts = null;
                _linkedCts?.Dispose();
                _linkedCts = null;
                _isCleaned = true;
            }
        }

        /// <summary>
        /// Stops processing as requested (does not throw an exception) 
        /// </summary>
        /// <returns></returns>
        public async Task StopProcessingAsync()
        {
             if(_currentTask is not null)
            {
                _internalCts?.Cancel();
                await _currentTask.ConfigureAwait(false);
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            Cleanup();
        }
    }
}
