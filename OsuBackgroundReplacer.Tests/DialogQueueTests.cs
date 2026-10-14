using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using OsuBackgroundReplacerMain.Services;
using Xunit;

namespace OsuBackgroundReplacer.Tests
{
    public class DialogQueueTests
    {
        [Fact]
        public async Task EnqueueAsync_ExecutesDialogsSequentially()
        {
            var queue = new DialogQueue();
            int concurrentCount = 0;
            int maxConcurrentObserved = 0;
            var executionOrder = new List<int>();
            var lockObj = new object();

            async Task<int> RunDialog(int id)
            {
                int current = Interlocked.Increment(ref concurrentCount);
                lock (lockObj)
                {
                    if (current > maxConcurrentObserved)
                    {
                        maxConcurrentObserved = current;
                    }
                }

                await Task.Delay(30);

                lock (lockObj)
                {
                    executionOrder.Add(id);
                }

                Interlocked.Decrement(ref concurrentCount);
                return id;
            }

            var task1 = Task.Run(() => queue.EnqueueAsync(() => RunDialog(1)));
            var task2 = Task.Run(() => queue.EnqueueAsync(() => RunDialog(2)));
            var task3 = Task.Run(() => queue.EnqueueAsync(() => RunDialog(3)));

            var results = await Task.WhenAll(task1, task2, task3);

            Assert.Equal(1, maxConcurrentObserved);
            Assert.Contains(1, results);
            Assert.Contains(2, results);
            Assert.Contains(3, results);
            Assert.Equal(3, executionOrder.Count);
        }

        [Fact]
        public async Task EnqueueAsync_ConsecutiveDialogsInSequence_ExecuteSuccessfully()
        {
            var queue = new DialogQueue();

            // Simulate Replace and Undo flows where dialogs are shown one after another sequentially
            var result1 = await queue.EnqueueAsync(async () =>
            {
                await Task.Delay(10);
                return "Confirm";
            });

            var result2 = await queue.EnqueueAsync(async () =>
            {
                await Task.Delay(10);
                return "Warning";
            });

            var result3 = await queue.EnqueueAsync(async () =>
            {
                await Task.Delay(10);
                return "Summary";
            });

            Assert.Equal("Confirm", result1);
            Assert.Equal("Warning", result2);
            Assert.Equal("Summary", result3);
        }

        [Fact]
        public async Task EnqueueAsync_WhenDialogActionThrows_ReleasesSemaphoreAndPropagatesException()
        {
            var queue = new DialogQueue();

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                queue.EnqueueAsync<string>(() => throw new InvalidOperationException("Dialog failed"))
            );

            Assert.Equal("Dialog failed", ex.Message);

            // Verify queue is not stuck and can process subsequent dialogs
            var nextResult = await queue.EnqueueAsync(async () =>
            {
                await Task.Yield();
                return "Recovered";
            });

            Assert.Equal("Recovered", nextResult);
        }

        [Fact]
        public async Task EnqueueAsync_NullAction_ThrowsArgumentNullException()
        {
            var queue = new DialogQueue();
            await Assert.ThrowsAsync<ArgumentNullException>(() => queue.EnqueueAsync<int>(null!));
        }
    }
}
