using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
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
        //private Task? _mergeTask = null;
        private CancellationTokenSource? _internalCts = null;
        private CancellationTokenSource? _linkedCts = null;
        private CancellationToken? _internalToken = null;
        private CancellationToken? _externalToken = null;
        private Task? _currentTask;

        public MultiChannelMerger(IEnumerable<ChannelReader<T>> inputReaders, ChannelWriter<IList<T>> outputWriter,
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
            try
            {
                _internalCts = new CancellationTokenSource();
                _internalToken = _internalCts.Token;
                _externalToken = cancellationToken;
                _linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
                _internalToken.Value, _externalToken.Value);

                var linkedToken = _linkedCts.Token;

                while (true)
                {
                    linkedToken.ThrowIfCancellationRequested();

                    List<T> results = new();

                    _logger.LogDebug("Merging frames start batch");

                    for (var index = 0; index < _inputReaders.Count; index++)
                    {
                        try
                        {
                            var curReader = _inputReaders[index];
                            if (curReader != null)
                            {
                                var result = await curReader.ReadAsync(_linkedCts.Token).ConfigureAwait(false);
                                results.Add(result);
                            }
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
                        await _outputWriter.WriteAsync(results, _linkedCts.Token).ConfigureAwait(false);
                        // if (!_outputWriter.TryWrite(results))
                        // {
                        //     _logger.LogWarning("Could not write merged result!");
                        // }
                        // else
                        // {
                        //     _logger.LogDebug("New Merged result available");
                        // }
                    }
                    else
                    {
                        if (_inputReaders.All(r => r == null))
                        {
                            //All Inputs are processed, so we can end processing
                            break;
                        }
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

        private void Cleanup()
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
