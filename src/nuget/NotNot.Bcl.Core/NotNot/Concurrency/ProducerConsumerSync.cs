using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NotNot.Concurrency;

using Nito.AsyncEx;

/// <summary>
/// synchronization construct specialized in single producer, single consumer.  One value at a time.
/// OK for consumer to wait for producer.
/// but if the producer has to wait for the consumer to finish processing, that's bad, so there is .IsConsumerRunning() to check.
/// <para>good for doing async work once per tick.</para>
/// </summary>
/// <typeparam name="TValue"></typeparam>
public class ProducerConsumerSync<TValue>
{
	private TValue _value;

	AsyncAutoResetEvent _waitForProduce = new(false);
	AsyncAutoResetEvent _waitForConsumeStarted = new(false);
	AsyncAutoResetEvent _waitForConsumeDone = new(true);

	public CancellationTokenSource _cts = new();

	public void Produce(TValue value, CancellationToken ct)
	{
		ct = ct._Link(_cts.Token);

		using (_waitForConsumeDoneGuard.Lock())
		{
			_waitForConsumeDone.Wait(ct);
		}

		this._value = value;
		_waitForProduce.Set();
		_waitForConsumeStarted.Wait(ct);
		ct.ThrowIfCancellationRequested();
	}



	/// <summary>
	/// true if consume is processing
	/// </summary>
	/// <returns></returns>
	public bool IsConsumerRunning()
	{
		return _waitForConsumeDone.IsSet is false;
	}


	/// <summary>
	/// wait for the consumer to complete processing without publishing a new value
	/// </summary>
	public void WaitForConsumerDone(CancellationToken ct)
	{
		ct = ct._Link(_cts.Token);

		if (_waitForConsumeDone.IsSet)
		{
			return;
		}

		using (_waitForConsumeDoneGuard.Lock())
		{
			try
			{
				_waitForConsumeDone.Wait(ct);
			}
			finally
			{
				_waitForConsumeDone.Set();
			}
		}
	}
	private AsyncLock _waitForConsumeDoneGuard = new();

	public async ValueTask<TValue> StartConsume(CancellationToken ct)
	{
		ct = ct._Link(_cts.Token);
		await _waitForProduce.WaitAsync(ct);
		var toReturn = this._value;
		this._value = default;
		_waitForConsumeStarted.Set();
		ct.ThrowIfCancellationRequested();
		return toReturn;
	}
	public void NotifyConsumeFinished()
	{
		_waitForConsumeDone.Set();
	}


	public void Dispose()
	{
		_cts.Cancel();

	}
}
