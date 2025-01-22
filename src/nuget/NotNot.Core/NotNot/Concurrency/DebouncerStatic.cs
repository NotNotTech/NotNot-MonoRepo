//using NotNot.Concurrency;

//namespace NotNot.GodotNet.Serialization;


////seems to have some bugs, shelf for now (not debouncing in godot as expected)

///// <summary>
///// static, shared instance of debouncer for easy access
///// </summary>
//public static class DebouncerStatic
//{
//	private static Debouncer _debouncer = new();

//	public static async Task EventuallyOnce(object debounceKey, Func<Task> action, CancellationToken ct = default)
//	{
//		await _debouncer.EventuallyOnce(debounceKey, action, ct);

//	}
//	public static async Task EventuallyOnce(Func<Task> action, CancellationToken ct = default)
//	{
//		var debounceKey = $"{action.Method.Name}:{action.Target?.GetHashCode()}";

		

//		await _debouncer.EventuallyOnce(action, action, ct);
//	}
//	public static async Task<TResult> EventuallyOnce<TResult>(object debounceKey, Func<Task<TResult>> action, CancellationToken ct = default)
//	{
//		return await _debouncer.EventuallyOnce(debounceKey, action, ct);

//	}
//	public static async Task<TResult> EventuallyOnce<TResult>(Func<Task<TResult>> action, CancellationToken ct = default)
//	{
//		var debounceKey = $"{action.Method.Name}:{action.Target?.GetHashCode()}";
//		return await _debouncer.EventuallyOnce(debounceKey, action, ct);
//	}


//}