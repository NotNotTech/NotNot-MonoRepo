using System.Collections.Concurrent;
using Xunit;
using Xunit.Abstractions;

namespace NotNot.Concurrency;
public partial class Debouncer
{

   public class _Tests: IDisposable
   {

      private readonly ITestOutputHelper _output;

      public _Tests(ITestOutputHelper output)
      {
         _output = output;
         __.Test.InitTest(output);
      }

      public void Dispose()
      {
         __.Test.DisposeTest();
      }

      /// <summary>
      /// make sure that debounce works as expected for a single debounceKey
      /// </summary>
      /// <returns></returns>
      [Fact]
      public async Task EventuallyOnce_BasicE2E()
      {
         var debouncer = new Debouncer { MinDelay = TimeSpan.FromMilliseconds(500) };
         var debounceKey = "testKey";
         int actionCount = 0;

         Func<Task> action = () => { actionCount++; return Task.CompletedTask; };

         var loopCount = 10;

         var debounceCount = 10;

         // Trigger action multiple times in quick succession
         for (var i = 0; i < loopCount; i++)
         {
            //even though we spam action a bunch of times, should only execute once for all these.
            List<Task> actionTries = new();
            for(var j = 0; j < debounceCount; j++)
            {
               var attempt = debouncer.EventuallyOnce(debounceKey, action);
               actionTries.Add(attempt);
            }            
            await Task.WhenAll(actionTries);
         }

         // The action should have been executed only once
         Assert.Equal(loopCount, actionCount);
      }
      [Theory]
      [InlineData(0.05,100)]
      [InlineData(1,100)]
      [InlineData(0, 100)]
      [InlineData(0, 2)]
      [InlineData(0, 1)]
      public async Task Parallel(double growMult, int keyCount)
      {
         var debouncer = new Debouncer { 
            MinDelay = TimeSpan.FromMilliseconds(500),
            ParallelGrowthMultiplier = growMult,
            MinimumParallel = 1,
         };
         int loopCount = 3;
         int debounceCount = 10;

         var actionCounts = new ConcurrentDictionary<string, int>();

         Func<string, Task> action = async (key) =>
         {  
            actionCounts.AddOrUpdate(key, 1, (key, count) => count + 1);
            await Task.Delay(50);
            //if (!actionCounts.ContainsKey(key))
            //{
            //   actionCounts[key] = 0;
            //}

            //actionCounts[key]++;
            //return new ValueTask();
         };

         List<Task> keyRuns = new();
         for (var k = 0; k < keyCount; k++)
         {
            string key = $"key_{k}";

            var run = Task.Run(async() =>
            {
               // Execute actions with different keys in parallel
               for (var i = 0; i < loopCount; i++)
               {
                  List<Task> tasks = new List<Task>();
                  for (var j = 0; j < debounceCount; j++)
                  {
                     var task = debouncer.EventuallyOnce(key, () => action(key));
                     tasks.Add(task);
                  }
                  await Task.WhenAll(tasks);
               }
            });

            keyRuns.Add(run);
         }
         await Task.WhenAll(keyRuns);

         // Each action associated with a unique key should have been executed only once
         for (var k = 0; k < keyCount; k++)
         {
            string key = $"key_{k}";
            var count = actionCounts[key];
            Assert.Equal(loopCount, count);
         }
      }




      [Fact]
      public async Task ParallelExecution_WithinLimits()
      {
         var debouncer = new Debouncer { MinimumParallel = 2 };
         var actionCount = 0;
         var lockObj = new object();

         Func<Task> action = () =>
         {
            lock (lockObj)
            {
               actionCount++;
            }
            return Task.CompletedTask;
         };

         // Execute multiple actions with different keys
         var tasks = new Task[4];
         for (int i = 0; i < 4; i++)
         {
            tasks[i] = debouncer.EventuallyOnce($"key{i}", action);
         }

         await Task.WhenAll(tasks);

         // All actions should have been executed
         Assert.Equal(4, actionCount);
      }

      [Fact]
      public async Task ActionCancellation_DebuggableExceptions()
      {
         var debouncer = new Debouncer();
         var debounceKey = "cancelKey";
         var cts = new CancellationTokenSource();

         Func<Task> action = () =>
         {
            cts.Cancel(); // Cancel during execution

            return Task.CompletedTask;
         };

         var task = debouncer.EventuallyOnce(debounceKey, action, cts.Token);

         await Assert.ThrowsAsync<OperationCanceledException>(() => task);
      }


      [Fact]
      public async Task ActionCancellation()
      {
         var debouncer = new Debouncer();
         var debounceKey = "cancelKey";
         var cts = new CancellationTokenSource();

         Func<Task> action = () =>
         {
            cts.Cancel(); // Cancel during execution

            return Task.CompletedTask;
         };

         var task = debouncer.EventuallyOnce(debounceKey, action, cts.Token);

         await Assert.ThrowsAsync<OperationCanceledException>(() => task);
      }


      [Fact]
      public async Task DynamicParallelSlots_Adjustment()
      {
         var debouncer = new Debouncer { MinimumParallel = 1, ParallelGrowthMultiplier = 0.1 };
         var actionCount = 0;
         var lockObj = new object();

         Func<Task> action = () =>
         {
            lock (lockObj)
            {
               actionCount++;
            }

            return Task.CompletedTask;
         };

         // Execute multiple actions to trigger dynamic adjustment
         var tasks = new Task[10];
         for (int i = 0; i < 10; i++)
         {
            tasks[i] = debouncer.EventuallyOnce($"key{i}", action);
         }

         await Task.WhenAll(tasks);

         // All actions should have been executed
         Assert.Equal(10, actionCount);
      }

   }
}
