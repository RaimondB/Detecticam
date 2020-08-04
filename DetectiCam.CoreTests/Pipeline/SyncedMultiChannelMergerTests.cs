using Microsoft.VisualStudio.TestTools.UnitTesting;
using DetectiCam.Core.VideoCapturing;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Moq;
using DetectiCam.Core.Pipeline;
using System.Threading.Tasks;
using System.Linq;

namespace DetectiCam.Core.VideoCapturing.Tests
{
    [TestClass()]
    public class SyncedMultiChannelMergerTests
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

            //or use this short equivalent 
            _logger = Mock.Of<ILogger>();

            _inputs = new List<ChannelReader<SyncItem>>
            {
                _firstInput.Reader,
                _secondInput.Reader
            };

            _sut = new SyncedMultiChannelMerger<SyncItem>(_inputs, _output.Writer, _logger);
        }

        [TestCleanup]
        public void Cleanup()
        {
            _cts.Dispose();
            _sut.Dispose();
        }


        [TestMethod()]
        public async Task InSyncProcessingTest()
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

            int resultCount = 0;
            await foreach (var result in _output.Reader.ReadAllAsync(_cts.Token))
            {
                Assert.AreEqual(2, result.Count);
                Assert.AreEqual(6, result.Select(x=>x.Value).Sum());
                resultCount++;
            }

            await task;
            Assert.AreEqual(5, resultCount);
        }

        [TestMethod()]
        public async Task OutOfSync_FirstHighest_ProcessingTest()
        {
            var task = _sut.ExecuteProcessingAsync(_cts.Token);

            int triggerId = 1;

            for (int x = 2; x <= 5; x++)
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

            //Expected:
            //- first result of second stream is dropped to get into sync.
            //- four results will come out that are in sync

            int resultCount = 0;
            await foreach (var result in _output.Reader.ReadAllAsync(_cts.Token))
            {
                Assert.AreEqual(2, result.Count);
                Assert.AreEqual(6, result.Select(x => x.Value).Sum());
                resultCount++;
            }

            await task;
            Assert.AreEqual(4, resultCount);
        }

        [TestMethod()]
        public async Task OutOfSync_SecondHighest_ProcessingTest()
        {
            var task = _sut.ExecuteProcessingAsync(_cts.Token);

            await WriteSequenceAsync(_firstInput, new int[] { 1, 2, 3, 4, 5 }, new int[] { 1, 2, 3, 4, 5 });
            await WriteSequenceAsync(_secondInput, new int[] { 2, 3, 4, 5 }, new int[] { 4, 3, 2, 1 });

            //Expected:
            //- first result of first stream is dropped to get into sync.
            //- four results will come out that are in sync

            int resultCount = 0;
            await foreach (var result in _output.Reader.ReadAllAsync(_cts.Token))
            {
                Assert.AreEqual(2, result.Count);
                Assert.AreEqual(6, result.Select(x => x.Value).Sum());
                resultCount++;
            }

            await task;
            Assert.AreEqual(4, resultCount);
        }

        private static async Task WriteSequenceAsync(ChannelWriter<SyncItem> input, int[] triggerIds, int[] values)
        {
            if (triggerIds.Length != values.Length) throw new ArgumentException("number of triggerIds and values must be equal");

            for(int i = 0; i < triggerIds.Length; i++)
            {
                await input.WriteAsync(new SyncItem() { TriggerId = triggerIds[i], Value = values[i] });
            }
            input.Complete();
        }

        [TestMethod()]
        public async Task OutOfSync_MiddleGap_ProcessingTest()
        {
            var task = _sut.ExecuteProcessingAsync(_cts.Token);

            await WriteSequenceAsync(_firstInput, new int[] { 1, 2, 4, 5 }, new int[] { 1, 2, 4, 5 });
            await WriteSequenceAsync(_secondInput, new int[] { 1, 2, 3, 4, 5 }, new int[] { 5, 4, 3, 2, 1 });

            //Expected:
            //- first result of first stream is dropped to get into sync.
            //- four results will come out that are in sync

            int resultCount = 0;
            await foreach (var result in _output.Reader.ReadAllAsync(_cts.Token))
            {
                Assert.AreEqual(2, result.Count);
                Assert.AreEqual(6, result.Select(x => x.Value).Sum());
                resultCount++;
            }

            await task;
            Assert.AreEqual(4, resultCount);
        }


        //[TestMethod()]
        //public void SyncedMultiChannelMergerTest()
        //{
        //    Assert.Fail();
        //}

        //[TestMethod()]
        //public void ExecuteProcessingAsyncTest()
        //{
        //    Assert.Fail();
        //}

        //[TestMethod()]
        //public void StopProcessingAsyncTest()
        //{
        //    Assert.Fail();
        //}

        //[TestMethod()]
        //public void DisposeTest()
        //{
        //    Assert.Fail();
        //}
    }
}