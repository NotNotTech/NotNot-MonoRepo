//using System.Runtime.CompilerServices;

//namespace LoLo;

///// <summary>
/////    Combines Lazy{T} with Task{T}.
/////    from: https://devblogs.microsoft.com/pfxteam/asynclazyt/
///// </summary>
///// <typeparam name="T"></typeparam>
//[Obsolete("use Nito.AsyncEx.AsyncLazy instead", true)]
//public class AsyncLazy<T> : Lazy<Task<T>>
//{
//	public AsyncLazy(Func<T> valueFactory) : base(() => Task.Factory.StartNew(valueFactory))
//	{
//	}

//	/// <summary>
//	///    invokes the taskFactory via Factory.StartNew to ensure run asynchronously.
//	/// </summary>
//	/// <param name="taskFactory"></param>
//	public AsyncLazy(Func<Task<T>> taskFactory) :
//		base(() => __.Async.Factory.Run(taskFactory)) // Task.Factory.StartNew(() => taskFactory()).Unwrap())
//	{
//	}

//	public AsyncLazy(T value) : base(Task.FromResult(value))
//	{
//	}


//	/// <summary>
//	///    allows awaiting on this, not only "await thisVar.Value"
//	/// </summary>
//	public TaskAwaiter<T> GetAwaiter()
//	{
//		return Value.GetAwaiter();
//	}
//}

