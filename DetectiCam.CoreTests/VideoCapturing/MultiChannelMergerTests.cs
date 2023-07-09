using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
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
    public class MultiChannelMergerTests
    {
        public class SutContext
        {
            public CancellationTokenSource _cts;

            public Channel<int> _firstInput;
            public Channel<int> _secondInput;
            public Channel<IList<int>> _output;
            public ILogger<MultiChannelMerger<int>> _logger;
            public List<ChannelReader<int>> _inputs;

            public MultiChannelMerger<int> _sut;            
        }


        private static SutContext BuildSutContext()
        {
            var sc = new SutContext();

            sc._cts = new CancellationTokenSource();

            sc._firstInput = Channel.CreateBounded<int>(5);
            sc._secondInput = Channel.CreateBounded<int>(5);
            sc._output = Channel.CreateUnbounded<IList<int>>();

            //or use this short equivalent 
            sc._logger = Mock.Of<ILogger<MultiChannelMerger<int>>>();

            // ServiceProvider serviceProvider = new ServiceCollection()
            //     .AddLogging((loggingBuilder) => loggingBuilder
            //         .SetMinimumLevel(LogLevel.Trace)
            //         .AddConsole()
            //         )
            //     .BuildServiceProvider();

            // _logger = serviceProvider.GetService<ILoggerFactory>().CreateLogger<MultiChannelMerger<int>>();

            sc._inputs = new List<ChannelReader<int>>
            {
                sc._firstInput.Reader,
                sc._secondInput.Reader
            };

            sc._sut = new MultiChannelMerger<int>(sc._inputs, sc._output.Writer, sc._logger);

            return sc;
        }

        public void Cleanup(SutContext sc)
        {
            sc._cts.Cancel(true);
            sc._cts.Dispose();
        }


        [TestMethod()]
        public async Task MultiStartProcessingAsyncTest()
        {
            var sc = BuildSutContext();

            var task = sc._sut.ExecuteProcessingAsync(sc._cts.Token);

            for (int x = 1; x <= 5; x++)
            {
                await sc._firstInput.Writer.WriteAsync(x).ConfigureAwait(false);
            }
            sc._firstInput.Writer.Complete();

            for (int y = 5; y >= 1; y--)
            {
                await sc._secondInput.Writer.WriteAsync(y).ConfigureAwait(false);
            }
            sc._secondInput.Writer.Complete();

            int nrResults = 0;
            await foreach (var result in sc._output.Reader.ReadAllAsync(sc._cts.Token)) //.ConfigureAwait(false))
            {
                Assert.AreEqual(2, result.Count);
                Assert.AreEqual(6, result.Sum());
                nrResults++;
            }
            await task.ConfigureAwait(false);
            Assert.AreEqual(5, nrResults);

            Cleanup(sc);
        }

        [TestMethod()]
        public async Task MultiStopProcessingAsyncTest()
        {
            var sc = BuildSutContext();

            Console.WriteLine("Start StopProcessingAsyncTest");
            var task = sc._sut.ExecuteProcessingAsync(sc._cts.Token);

            var firstTask = Task.Run(async () =>
            {
                for (int x = 1; x <= 5; x++)
                {
                    //Console.WriteLine($"Write {x} to first");
                    await sc._firstInput.Writer.WriteAsync(x, sc._cts.Token).ConfigureAwait(false);
                }
                sc._firstInput.Writer.Complete();
            });

            var secondTask = Task.Run(async () =>
            {
                for (int y = 5; y >= 1; y--)
                {
                    //Console.WriteLine($"Write {y} to second");
                    await sc._secondInput.Writer.WriteAsync(y, sc._cts.Token).ConfigureAwait(false);
                }
                sc._secondInput.Writer.Complete();
            });

            var outputTask = Task.Run(async () =>
            {
                for (int y = 3; y >= 1; y--)
                {
                    var output = await sc._output.Reader.ReadAsync(sc._cts.Token).ConfigureAwait(false);
                    //Console.WriteLine($"Read {output[0]},{output[1]} from output");
                }
            });

            await sc._sut.StopProcessingAsync().ConfigureAwait(false);
            
            await Task.WhenAll(task, firstTask, secondTask).ConfigureAwait(false);

            //Flush channel
            await foreach (var result in sc._output.Reader.ReadAllAsync(sc._cts.Token).ConfigureAwait(false));

//            Assert.AreEqual(2, sc._output.Reader.Count, "2 Items left in output expected");
            Assert.AreEqual(true, sc._output.Reader.Completion.IsCompleted, "Expects channel to be completed");
            // if (sc._output.Reader.TryRead(out var result))
            // {
            //     Assert.Fail("Read should not succeed because the channel is completed");
            // }

            Cleanup(sc);
        }

        [TestMethod()]
        [ExpectedException(typeof(OperationCanceledException))]
        public async Task MultiStopWithUnbalancedWritesAndExternalCancelTest()
        {
            var sc = BuildSutContext();

            var task = sc._sut.ExecuteProcessingAsync(sc._cts.Token);

            var firstTask = Task.Run(async () =>
            {
                for (int x = 1; x <= 5; x++)
                {
                    await sc._firstInput.Writer.WriteAsync(x).ConfigureAwait(false);
                }
                sc._firstInput.Writer.Complete();
            });

            var secondTask = Task.Run(async () =>
            {
                for (int y = 5; y >= 2; y--)
                {
                    await sc._secondInput.Writer.WriteAsync(y).ConfigureAwait(false);
                    if (y == 3)
                    {
                        sc._cts.Cancel();
                    }
                }
                sc._secondInput.Writer.Complete();
            });

            await Task.WhenAll(task,firstTask,secondTask).ConfigureAwait(false);
            Assert.Fail("Should not be here since cancellation will throw");

            Cleanup(sc);
        }
    }
}