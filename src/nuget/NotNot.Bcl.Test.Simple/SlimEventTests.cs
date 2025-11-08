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
		public int CallCount { get; set; }
		public TestSender? LastSender { get; set; }

		public TestRecipient(string name = "recipient")
		{
			Name = name;
		}

		public void OnEvent(TestSender sender)
		{
			CallCount++;
			LastSender = sender;
		}

		public void OnEventWithArgs(TestSender sender, string args)
		{
			CallCount++;
			LastSender = sender;
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
		var callCount = 0;

		slimEvent.Handler += s => callCount++;

		// Act
		slimEvent.Invoke(sender);

		// Assert
		Assert.Equal(1, callCount);

		// Cleanup
		sender.Dispose();
	}

	[Fact]
	public void SlimEvent_MultipleSubscribers_AllInvoked()
	{
		// Arrange
		var slimEvent = new SlimEvent<TestSender>();
		var sender = new TestSender();
		var count1 = 0;
		var count2 = 0;
		var count3 = 0;

		slimEvent.Handler += s => count1++;
		slimEvent.Handler += s => count2++;
		slimEvent.Handler += s => count3++;

		// Act
		slimEvent.Invoke(sender);

		// Assert
		Assert.Equal(1, count1);
		Assert.Equal(1, count2);
		Assert.Equal(1, count3);

		// Cleanup
		sender.Dispose();
	}

	[Fact]
	public void SlimEvent_Unsubscribe_RemovesHandler()
	{
		// Arrange
		var slimEvent = new SlimEvent<TestSender>();
		var sender = new TestSender();
		var callCount = 0;
		Action<TestSender> handler = s => callCount++;

		slimEvent.Handler += handler;
		slimEvent.Invoke(sender);
		Assert.Equal(1, callCount);

		// Act
		slimEvent.Handler -= handler;
		slimEvent.Invoke(sender);

		// Assert
		Assert.Equal(1, callCount); // Still 1 - handler was removed

		// Cleanup
		sender.Dispose();
	}

	[Fact]
	public void SlimEvent_Clear_RemovesAllHandlers()
	{
		// Arrange
		var slimEvent = new SlimEvent<TestSender>();
		var sender = new TestSender();
		var callCount = 0;

		slimEvent.Handler += s => callCount++;
		slimEvent.Handler += s => callCount++;
		slimEvent.Handler += s => callCount++;

		// Act
		slimEvent.Clear();
		slimEvent.Invoke(sender);

		// Assert
		Assert.Equal(0, callCount);

		// Cleanup
		sender.Dispose();
	}

	[Fact]
	public void SlimEvent_EmptyHandlerList_DoesNotThrow()
	{
		// Arrange
		var slimEvent = new SlimEvent<TestSender>();
		var sender = new TestSender();

		// Act & Assert
		slimEvent.Invoke(sender); // Should not throw

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
		slimEvent.Invoke(sender);

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
		var receivedArgs = "";

		slimEvent.Handler += (s, args) => receivedArgs = args;

		// Act
		slimEvent.Invoke(sender, "hello");

		// Assert
		Assert.Equal("hello", receivedArgs);

		// Cleanup
		sender.Dispose();
	}

	[Fact]
	public void SlimEventWithArgs_IntArg_Works()
	{
		// Arrange
		var slimEvent = new SlimEvent<TestSender, int>();
		var sender = new TestSender();
		var receivedValue = 0;

		slimEvent.Handler += (s, value) => receivedValue = value;

		// Act
		slimEvent.Invoke(sender, 42);

		// Assert
		Assert.Equal(42, receivedValue);

		// Cleanup
		sender.Dispose();
	}

	[Fact]
	public void SlimEventWithArgs_StructArg_Works()
	{
		// Arrange
		var slimEvent = new SlimEvent<TestSender, TestArgs>();
		var sender = new TestSender();
		TestArgs receivedArgs = default;

		slimEvent.Handler += (s, args) => receivedArgs = args;

		// Act
		var testArgs = new TestArgs(123, "test");
		slimEvent.Invoke(sender, testArgs);

		// Assert
		Assert.Equal(123, receivedArgs.Value);
		Assert.Equal("test", receivedArgs.Text);

		// Cleanup
		sender.Dispose();
	}

	[Fact]
	public void SlimEventWithArgs_CustomClassArg_Works()
	{
		// Arrange
		var slimEvent = new SlimEvent<TestSender, TestRecipient>();
		var sender = new TestSender();
		TestRecipient? receivedRecipient = null;
		var testRecipient = new TestRecipient("arg");

		slimEvent.Handler += (s, recipient) => receivedRecipient = recipient;

		// Act
		slimEvent.Invoke(sender, testRecipient);

		// Assert
		Assert.Same(testRecipient, receivedRecipient);

		// Cleanup
		sender.Dispose();
		testRecipient.Dispose();
	}

	[Fact]
	public void SlimEventWithArgs_InstanceMethodHandler_Invoked()
	{
		// Arrange
		var slimEvent = new SlimEvent<TestSender, string>();
		var sender = new TestSender();
		var recipient = new TestRecipient();

		slimEvent.Handler += recipient.OnEventWithArgs;

		// Act
		slimEvent.Invoke(sender, "test");

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
		var exception = Record.Exception(() => slimEvent.Invoke(sender));

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
		slimEvent.Invoke(sender);
		Assert.Equal(1, recipient1.CallCount);
		Assert.Equal(1, recipient2.CallCount);

		// Dispose first recipient
		recipient1.Dispose();

		// Act - invoke (will detect and remove disposed recipient)
#if DEBUG
		// In DEBUG builds, assertion fires
		var exception = Record.Exception(() => slimEvent.Invoke(sender));
		Assert.NotNull(exception); // Assertion throws
#else
		// In RELEASE builds, just skips disposed recipient
		slimEvent.Invoke(sender);
#endif

		// Assert - recipient1 still at 1, recipient2 now at 2
		Assert.Equal(1, recipient1.CallCount); // Not called because disposed
		Assert.Equal(2, recipient2.CallCount); // Called on second invoke

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

		slimEvent.Handler += recipient.OnEventWithArgs;

		// Dispose recipient (memory leak scenario)
		recipient.Dispose();

		// Act & Assert
		var exception = Record.Exception(() => slimEvent.Invoke(sender, "test"));

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
		slimEvent.Invoke(sender);
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
		// In DEBUG builds, assertions fire - first disposed recipient triggers exception
		var exception = Record.Exception(() => slimEvent.Invoke(sender));
		Assert.NotNull(exception); // Assertion throws
#else
		// In RELEASE builds, skips disposed recipients
		slimEvent.Invoke(sender);
#endif

		// Assert - disposed recipients still at 1, live recipient at 2
		Assert.Equal(1, recipient1.CallCount);
		Assert.Equal(1, recipient2.CallCount);
		Assert.Equal(2, liveRecipient.CallCount); // Called on second invoke
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
		var callCount = 0;

		// Act - concurrent subscription
		Parallel.For(0, threadCount, _ =>
		{
			for (int i = 0; i < subscriptionsPerThread; i++)
			{
				slimEvent.Handler += s => Interlocked.Increment(ref callCount);
			}
		});

		slimEvent.Invoke(sender);

		// Assert
		Assert.Equal(threadCount * subscriptionsPerThread, callCount);

		// Cleanup
		sender.Dispose();
	}

	[Fact]
	public void SlimEvent_ConcurrentInvocation_ThreadSafe()
	{
		// Arrange
		var slimEvent = new SlimEvent<TestSender>();
		var sender = new TestSender();
		var callCount = 0;

		slimEvent.Handler += s => Interlocked.Increment(ref callCount);

		// Act - concurrent invocation
		const int invocations = 1000;
		Parallel.For(0, invocations, _ =>
		{
			slimEvent.Invoke(sender);
		});

		// Assert
		Assert.Equal(invocations, callCount);

		// Cleanup
		sender.Dispose();
	}

	[Fact]
	public void SlimEvent_ConcurrentSubscribeUnsubscribeInvoke_ThreadSafe()
	{
		// Arrange
		var slimEvent = new SlimEvent<TestSender>();
		var sender = new TestSender();
		var callCount = 0;
		var handlers = new List<Action<TestSender>>();

		// Create handlers
		for (int i = 0; i < 100; i++)
		{
			handlers.Add(s => Interlocked.Increment(ref callCount));
		}

		// Act - concurrent operations
		Parallel.Invoke(
			// Subscribe
			() =>
			{
				foreach (var handler in handlers)
				{
					slimEvent.Handler += handler;
				}
			},
			// Unsubscribe
			() =>
			{
				Thread.Sleep(5); // Let some subscribe first
				foreach (var handler in handlers.Take(50))
				{
					slimEvent.Handler -= handler;
				}
			},
			// Invoke
			() =>
			{
				for (int i = 0; i < 100; i++)
				{
					slimEvent.Invoke(sender);
					Thread.Sleep(1);
				}
			}
		);

		// Assert - no exceptions thrown, operations completed
		Assert.True(callCount > 0); // Some handlers were invoked

		// Cleanup
		sender.Dispose();
	}

	#endregion

	#region Edge Cases

	[Fact]
	public void SlimEvent_HandlerThrowsException_BubblesAndStopsInvocation()
	{
		// Arrange
		var slimEvent = new SlimEvent<TestSender>();
		var sender = new TestSender();
		var count1 = 0;
		var count2 = 0;

		slimEvent.Handler += s => count1++;
		slimEvent.Handler += s => throw new InvalidOperationException("Test exception");
		slimEvent.Handler += s => count2++; // Should not be called

		// Act & Assert
		Assert.Throws<InvalidOperationException>(() => slimEvent.Invoke(sender));

		Assert.Equal(1, count1); // First handler called
		Assert.Equal(0, count2); // Third handler not called - exception stopped invocation

		// Cleanup
		sender.Dispose();
	}

	[Fact]
	public void SlimEvent_HandlerUnsubscribesSelfDuringInvocation_Safe()
	{
		// Arrange
		var slimEvent = new SlimEvent<TestSender>();
		var sender = new TestSender();
		Action<TestSender>? selfRemovingHandler = null;
		var callCount = 0;

		selfRemovingHandler = s =>
		{
			callCount++;
			slimEvent.Handler -= selfRemovingHandler!;
		};

		slimEvent.Handler += selfRemovingHandler;

		// Act
		slimEvent.Invoke(sender);
		slimEvent.Invoke(sender); // Second invoke - handler removed

		// Assert
		Assert.Equal(1, callCount); // Only called once

		// Cleanup
		sender.Dispose();
	}

	[Fact]
	public void SlimEvent_HandlerSubscribesNewHandlerDuringInvocation_NewHandlerNotInvokedImmediately()
	{
		// Arrange
		var slimEvent = new SlimEvent<TestSender>();
		var sender = new TestSender();
		var count1 = 0;
		var count2 = 0;

		slimEvent.Handler += s =>
		{
			count1++;
			// Subscribe new handler during invocation
			slimEvent.Handler += s2 => count2++;
		};

		// Act
		slimEvent.Invoke(sender); // First handler subscribes second handler

		// Assert
		Assert.Equal(1, count1);
		Assert.Equal(0, count2); // Second handler not invoked during same invoke

		// Act - second invoke
		slimEvent.Invoke(sender);

		// Assert
		Assert.Equal(2, count1);
		Assert.Equal(1, count2); // Now second handler invoked

		// Cleanup
		sender.Dispose();
	}

	[Fact]
	public void SlimEvent_NullSender_AssertsInDebugBuild()
	{
#if DEBUG
		// Arrange
		var slimEvent = new SlimEvent<TestSender>();

		// Act & Assert
		var exception = Record.Exception(() => slimEvent.Invoke(null!));
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
		var exception = Record.Exception(() => slimEvent.Invoke(sender));
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
		slimEvent.Invoke(sender);

		// Cleanup
		sender.Dispose();
	}

	private static void StaticHandler(TestSender sender)
	{
		// Static handler for testing
	}

	[Fact]
	public void SlimEvent_LambdaCapturingDisposedRecipient_CannotBeDetected()
	{
		// Arrange
		var slimEvent = new SlimEvent<TestSender>();
		var sender = new TestSender();
		var recipient = new TestRecipient();

		// Lambda captures recipient - Target is closure object, not recipient
		slimEvent.Handler += s => recipient.OnEvent(s);

		// Dispose recipient
		recipient.Dispose();

		// Act
		// Lambda disposal cannot be detected - Target is closure, not captured variable
		// This will still invoke the handler, but with a disposed recipient
		// User responsibility to unsubscribe lambdas that capture disposable objects
		slimEvent.Invoke(sender);

		// Assert - handler was called (no disposal detection for lambdas)
		Assert.Equal(1, recipient.CallCount);

		// Cleanup
		sender.Dispose();
	}

	#endregion

	#region Remove Specific Handler Tests

	[Fact]
	public void SlimEvent_RemoveSpecificHandler_OnlyRemovesMatchingHandler()
	{
		// Arrange
		var slimEvent = new SlimEvent<TestSender>();
		var sender = new TestSender();
		var count1 = 0;
		var count2 = 0;
		var count3 = 0;

		Action<TestSender> handler1 = s => count1++;
		Action<TestSender> handler2 = s => count2++;
		Action<TestSender> handler3 = s => count3++;

		slimEvent.Handler += handler1;
		slimEvent.Handler += handler2;
		slimEvent.Handler += handler3;

		// Act - remove middle handler
		slimEvent.Handler -= handler2;
		slimEvent.Invoke(sender);

		// Assert
		Assert.Equal(1, count1);
		Assert.Equal(0, count2); // Removed
		Assert.Equal(1, count3);

		// Cleanup
		sender.Dispose();
	}

	[Fact]
	public void SlimEvent_RemoveSameHandlerAddedMultipleTimes_RemovesLastOccurrence()
	{
		// Arrange
		var slimEvent = new SlimEvent<TestSender>();
		var sender = new TestSender();
		var callCount = 0;

		Action<TestSender> handler = s => callCount++;

		slimEvent.Handler += handler;
		slimEvent.Handler += handler;
		slimEvent.Handler += handler;

		// Act - remove once (should remove last occurrence)
		slimEvent.Handler -= handler;
		slimEvent.Invoke(sender);

		// Assert
		Assert.Equal(2, callCount); // Two occurrences remain

		// Cleanup
		sender.Dispose();
	}

	#endregion
}
