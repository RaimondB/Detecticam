using DetectiCam.Core.Pipeline;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace DetectiCam.Core.VideoCapturing.Tests
{
    [TestClass()]
    public class SyncedMultiChannelMergerLifecycleTests
    {
        public class SyncItem : ISyncTokenProvider
        {
            public int Value { get; set; }
            public int? TriggerId { get; set; }

            public int? SyncToken => TriggerId;
        }

        CancellationTokenSource _cts;

        Channel<SyncItem> _firstInput;
        Channel<SyncItem> _secondInput;
        Channel<IList<SyncItem>> _output;
        ILogger _logger;
        List<ChannelReader<SyncItem>> _inputs;

        SyncedMultiChannelMerger<SyncItem> _sut;

        [TestInitialize]
        public void Setup()
        {
            _cts = new CancellationTokenSource();

            _firstInput = Channel.CreateBounded<SyncItem>(5);
            _secondInput = Channel.CreateBounded<SyncItem>(5);
            _output = Channel.CreateUnbounded<IList<SyncItem>>();

            _logger = Mock.Of<ILogger<MultiChannelMerger<SyncItem>>>();

            _inputs = new List<ChannelReader<SyncItem>>
            {
                _firstInput.Reader,
                _secondInput.Reader
            };

            _sut = new  SyncedMultiChannelMerger<SyncItem>(_inputs, _output.Writer, _logger);
        }

        [TestCleanup]
        public void Cleanup()
        {
            _cts.Cancel(true);
            _cts.Dispose();
        }


        [TestMethod()]
        public async Task SyncedMultiStartProcessingAsyncTest()
        {
            var task = _sut.ExecuteProcessingAsync(_cts.Token);


            int triggerId = 0;

            for (int x = 1; x <= 5; x++)
            {
                triggerId++;
                await _firstInput.Writer.WriteAsync(new SyncItem() { TriggerId = triggerId, Value = x });
            }
            _firstInput.Writer.Complete();

            triggerId = 0;
            for (int y = 5; y >= 1; y--)
            {
                triggerId++;
                await _secondInput.Writer.WriteAsync(new SyncItem() { TriggerId = triggerId, Value = y });
            }
            _secondInput.Writer.Complete();

            int nrResults = 0;
            await foreach (var result in _output.Reader.ReadAllAsync(_cts.Token).ConfigureAwait(false))
            {
                Assert.AreEqual(2, result.Count);
                Assert.AreEqual(6, result.Select(i => i.Value).Sum());
                nrResults++;
            }
            await task.ConfigureAwait(false);
            Assert.AreEqual(5, nrResults);
        }

        [TestMethod()]
        public async Task SyncedMultiStopProcessingAsyncTest()
        {
            Console.WriteLine("Start StopProcessingAsyncTest");
            var task = _sut.ExecuteProcessingAsync(_cts.Token);

            var firstTask = Task.Run(async () =>
            {
                int triggerId = 0;

                for (int x = 1; x <= 5; x++)
                {
                    triggerId++;
                    await _firstInput.Writer.WriteAsync(new SyncItem() { TriggerId = triggerId, Value = x });
                }
                _firstInput.Writer.Complete();
            });

            var secondTask = Task.Run(async () =>
            {
                int triggerId = 0;
                for (int y = 5; y >= 1; y--)
                {
                    triggerId++;
                    await _secondInput.Writer.WriteAsync(new SyncItem() { TriggerId = triggerId, Value = y });
                }
                _secondInput.Writer.Complete();
            });

            Task stopResult = Task.CompletedTask;
            var outputTask = Task.Run(async () =>
            {
                for (int y = 5; y >= 1; y--)
                {
                    var output = await _output.Reader.ReadAsync(_cts.Token).ConfigureAwait(false);
                    //Console.WriteLine($"Read {output[0]},{output[1]} from output");

                    if(y==3)
                    {
                        stopResult = _sut.StopProcessingAsync();
                    }
                }
            });


          
            await Task.WhenAll(task, firstTask, secondTask, stopResult).ConfigureAwait(false);

            //Flush channel
            await foreach (var result in _output.Reader.ReadAllAsync(_cts.Token).ConfigureAwait(false));

            Assert.AreEqual(true, _output.Reader.Completion.IsCompleted, "Expects channel to be completed");

            // if (_output.Reader.TryRead(out var result))
            // {
            //     Assert.Fail("Read should not succeed because the channel is completed");
            // }
        }

        [TestMethod()]
        [ExpectedException(typeof(OperationCanceledException))]
        public async Task SyncedMultiStopWithUnbalancedWritesAndExternalCancelTest()
        {
            var task = _sut.ExecuteProcessingAsync(_cts.Token);

            var firstTask = Task.Run(async () =>
            {
                int triggerId = 0;

                for (int x = 1; x <= 5; x++)
                {
                    triggerId++;
                    await _firstInput.Writer.WriteAsync(new SyncItem() { TriggerId = triggerId, Value = x });
                }
                _firstInput.Writer.Complete();

            });

            var secondTask = Task.Run(async () =>
            {
                int triggerId = 0;
                for (int y = 5; y >= 1; y--)
                {
                    triggerId++;
                    if (y == 3)
                    {
                        _cts.Cancel();
                    }
                    await _secondInput.Writer.WriteAsync(new SyncItem() { TriggerId = triggerId, Value = y });
                }
                _secondInput.Writer.Complete();
            });

            await Task.WhenAll(task,firstTask,secondTask).ConfigureAwait(false);
            Assert.Fail("Should not be here since cancellation will throw");
        }
 
    }
}