using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading;

namespace NotNot;

/// <summary>
/// Hand-rolled minimal <see cref="IObservable{T}"/> implementation. Lock-free Subscribe/Unsubscribe
/// via <see cref="ImmutableInterlocked"/>; OnNext iterates an immutable snapshot so subscribers
/// can come and go mid-emission without races.
/// </summary>
/// <remarks>
/// <para>
/// Chosen over <c>System.Reactive</c> to keep <c>NotNot.Bcl.Core</c> dependency-free for
/// non-Rx consumers. Subscribers get the standard <see cref="IObservable{T}"/> contract, so
/// the implementation can be swapped to <c>Subject&lt;T&gt;</c> later without consumer churn.
/// </para>
/// <para>
/// <b>Observer contract:</b> subscribers MUST be non-throwing. An exception thrown from
/// <see cref="IObserver{T}.OnNext"/> is swallowed (logged via <see cref="OnObserverException"/>
/// when set) so a single failing observer does not break the rest of the dispatch chain.
/// </para>
/// </remarks>
/// <typeparam name="T">Notification value type.</typeparam>
public sealed class SimpleObservable<T> : IObservable<T>
{
	private ImmutableArray<IObserver<T>> _observers = ImmutableArray<IObserver<T>>.Empty;

	/// <summary>
	/// Optional hook invoked when a subscriber's <see cref="IObserver{T}.OnNext"/> throws.
	/// Allows external diagnostic logging without coupling the observable to a logger.
	/// </summary>
	public Action<Exception, IObserver<T>>? OnObserverException { get; set; }

	/// <summary>
	/// Registers a new observer. Returns an <see cref="IDisposable"/> whose <see cref="IDisposable.Dispose"/>
	/// removes the observer; multiple Dispose calls are idempotent.
	/// </summary>
	public IDisposable Subscribe(IObserver<T> observer)
	{
		if (observer is null) throw new ArgumentNullException(nameof(observer));
		ImmutableInterlocked.Update(ref _observers, static (current, obs) => current.Add(obs), observer);
		return new Subscription(this, observer);
	}

	/// <summary>
	/// Pushes a value to every currently-registered observer. Observers added or removed
	/// concurrently with this call are tolerated — iteration uses an immutable snapshot of the
	/// observer list captured at entry.
	/// </summary>
	public void OnNext(T value)
	{
		// ImmutableArray<T> is a struct; field reads are atomic word-sized references to the
		// underlying array. ImmutableInterlocked.Update guarantees writers publish via memory
		// barrier, so a plain field read here observes a consistent snapshot.
		var snapshot = _observers;
		foreach (var observer in snapshot)
		{
			try
			{
				observer.OnNext(value);
			}
#pragma warning disable NN_R005 // Observer exceptions must not crash the publisher
			catch (Exception ex)
#pragma warning restore NN_R005
			{
				OnObserverException?.Invoke(ex, observer);
			}
		}
	}

	private void Unsubscribe(IObserver<T> observer)
	{
		ImmutableInterlocked.Update(ref _observers, static (current, obs) => current.Remove(obs), observer);
	}

	private sealed class Subscription : IDisposable
	{
		private SimpleObservable<T>? _parent;
		private IObserver<T>? _observer;

		public Subscription(SimpleObservable<T> parent, IObserver<T> observer)
		{
			_parent = parent;
			_observer = observer;
		}

		public void Dispose()
		{
			var parent = Interlocked.Exchange(ref _parent, null);
			var observer = Interlocked.Exchange(ref _observer, null);
			if (parent is not null && observer is not null)
			{
				parent.Unsubscribe(observer);
			}
		}
	}
}

/// <summary>
/// Convenience adapter: wraps an <see cref="Action{T}"/> as an <see cref="IObserver{T}"/>
/// with no-op <see cref="IObserver{T}.OnCompleted"/> and <see cref="IObserver{T}.OnError"/>.
/// Saves callers from implementing the full 3-method <see cref="IObserver{T}"/> interface
/// when they only care about <see cref="IObserver{T}.OnNext"/>.
/// </summary>
/// <typeparam name="T">Notification value type.</typeparam>
public sealed class ActionObserver<T> : IObserver<T>
{
	private readonly Action<T> _onNext;

	public ActionObserver(Action<T> onNext)
	{
		_onNext = onNext ?? throw new ArgumentNullException(nameof(onNext));
	}

	public void OnNext(T value) => _onNext(value);
	public void OnCompleted() { }
	public void OnError(Exception error) { }
}
