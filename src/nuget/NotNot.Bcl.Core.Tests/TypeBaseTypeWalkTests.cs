using System;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace NotNot.Bcl.Core.Tests;

/// <summary>
/// Regression tests for the base-type walk loops in <c>zz_Extensions_Type</c> (<c>_IsBaseType</c> /
/// <c>_IsSubclassOfRawGeneric</c>, declared in the global namespace).
/// <para>
/// Previously the loops read <c>while (type != typeof(object)) { if (type == null) continue; ... type = type.BaseType; }</c>.
/// <see cref="Type.BaseType"/> is null for interfaces (and for System.Object), so once the cursor became null the
/// <c>continue</c> re-evaluated the same condition forever — an infinite loop that never advanced the cursor. The fix
/// replaces <c>continue</c> with <c>break</c> (null = top of the chain reached). These tests feed an interface type
/// (BaseType == null) and require termination within a hard timeout; under the bug they would hang.
/// </para>
/// </summary>
public class TypeBaseTypeWalkTests
{
	private static readonly TimeSpan _terminationBudget = TimeSpan.FromSeconds(5);

	[Fact]
	public void IsBaseType_InterfaceWithNullBaseType_TerminatesAndReturnsFalse()
	{
		// IDisposable is an interface: typeof(IDisposable).BaseType is null.
		var result = RunWithTimeout(() => typeof(IDisposable)._IsBaseType(typeof(object)));

		// object is not on an interface's base-type chain, so the answer is false — and, critically, the call returns.
		Assert.False(result);
	}

	[Fact]
	public void IsSubclassOfRawGeneric_InterfaceWithNullBaseType_TerminatesAndReturnsFalse()
	{
		var result = RunWithTimeout(() => typeof(IDisposable)._IsSubclassOfRawGeneric(typeof(System.Collections.Generic.List<>)));

		Assert.False(result);
	}

	[Fact]
	public void IsBaseType_ClassChain_FindsGenuineBaseType()
	{
		// Sanity: the corrected loop still finds a real base type on a normal class chain.
		var result = RunWithTimeout(() => typeof(Derived)._IsBaseType(typeof(BaseClass)));

		Assert.True(result);
	}

	/// <summary>
	/// Runs <paramref name="work"/> on a background thread and fails the test (rather than hanging the whole
	/// suite) if it does not complete within the budget — the observable signature of the infinite-loop bug.
	/// </summary>
	private static bool RunWithTimeout(Func<bool> work)
	{
		var task = Task.Run(work);
		var completed = task.Wait(_terminationBudget);
		Assert.True(completed, "base-type walk did not terminate within the timeout (infinite-loop regression)");
		return task.Result;
	}

	private class BaseClass { }

	private sealed class Derived : BaseClass { }
}
