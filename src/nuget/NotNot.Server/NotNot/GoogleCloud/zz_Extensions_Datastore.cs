using Google.Cloud.Datastore.V1;
using NotNot;

public static class zz_Extensions_Datastore
{
	/// <summary>
	/// easy automatic transaction commit and retry logic wrapper.
	/// </summary>
	public static async Task<Maybe<T>> _EzTransaction<T>(this DatastoreDb datastore, Func<DatastoreTransaction, Task<T>> action, CancellationToken ct, int maxRetries = _retryCount)
	{
		try
		{
			return await RetryRpc<Maybe<T>>(async () =>
			{
				{
					using (var transaction = await datastore.BeginTransactionAsync())
					{
						var toReturn = await action(transaction);

						await transaction.CommitAsync();
						return new(toReturn);
					}
				}
			}, ct, maxRetries);
		}
		catch (GRpcAggregateException grpcae)
		{
			if (grpcae._Find<Grpc.Core.RpcException>(out var rpcEx))
			{
				var toReturn = new Problem()
				{
					Detail = rpcEx.Message,
					Ex = grpcae,
					isRecoverable = true,
					category = Problem.CategoryNames.DbIo,
					Status = System.Net.HttpStatusCode.InternalServerError,
				};
				return new(toReturn);
			}
			throw;
		}
	}


	private static int _retryDelayMs = 20;
	private const int _retryCount = 5;

	/// <summary>
	/// Retry the action when a Grpc.Core.RpcException is thrown.
	/// </summary>
	public static async Task RetryRpc(Func<Task> action, CancellationToken ct, int maxRetries = _retryCount)
	{
		var result = await RetryRpc(async () => { await action(); return 0; }, ct, maxRetries);
	}
	public static async Task<T> RetryRpc<T>(Func<Task<T>> action, CancellationToken ct, int maxRetries = _retryCount)
	{
		List<Grpc.Core.RpcException>? exceptions = null;
		var delayMs = _retryDelayMs;
		for (int tryCount = 0; tryCount < maxRetries; ++tryCount)
		{
			try
			{
				var val = await action();
				return val;
			}
			catch (Grpc.Core.RpcException e)
			{
				if (exceptions == null)
					exceptions = new List<Grpc.Core.RpcException>();
				exceptions.Add(e);
			}
			//System.Threading.Thread.Sleep(delayMs);
			await __.Async.Delay(TimeSpan.FromMilliseconds(delayMs), ct);
			delayMs *= 2;  // Exponential back-off.
		}
		throw new GRpcAggregateException(exceptions!);
	}


}
