using Microsoft.VisualStudio.TestTools.UnitTesting;
using DetectiCam.Core.VideoCapturing;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Channels;
using Microsoft.VisualStudio.TestTools.UnitTesting.Logging;
using Microsoft.Extensions.Logging;
using Moq;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;

namespace DetectiCam.Core.VideoCapturing.Tests
{
    [TestClass()]
    public class MultiChannelMergerTests
    {
        CancellationTokenSource _cts;

        Channel<int> _firstInput;
        Channel<int> _secondInput;
        Channel<IList<int>> _output;
        ILogger<MultiChannelMerger<int>> _logger;
        List<ChannelReader<int>> _inputs;

        MultiChannelMerger<int> _sut;

        [TestInitialize]
        public void Setup()
        {
            _cts = new CancellationTokenSource();

            _firstInput = Channel.CreateBounded<int>(5);
            _secondInput = Channel.CreateBounded<int>(5);
            _output = Channel.CreateUnbounded<IList<int>>();

            //or use this short equivalent 
            _logger = Mock.Of<ILogger<MultiChannelMerger<int>>>();

            _inputs = new List<ChannelReader<int>>
            {
                _firstInput.Reader,
                _secondInput.Reader
            };

            _sut = new MultiChannelMerger<int>(_inputs, _output.Writer, _logger);
        }

        [TestCleanup]
        public void Cleanup()
        {
            _cts.Dispose();
        }


        [TestMethod()]
        public async Task StartProcessingAsyncTest()
        {
            var task = _sut.ExecuteProcessingAsync(_cts.Token);

            for(int x = 1; x <= 5; x++)
            {
                await _firstInput.Writer.WriteAsync(x);
            }
            _firstInput.Writer.Complete();

            for (int y = 5; y >= 1; y--)
            {
                await _secondInput.Writer.WriteAsync(y);
            }
            _secondInput.Writer.Complete();

            int noResults = 0;
            await foreach(var result in _output.Reader.ReadAllAsync(_cts.Token))
            {
                Assert.AreEqual(2, result.Count);
                Assert.AreEqual(6, result.Sum());
                noResults++;
            }
            await task;
            Assert.AreEqual(5, noResults);
        }

        [TestMethod()]
        [ExpectedException(typeof(OperationCanceledException))]
        public async Task StopProcessingAsyncTest()
        {
            var task = _sut.ExecuteProcessingAsync(_cts.Token);

            var firstTask = Task.Run(async () =>
            {
                for (int x = 1; x <= 5; x++)
                {
                    await _firstInput.Writer.WriteAsync(x);
                }
                _firstInput.Writer.Complete();
            });

            var secondTask = Task.Run(async () =>
            {
                for (int y = 5; y >= 1; y--)
                {
                    await _secondInput.Writer.WriteAsync(y);
                }
                _secondInput.Writer.Complete();
            });

            await _sut.StopProcessingAsync(_cts.Token);

            if(_output.Reader.TryRead(out var result))
            {
                Assert.Fail("Read should not succeed because the channel is completed");
            }
        }

        [TestMethod()]
        [ExpectedException(typeof(OperationCanceledException))]
        public async Task StopWithUnbalancedWritesAndCancelTest()
        {
            var task = _sut.ExecuteProcessingAsync(_cts.Token);

            var firstTask = Task.Run(async () =>
            {
                for (int x = 1; x <= 5; x++)
                {
                    await _firstInput.Writer.WriteAsync(x);
                }
                _firstInput.Writer.Complete();
            });

            var secondTask = Task.Run(async () =>
            {
                for (int y = 5; y >= 2; y--)
                {
                    await _secondInput.Writer.WriteAsync(y);
                    if(y==3)
                    {
                        _cts.Cancel();
                    }
                }
                _secondInput.Writer.Complete();
            });

            await task;
            Assert.Fail("Should not be here since cancellation will throw");
        }
    }
}