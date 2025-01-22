//using System.Diagnostics;
//using System.Runtime.CompilerServices;

//namespace LoLo;

//public abstract class InitializeBase : AsyncDisposeGuard
//{
//	private bool _IsInitialized;
//	protected bool IsInitialized { get => _IsInitialized; init => _IsInitialized = value; }

//	/// <summary>
//	///    call to verify that the object is initialized (and not disposed).  if not, throws an exception.
//	///    only checks in DEBUG builds
//	/// </summary>
//	[Conditional("DEBUG")]
//	[Conditional("CHECKED")]
//	[DebuggerNonUserCode]
//	[DebuggerHidden]
//	protected void _DEBUG_VerifyInitialized([CallerMemberName] string memberName = "",
//		[CallerFilePath] string sourceFilePath = "",
//		[CallerLineNumber] int sourceLineNumber = 0)
//	{
//		__.GetLogger()._EzError(IsInitialized, "not initialized", memberName: memberName,
//			sourceFilePath: sourceFilePath, sourceLineNumber: sourceLineNumber);
//		__.GetLogger()._EzError(IsDisposed is false, "already disposed", memberName: memberName,
//			sourceFilePath: sourceFilePath, sourceLineNumber: sourceLineNumber);
//	}

//	public async Task Initialize(CancellationToken ct = default)
//	{
//		__.GetLogger()._EzError(IsInitialized is false, "already initialized");
//		await OnInitialize(ct);
//		__.GetLogger()._EzError(IsInitialized is true, "you didn't call base.OnInitalize() properly");
//	}

//	protected virtual Task OnInitialize(CancellationToken ct = default)
//	{
//		_IsInitialized = true;
//		return Task.CompletedTask;
//	}
//}

//public abstract class InitializeBase<TInitArg> : AsyncDisposeGuard
//{
//	private bool _IsInitialized;
//	protected bool IsInitialized { get => _IsInitialized; init => _IsInitialized = value; }

//	/// <summary>
//	///    call to verify that the object is initialized (and not disposed).  if not, throws an exception.
//	///    only checks in DEBUG builds
//	/// </summary>
//	[Conditional("DEBUG")]
//	[Conditional("CHECKED")]
//	[DebuggerNonUserCode]
//	[DebuggerHidden]
//	protected void _DEBUG_VerifyInitialized([CallerMemberName] string memberName = "",
//		[CallerFilePath] string sourceFilePath = "",
//		[CallerLineNumber] int sourceLineNumber = 0)
//	{
//		__.GetLogger()._EzError(IsInitialized, "not initialized", memberName: memberName,
//			sourceFilePath: sourceFilePath, sourceLineNumber: sourceLineNumber);
//		__.GetLogger()._EzError(IsDisposed is false, "already disposed", memberName: memberName,
//			sourceFilePath: sourceFilePath, sourceLineNumber: sourceLineNumber);
//	}

//	public async Task Initialize(TInitArg arg, CancellationToken ct = default)
//	{
//		__.GetLogger()._EzError(IsInitialized is false, "already initialized");
//		await OnInitialize(arg, ct);
//		__.GetLogger()._EzError(IsInitialized is true, "you didn't call base.OnInitalize() properly");
//	}

//	protected virtual Task OnInitialize(TInitArg arg, CancellationToken ct = default)
//	{
//		_IsInitialized = true;
//		return Task.CompletedTask;
//	}
//}