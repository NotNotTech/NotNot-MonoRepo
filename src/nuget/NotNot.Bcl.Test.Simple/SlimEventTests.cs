using Xunit;

namespace NotNot.Bcl.Test.Simple;

/// <summary>
/// Tests for SlimEvent strong-reference event handlers.
/// Covers basic functionality, thread safety, recipient disposal detection, and edge cases.
/// </summary>
public class SlimEventTests
{
   #region Test Helper Classes

   /// <summary>
   /// Test sender class implementing IDisposeGuard
   /// </summary>
   private class TestSender : IDisposeGuard
   {
      public bool IsDisposed { get; private set; }
      public string Name { get; set; }

      public TestSender(string name = "sender")
      {
         Name = name;
      }

      public void Dispose()
      {
         __.AssertIfNot(!IsDisposed, "Double dispose detected");
         IsDisposed = true;
      }
   }

   /// <summary>
   /// Test recipient class implementing IDisposeGuard with event handlers
   /// </summary>
   private class TestRecipient : IDisposeGuard
   {
      public bool IsDisposed { get; private set; }
      public string Name { get; set; }
      public int CallCount; // Field for Interlocked.Increment
      public TestSender? LastSender { get; set; }
      public string? ReceivedString { get; set; }
      public int ReceivedInt { get; set; }
      public TestArgs ReceivedTestArgs { get; set; }
      public TestRecipient? ReceivedRecipient { get; set; }

      // For self-modifying handler tests
      public SlimEvent<TestSender>? EventToModify { get; set; }
      public TestRecipient? OtherRecipientToSubscribe { get; set; }

      public TestRecipient(string name = "recipient")
      {
         Name = name;
      }

      public void OnEvent(TestSender sender)
      {
         Interlocked.Increment(ref CallCount);
         LastSender = sender;
      }

      public void OnEventWithString(TestSender sender, string args)
      {
         Interlocked.Increment(ref CallCount);
         LastSender = sender;
         ReceivedString = args;
      }

      public void OnEventWithInt(TestSender sender, int value)
      {
         Interlocked.Increment(ref CallCount);
         ReceivedInt = value;
      }

      public void OnEventWithTestArgs(TestSender sender, TestArgs args)
      {
         Interlocked.Increment(ref CallCount);
         ReceivedTestArgs = args;
      }

      public void OnEventWithRecipient(TestSender sender, TestRecipient recipient)
      {
         Interlocked.Increment(ref CallCount);
         ReceivedRecipient = recipient;
      }

      public void OnEventThrows(TestSender sender)
      {
         Interlocked.Increment(ref CallCount);
         throw new InvalidOperationException("Test exception");
      }

      public void OnEventAndUnsubscribeSelf(TestSender sender)
      {
         Interlocked.Increment(ref CallCount);
         if (EventToModify != null)
         {
            EventToModify.Handler -= OnEventAndUnsubscribeSelf;
         }
      }

      public void OnEventAndSubscribeOther(TestSender sender)
      {
         Interlocked.Increment(ref CallCount);
         if (EventToModify != null && OtherRecipientToSubscribe != null)
         {
            EventToModify.Handler += OtherRecipientToSubscribe.OnEvent;
         }
      }

      public void Dispose()
      {
         __.AssertIfNot(!IsDisposed, "Double dispose detected");
         IsDisposed = true;
      }
   }

   /// <summary>
   /// Test args struct
   /// </summary>
   private record struct TestArgs(int Value, string Text);

   #endregion

   #region SlimEvent<TSender> Basic Tests

   [Fact]
   public void SlimEvent_BasicSubscribeInvoke_Works()
   {
      // Arrange
      var slimEvent = new SlimEvent<TestSender>();
      var sender = new TestSender("test");
      var recipient = new TestRecipient();

      slimEvent.Handler += recipient.OnEvent;

      // Act
      slimEvent.Raise(sender);

      // Assert
      Assert.Equal(1, recipient.CallCount);

      // Cleanup
      sender.Dispose();
      recipient.Dispose();
   }

   [Fact]
   public void SlimEvent_MultipleSubscribers_AllInvoked()
   {
      // Arrange
      var slimEvent = new SlimEvent<TestSender>();
      var sender = new TestSender();
      var recipient1 = new TestRecipient("r1");
      var recipient2 = new TestRecipient("r2");
      var recipient3 = new TestRecipient("r3");

      slimEvent.Handler += recipient1.OnEvent;
      slimEvent.Handler += recipient2.OnEvent;
      slimEvent.Handler += recipient3.OnEvent;

      // Act
      slimEvent.Raise(sender);

      // Assert
      Assert.Equal(1, recipient1.CallCount);
      Assert.Equal(1, recipient2.CallCount);
      Assert.Equal(1, recipient3.CallCount);

      // Cleanup
      sender.Dispose();
      recipient1.Dispose();
      recipient2.Dispose();
      recipient3.Dispose();
   }

   [Fact]
   public void SlimEvent_Unsubscribe_RemovesHandler()
   {
      // Arrange
      var slimEvent = new SlimEvent<TestSender>();
      var sender = new TestSender();
      var recipient = new TestRecipient();

      slimEvent.Handler += recipient.OnEvent;
      slimEvent.Raise(sender);
      Assert.Equal(1, recipient.CallCount);

      // Act
      slimEvent.Handler -= recipient.OnEvent;
      slimEvent.Raise(sender);

      // Assert
      Assert.Equal(1, recipient.CallCount); // Still 1 - handler was removed

      // Cleanup
      sender.Dispose();
      recipient.Dispose();
   }

   [Fact]
   public void SlimEvent_Clear_RemovesAllHandlers()
   {
      // Arrange
      var slimEvent = new SlimEvent<TestSender>();
      var sender = new TestSender();
      var recipient1 = new TestRecipient("r1");
      var recipient2 = new TestRecipient("r2");
      var recipient3 = new TestRecipient("r3");

      slimEvent.Handler += recipient1.OnEvent;
      slimEvent.Handler += recipient2.OnEvent;
      slimEvent.Handler += recipient3.OnEvent;

      // Act
      slimEvent.Clear();
      slimEvent.Raise(sender);

      // Assert
      Assert.Equal(0, recipient1.CallCount);
      Assert.Equal(0, recipient2.CallCount);
      Assert.Equal(0, recipient3.CallCount);

      // Cleanup
      sender.Dispose();
      recipient1.Dispose();
      recipient2.Dispose();
      recipient3.Dispose();
   }

   [Fact]
   public void SlimEvent_EmptyHandlerList_DoesNotThrow()
   {
      // Arrange
      var slimEvent = new SlimEvent<TestSender>();
      var sender = new TestSender();

      // Act & Assert
      slimEvent.Raise(sender); // Should not throw

      // Cleanup
      sender.Dispose();
   }

   [Fact]
   public void SlimEvent_InstanceMethodHandler_Invoked()
   {
      // Arrange
      var slimEvent = new SlimEvent<TestSender>();
      var sender = new TestSender();
      var recipient = new TestRecipient();

      slimEvent.Handler += recipient.OnEvent;

      // Act
      slimEvent.Raise(sender);

      // Assert
      Assert.Equal(1, recipient.CallCount);
      Assert.Same(sender, recipient.LastSender);

      // Cleanup
      sender.Dispose();
      recipient.Dispose();
   }

   #endregion

   #region SlimEvent<TSender, TArgs> Basic Tests

   [Fact]
   public void SlimEventWithArgs_BasicSubscribeInvoke_Works()
   {
      // Arrange
      var slimEvent = new SlimEvent<TestSender, string>();
      var sender = new TestSender("test");
      var recipient = new TestRecipient();

      slimEvent.Handler += recipient.OnEventWithString;

      // Act
      slimEvent.Raise(sender, "hello");

      // Assert
      Assert.Equal("hello", recipient.ReceivedString);
      Assert.Equal(1, recipient.CallCount);

      // Cleanup
      sender.Dispose();
      recipient.Dispose();
   }

   [Fact]
   public void SlimEventWithArgs_IntArg_Works()
   {
      // Arrange
      var slimEvent = new SlimEvent<TestSender, int>();
      var sender = new TestSender();
      var recipient = new TestRecipient();

      slimEvent.Handler += recipient.OnEventWithInt;

      // Act
      slimEvent.Raise(sender, 42);

      // Assert
      Assert.Equal(42, recipient.ReceivedInt);
      Assert.Equal(1, recipient.CallCount);

      // Cleanup
      sender.Dispose();
      recipient.Dispose();
   }

   [Fact]
   public void SlimEventWithArgs_StructArg_Works()
   {
      // Arrange
      var slimEvent = new SlimEvent<TestSender, TestArgs>();
      var sender = new TestSender();
      var recipient = new TestRecipient();

      slimEvent.Handler += recipient.OnEventWithTestArgs;

      // Act
      var testArgs = new TestArgs(123, "test");
      slimEvent.Raise(sender, testArgs);

      // Assert
      Assert.Equal(123, recipient.ReceivedTestArgs.Value);
      Assert.Equal("test", recipient.ReceivedTestArgs.Text);
      Assert.Equal(1, recipient.CallCount);

      // Cleanup
      sender.Dispose();
      recipient.Dispose();
   }

   [Fact]
   public void SlimEventWithArgs_CustomClassArg_Works()
   {
      // Arrange
      var slimEvent = new SlimEvent<TestSender, TestRecipient>();
      var sender = new TestSender();
      var recipient = new TestRecipient();
      var testRecipient = new TestRecipient("arg");

      slimEvent.Handler += recipient.OnEventWithRecipient;

      // Act
      slimEvent.Raise(sender, testRecipient);

      // Assert
      Assert.Same(testRecipient, recipient.ReceivedRecipient);
      Assert.Equal(1, recipient.CallCount);

      // Cleanup
      sender.Dispose();
      recipient.Dispose();
      testRecipient.Dispose();
   }

   [Fact]
   public void SlimEventWithArgs_InstanceMethodHandler_Invoked()
   {
      // Arrange
      var slimEvent = new SlimEvent<TestSender, string>();
      var sender = new TestSender();
      var recipient = new TestRecipient();

      slimEvent.Handler += recipient.OnEventWithString;

      // Act
      slimEvent.Raise(sender, "test");

      // Assert
      Assert.Equal(1, recipient.CallCount);
      Assert.Same(sender, recipient.LastSender);

      // Cleanup
      sender.Dispose();
      recipient.Dispose();
   }

   #endregion

   #region Disposed Recipient Detection Tests (CRITICAL)

   [Fact]
   public void SlimEvent_DisposedRecipient_DetectedInDebugBuild()
   {
#if DEBUG
		// Arrange
		var slimEvent = new SlimEvent<TestSender>();
		var sender = new TestSender();
		var recipient = new TestRecipient();

		slimEvent.Handler += recipient.OnEvent;

		// Dispose recipient (memory leak scenario)
		recipient.Dispose();

		// Act & Assert
		// In DEBUG builds, assertion should fire
		var exception = Record.Exception(() => slimEvent.Raise(sender));

		// Assertion fires, exception thrown
		Assert.NotNull(exception);

		// Cleanup
		sender.Dispose();
#endif
   }

   [Fact]
   public void SlimEvent_DisposedRecipient_AutoRemoved()
   {
      // Arrange
      var slimEvent = new SlimEvent<TestSender>();
      var sender = new TestSender();
      var recipient1 = new TestRecipient("r1");
      var recipient2 = new TestRecipient("r2");

      slimEvent.Handler += recipient1.OnEvent;
      slimEvent.Handler += recipient2.OnEvent;

      // First invoke - both should be called
      slimEvent.Raise(sender);
      Assert.Equal(1, recipient1.CallCount);
      Assert.Equal(1, recipient2.CallCount);

      // Dispose first recipient
      recipient1.Dispose();

      // Act - invoke (will detect and remove disposed recipient)
#if DEBUG
		// In DEBUG builds, assertion fires and stops invocation
		var exception = Record.Exception(() => slimEvent.Raise(sender));
		Assert.NotNull(exception); // Assertion throws - remaining handlers not invoked

		// Handler was auto-removed before assertion, so third invoke works
		slimEvent.Raise(sender);
#else
      // In RELEASE builds, just skips disposed recipient
      slimEvent.Raise(sender);
#endif

      // Assert - recipient1 still at 1, recipient2 now at 2
      Assert.Equal(1, recipient1.CallCount); // Not called because disposed
      Assert.Equal(2, recipient2.CallCount); // Called on second (RELEASE) or third (DEBUG) invoke

      // Cleanup
      sender.Dispose();
      recipient2.Dispose();
   }

   [Fact]
   public void SlimEventWithArgs_DisposedRecipient_DetectedInDebugBuild()
   {
#if DEBUG
		// Arrange
		var slimEvent = new SlimEvent<TestSender, string>();
		var sender = new TestSender();
		var recipient = new TestRecipient();

		slimEvent.Handler += recipient.OnEventWithString;

		// Dispose recipient (memory leak scenario)
		recipient.Dispose();

		// Act & Assert
		var exception = Record.Exception(() => slimEvent.Raise(sender, "test"));

		// Assertion fires in DEBUG
		Assert.NotNull(exception);

		// Cleanup
		sender.Dispose();
#endif
   }

   [Fact]
   public void SlimEvent_MultipleDisposedRecipients_AllDetected()
   {
      // Arrange
      var slimEvent = new SlimEvent<TestSender>();
      var sender = new TestSender();
      var recipient1 = new TestRecipient("r1");
      var recipient2 = new TestRecipient("r2");
      var recipient3 = new TestRecipient("r3");
      var liveRecipient = new TestRecipient("live");

      slimEvent.Handler += recipient1.OnEvent;
      slimEvent.Handler += recipient2.OnEvent;
      slimEvent.Handler += liveRecipient.OnEvent;
      slimEvent.Handler += recipient3.OnEvent;

      // First invoke - all called
      slimEvent.Raise(sender);
      Assert.Equal(1, recipient1.CallCount);
      Assert.Equal(1, recipient2.CallCount);
      Assert.Equal(1, liveRecipient.CallCount);
      Assert.Equal(1, recipient3.CallCount);

      // Dispose some recipients
      recipient1.Dispose();
      recipient2.Dispose();
      recipient3.Dispose();

      // Act - invoke (will detect and remove disposed recipients)
#if DEBUG
		// In DEBUG builds, each invoke removes ONE disposed recipient and throws
		// Need multiple invokes to clean all 3 disposed recipients
		var exception1 = Record.Exception(() => slimEvent.Raise(sender));
		Assert.NotNull(exception1); // Removed recipient1, threw

		var exception2 = Record.Exception(() => slimEvent.Raise(sender));
		Assert.NotNull(exception2); // Removed recipient2, threw

		var exception3 = Record.Exception(() => slimEvent.Raise(sender));
		Assert.NotNull(exception3); // Removed recipient3, threw

		// Fourth invoke - all disposed handlers removed, only liveRecipient left
		slimEvent.Raise(sender);
#else
      // In RELEASE builds, skips all disposed recipients in one invoke
      slimEvent.Raise(sender);
#endif

      // Assert - disposed recipients still at 1, live recipient called during cleanup
      Assert.Equal(1, recipient1.CallCount);
      Assert.Equal(1, recipient2.CallCount);
#if DEBUG
		Assert.Equal(3, liveRecipient.CallCount); // Called 1 (first) + 1 (fourth invoke) + 1 (fifth invoke) = 3
#else
      Assert.Equal(2, liveRecipient.CallCount); // Called 1 (first) + 1 (second invoke) = 2
#endif
      Assert.Equal(1, recipient3.CallCount);

      // Cleanup
      sender.Dispose();
      liveRecipient.Dispose();
   }

   #endregion

   #region Thread Safety Tests

   [Fact]
   public void SlimEvent_ConcurrentSubscription_ThreadSafe()
   {
      // Arrange
      var slimEvent = new SlimEvent<TestSender>();
      var sender = new TestSender();
      const int threadCount = 10;
      const int subscriptionsPerThread = 100;
      var recipients = new List<TestRecipient>();

      // Create recipients
      for (int i = 0; i < threadCount * subscriptionsPerThread; i++)
      {
         recipients.Add(new TestRecipient($"r{i}"));
      }

      // Act - concurrent subscription
      Parallel.For(0, threadCount, threadIndex =>
      {
         for (int i = 0; i < subscriptionsPerThread; i++)
         {
            var recipientIndex = threadIndex * subscriptionsPerThread + i;
            slimEvent.Handler += recipients[recipientIndex].OnEvent;
         }
      });

      slimEvent.Raise(sender);

      // Assert
      var totalCalls = recipients.Sum(r => r.CallCount);
      Assert.Equal(threadCount * subscriptionsPerThread, totalCalls);

      // Cleanup
      sender.Dispose();
      foreach (var r in recipients) r.Dispose();
   }

   [Fact]
   public void SlimEvent_ConcurrentInvocation_ThreadSafe()
   {
      // Arrange
      var slimEvent = new SlimEvent<TestSender>();
      var sender = new TestSender();
      var recipient = new TestRecipient();

      slimEvent.Handler += recipient.OnEvent;

      // Act - concurrent invocation
      const int invocations = 1000;
      Parallel.For(0, invocations, _ =>
      {
         slimEvent.Raise(sender);
      });

      // Assert
      Assert.Equal(invocations, recipient.CallCount);

      // Cleanup
      sender.Dispose();
      recipient.Dispose();
   }

   [Fact]
   public void SlimEvent_ConcurrentSubscribeUnsubscribeInvoke_ThreadSafe()
   {
      // Arrange
      var slimEvent = new SlimEvent<TestSender>();
      var sender = new TestSender();
      var recipients = new List<TestRecipient>();

      // Create recipients
      for (int i = 0; i < 100; i++)
      {
         recipients.Add(new TestRecipient($"r{i}"));
      }

      // Act - concurrent operations
      Parallel.Invoke(
         // Subscribe
         () =>
         {
            foreach (var recipient in recipients)
            {
               slimEvent.Handler += recipient.OnEvent;
            }
         },
         // Unsubscribe
         () =>
         {
            Thread.Sleep(5); // Let some subscribe first
            foreach (var recipient in recipients.Take(50))
            {
               slimEvent.Handler -= recipient.OnEvent;
            }
         },
         // Invoke
         () =>
         {
            for (int i = 0; i < 100; i++)
            {
               slimEvent.Raise(sender);
               Thread.Sleep(1);
            }
         }
      );

      // Assert - no exceptions thrown, operations completed
      var totalCalls = recipients.Sum(r => r.CallCount);
      Assert.True(totalCalls > 0); // Some handlers were invoked

      // Cleanup
      sender.Dispose();
      foreach (var r in recipients) r.Dispose();
   }

   #endregion

   #region Edge Cases

   [Fact]
   public void SlimEvent_HandlerThrowsException_BubblesAndStopsInvocation()
   {
      // Arrange
      var slimEvent = new SlimEvent<TestSender>();
      var sender = new TestSender();
      var recipient1 = new TestRecipient("r1");
      var recipient2 = new TestRecipient("r2");
      var recipient3 = new TestRecipient("r3");

      slimEvent.Handler += recipient1.OnEvent;
      slimEvent.Handler += recipient2.OnEventThrows; // Throws exception
      slimEvent.Handler += recipient3.OnEvent; // Should not be called

      // Act & Assert
      Assert.Throws<InvalidOperationException>(() => slimEvent.Raise(sender));

      Assert.Equal(1, recipient1.CallCount); // First handler called
      Assert.Equal(1, recipient2.CallCount); // Second handler called (then threw)
      Assert.Equal(0, recipient3.CallCount); // Third handler not called - exception stopped invocation

      // Cleanup
      sender.Dispose();
      recipient1.Dispose();
      recipient2.Dispose();
      recipient3.Dispose();
   }

   [Fact]
   public void SlimEvent_HandlerUnsubscribesSelfDuringInvocation_Safe()
   {
      // Arrange
      var slimEvent = new SlimEvent<TestSender>();
      var sender = new TestSender();
      var recipient = new TestRecipient();
      recipient.EventToModify = slimEvent;

      slimEvent.Handler += recipient.OnEventAndUnsubscribeSelf;

      // Act
      slimEvent.Raise(sender);
      slimEvent.Raise(sender); // Second invoke - handler removed

      // Assert
      Assert.Equal(1, recipient.CallCount); // Only called once

      // Cleanup
      sender.Dispose();
      recipient.Dispose();
   }

   [Fact]
   public void SlimEvent_HandlerSubscribesNewHandlerDuringInvocation_NewHandlerNotInvokedImmediately()
   {
      // Arrange
      var slimEvent = new SlimEvent<TestSender>();
      var sender = new TestSender();
      var recipient1 = new TestRecipient("r1");
      var recipient2 = new TestRecipient("r2");

      recipient1.EventToModify = slimEvent;
      recipient1.OtherRecipientToSubscribe = recipient2;

      slimEvent.Handler += recipient1.OnEventAndSubscribeOther;

      // Act
      slimEvent.Raise(sender); // First handler subscribes second handler

      // Assert
      Assert.Equal(1, recipient1.CallCount);
      Assert.Equal(0, recipient2.CallCount); // Second handler not invoked during same invoke

      // Act - second invoke
      slimEvent.Raise(sender);

      // Assert
      Assert.Equal(2, recipient1.CallCount);
      Assert.Equal(1, recipient2.CallCount); // Now second handler invoked

      // Cleanup
      sender.Dispose();
      recipient1.Dispose();
      recipient2.Dispose();
   }

   [Fact]
   public void SlimEvent_NullSender_AssertsInDebugBuild()
   {
#if DEBUG
		// Arrange
		var slimEvent = new SlimEvent<TestSender>();

		// Act & Assert
		var exception = Record.Exception(() => slimEvent.Raise(null!));
		Assert.NotNull(exception); // Assertion fires

#endif
   }

   [Fact]
   public void SlimEvent_DisposedSender_AssertsInDebugBuild()
   {
#if DEBUG
		// Arrange
		var slimEvent = new SlimEvent<TestSender>();
		var sender = new TestSender();
		sender.Dispose();
		// Act & Assert
		var exception = Record.Exception(() => slimEvent.Raise(sender));
		Assert.NotNull(exception); // Assertion fires
#endif
   }

   [Fact]
   public void SlimEvent_StaticMethodHandler_WorksWithoutDisposalCheck()
   {
      // Arrange
      var slimEvent = new SlimEvent<TestSender>();
      var sender = new TestSender();

      // Static method handler has null Target - should work fine
      slimEvent.Handler += StaticHandler;

      // Act & Assert - should not throw
      slimEvent.Raise(sender);

      // Cleanup
      sender.Dispose();
   }

   private static void StaticHandler(TestSender sender)
   {
      // Static handler for testing
   }

   [Fact]
   public void SlimEvent_LambdasNotAllowed_AssertsInDebugBuild()
   {
#if DEBUG
		// Arrange
		var slimEvent = new SlimEvent<TestSender>();
		var recipient = new TestRecipient();

		// Act & Assert
		// Lambdas not allowed - Target is closure object, not IDisposeGuard
		var exception = Record.Exception(() => slimEvent.Handler += s => recipient.OnEvent(s));
		Assert.NotNull(exception); // Assertion fires - closure doesn't implement IDisposeGuard
#endif
   }

   #endregion

   #region Remove Specific Handler Tests

   [Fact]
   public void SlimEvent_RemoveSpecificHandler_OnlyRemovesMatchingHandler()
   {
      // Arrange
      var slimEvent = new SlimEvent<TestSender>();
      var sender = new TestSender();
      var recipient1 = new TestRecipient("r1");
      var recipient2 = new TestRecipient("r2");
      var recipient3 = new TestRecipient("r3");

      slimEvent.Handler += recipient1.OnEvent;
      slimEvent.Handler += recipient2.OnEvent;
      slimEvent.Handler += recipient3.OnEvent;

      // Act - remove middle handler
      slimEvent.Handler -= recipient2.OnEvent;
      slimEvent.Raise(sender);

      // Assert
      Assert.Equal(1, recipient1.CallCount);
      Assert.Equal(0, recipient2.CallCount); // Removed
      Assert.Equal(1, recipient3.CallCount);

      // Cleanup
      sender.Dispose();
      recipient1.Dispose();
      recipient2.Dispose();
      recipient3.Dispose();
   }

   [Fact]
   public void SlimEvent_RemoveSameHandlerAddedMultipleTimes_RemovesLastOccurrence()
   {
      // Arrange
      var slimEvent = new SlimEvent<TestSender>();
      var sender = new TestSender();
      var recipient = new TestRecipient();

      slimEvent.Handler += recipient.OnEvent;
      slimEvent.Handler += recipient.OnEvent;
      slimEvent.Handler += recipient.OnEvent;

      // Act - remove once (should remove last occurrence)
      slimEvent.Handler -= recipient.OnEvent;
      slimEvent.Raise(sender);

      // Assert
      Assert.Equal(2, recipient.CallCount); // Two occurrences remain

      // Cleanup
      sender.Dispose();
      recipient.Dispose();
   }

   #endregion
}
