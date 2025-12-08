using Microsoft.CodeAnalysis;
using NotNot.Mixins.Tests.TestHelpers;

namespace NotNot.Mixins.Tests;

/// <summary>
/// Tests demonstrating the cross-assembly limitation of InlineComposition.
///
/// CRITICAL: Source generators can only access source code in the current compilation.
/// When a base class comes from an external assembly (compiled .dll), the generator
/// cannot read its source to inline members.
///
/// This mirrors the real-world SlimNode/Tags issue:
/// - Tags class in NotNot.Bcl.Core (external assembly)
/// - SlimNode in PaxPagi (current compilation) with [Inline&lt;Tags&gt;]
/// - Generator sees only Tags metadata, not source → no members inlined
/// </summary>
public class CrossAssemblyLimitationTests
{
	/// <summary>
	/// Simulates the Tags class from NotNot.Bcl.Core external assembly.
	/// </summary>
	private const string ExternalTagsSource = """
		using System;
		using System.Collections.Generic;

		namespace NotNot.Mixins;

		public interface ITags
		{
			TValue _GetTagOrDefault<TValue>(object key, TValue defaultValue = default);
			void _SetTag<TValue>(object key, TValue value);
			bool _TryGetTag<TValue>(object key, out TValue value);
			bool _TryRemoveTag(object key);
		}

		public class Tags : ITags
		{
			private Dictionary<object, object> _tags;

			public bool _TryGetTag<TValue>(object key, out TValue value)
			{
				if (_tags != null && _tags.TryGetValue(key, out var objValue))
				{
					value = (TValue)objValue;
					return true;
				}
				value = default;
				return false;
			}

			public TValue _GetTagOrDefault<TValue>(object key, TValue defaultValue = default)
			{
				if (_TryGetTag<TValue>(key, out var value))
					return value;
				return defaultValue;
			}

			public TValue _GetOrCreateTag<TValue>(object key, TValue defaultValue)
			{
				if (_TryGetTag<TValue>(key, out var value))
					return value;
				_SetTag<TValue>(key, defaultValue);
				return defaultValue;
			}

			public TValue _GetOrCreateTag<TValue>(object key, Func<TValue> createDefaultValue)
			{
				if (_TryGetTag<TValue>(key, out var value))
					return value;
				var defaultValue = createDefaultValue();
				_SetTag<TValue>(key, defaultValue);
				return defaultValue;
			}

			public void _SetTag<TValue>(object key, TValue value)
			{
				_tags ??= new();
				_tags[key] = value;
			}

			public bool _TryRemoveTag(object key)
			{
				if (_tags == null)
					return false;
				return _tags.Remove(key);
			}
		}
		""";

	[Fact]
	public void CrossAssembly_TagsInExternalAssembly_ShouldNotInlineMembers()
	{
		// Step 1: Compile Tags into an "external" assembly (simulating NotNot.Bcl.Core.dll)
		var externalAssemblyRef = SourceGeneratorTestHelper.CreateExternalAssemblyReference(
			ExternalTagsSource,
			"NotNot.Bcl.Core");

		// Step 2: Main source references Tags from external assembly
		// This mirrors: SlimNode in PaxPagi with [Inline<Tags>] where Tags is in NotNot.Bcl.Core
		const string mainSource = """
			using NotNot.MixinsAttributes;
			using NotNot.Mixins;

			namespace MyCode;

			[Inline<Tags>]
			public partial class SlimNode;
			""";

		// Step 3: Run generator - it should NOT produce inlined members
		// because Tags source is not available (only metadata from .dll)
		var sources = SourceGeneratorTestHelper.GenerateWithExternalAssembly(
			mainSource,
			externalAssemblyRef,
			out var outputCompilation,
			out var diagnostics);

		// Filter to user sources (exclude attribute definitions)
		var userSources = sources
			.Where(s => !s.Contains("internal sealed class InlineAttribute") &&
			           !s.Contains("internal sealed class InlineBaseAttribute") &&
			           !s.Contains("internal sealed class NoInlineAttribute") &&
			           !s.Contains("internal sealed class InlineMethodAttribute") &&
			           !s.Contains("internal sealed class InlineConstructorAttribute") &&
			           !s.Contains("internal sealed class InlineFinalizerAttribute"))
			.ToArray();

		// Generator should not produce errors (silent handling of cross-assembly)
		diagnostics.Should().BeEmpty("generator should handle external assembly gracefully without errors");

		// CRITICAL ASSERTION: Generator creates empty partial class but NO members inlined
		// When Tags is in external assembly, the partial class is generated but empty
		userSources.Should().HaveCount(1,
			"generator creates a partial class file even when no members can be inlined");

		// The key verification: no Tags methods are inlined
		var generatedSource = userSources[0];
		generatedSource.Should().NotContain("_GetOrCreateTag",
			"Tags._GetOrCreateTag should NOT be inlined from external assembly");
		generatedSource.Should().NotContain("_TryGetTag",
			"Tags._TryGetTag should NOT be inlined from external assembly");
		generatedSource.Should().NotContain("_SetTag",
			"Tags._SetTag should NOT be inlined from external assembly");
		generatedSource.Should().NotContain("Dictionary<object, object> _tags",
			"Tags._tags field should NOT be inlined from external assembly");
	}

	[Fact]
	public void CrossAssembly_TagsInExternalAssembly_ShouldNotContainTagsMethods()
	{
		// Compile Tags as external
		var externalAssemblyRef = SourceGeneratorTestHelper.CreateExternalAssemblyReference(
			ExternalTagsSource,
			"NotNot.Bcl.Core");

		const string mainSource = """
			using NotNot.MixinsAttributes;
			using NotNot.Mixins;

			namespace MyCode;

			[Inline<Tags>]
			public partial class SlimNode;
			""";

		var allSources = SourceGeneratorTestHelper.GenerateWithExternalAssembly(
			mainSource,
			externalAssemblyRef,
			out _,
			out _);

		// Verify: None of the Tags methods appear in any generated source
		var joinedSources = string.Join("\n", allSources);

		joinedSources.Should().NotContain("_GetOrCreateTag",
			"Tags._GetOrCreateTag should NOT be inlined from external assembly");
		joinedSources.Should().NotContain("_TryGetTag",
			"Tags._TryGetTag should NOT be inlined from external assembly");
		joinedSources.Should().NotContain("_SetTag",
			"Tags._SetTag should NOT be inlined from external assembly");
		joinedSources.Should().NotContain("private Dictionary<object, object> _tags",
			"Tags._tags field should NOT be inlined from external assembly");
	}

	[Fact]
	public void SameCompilation_TagsInSameProject_ShouldInlineMembers()
	{
		// CONTRAST: When Tags source IS in the same compilation, inlining works
		const string input = """
			using NotNot.MixinsAttributes;
			using System;
			using System.Collections.Generic;

			namespace NotNot.Mixins
			{
				public class Tags
				{
					private Dictionary<object, object> _tags;

					public TValue _GetOrCreateTag<TValue>(object key, TValue defaultValue)
					{
						if (_TryGetTag<TValue>(key, out var value))
							return value;
						_SetTag<TValue>(key, defaultValue);
						return defaultValue;
					}

					public bool _TryGetTag<TValue>(object key, out TValue value)
					{
						if (_tags != null && _tags.TryGetValue(key, out var objValue))
						{
							value = (TValue)objValue;
							return true;
						}
						value = default;
						return false;
					}

					public void _SetTag<TValue>(object key, TValue value)
					{
						_tags ??= new();
						_tags[key] = value;
					}
				}
			}

			namespace MyCode
			{
				using NotNot.Mixins;

				[Inline<Tags>]
				public partial class SlimNode;
			}
			""";

		var sources = SourceGeneratorTestHelper.GenerateUserSourceText(input, out _, out var diagnostics);

		// Generator should not produce errors
		diagnostics.Should().BeEmpty("generator should not produce errors for same-compilation inlining");

		// PASSES: Same-compilation inlining works correctly
		sources.Should().HaveCount(1, "should generate partial class when source available");
		sources[0].Should().Contain("_GetOrCreateTag", "methods should be inlined");
		sources[0].Should().Contain("_TryGetTag");
		sources[0].Should().Contain("_SetTag");
		sources[0].Should().Contain("private Dictionary<object, object> _tags");
	}

	[Fact]
	public void CrossAssembly_RealWorldScenario_SlimNodeWithTags()
	{
		// This test mirrors the EXACT issue from the original bug report:
		// - Tags in NotNot.Bcl.Core (external)
		// - SlimNode in PaxPagi uses [Inline<Tags>]
		// - SlimNodeTreeDebugWindow.cs:35 calls rootMost._GetOrCreateTag<...>
		// - Build fails: CS1061 'SlimNode' does not contain a definition for '_GetOrCreateTag'

		var externalAssemblyRef = SourceGeneratorTestHelper.CreateExternalAssemblyReference(
			ExternalTagsSource,
			"NotNot.Bcl.Core");

		const string slimNodeSource = """
			using NotNot.MixinsAttributes;
			using NotNot.Mixins;

			namespace NotNot.SlimGraph;

			/// <summary>
			/// Mirrors the real SlimNode class structure
			/// </summary>
			[Inline<Tags>]
			public abstract partial class SlimNode
			{
				public bool IsInitialized { get; private set; }
			}
			""";

		var sources = SourceGeneratorTestHelper.GenerateWithExternalAssembly(
			slimNodeSource,
			externalAssemblyRef,
			out var outputCompilation,
			out var diagnostics);

		// Get the SlimNode type from compilation
		var slimNodeType = outputCompilation.GetTypeByMetadataName("NotNot.SlimGraph.SlimNode");
		slimNodeType.Should().NotBeNull();

		// Verify: SlimNode does NOT have _GetOrCreateTag method
		// This is the root cause of CS1061
		var getOrCreateTagMethod = slimNodeType!.GetMembers("_GetOrCreateTag");
		getOrCreateTagMethod.Should().BeEmpty(
			"_GetOrCreateTag should NOT exist - cross-assembly inlining fails silently, " +
			"causing CS1061 when usage code tries to call it");

		// Verify: SlimNode does NOT have _tags field
		var tagsField = slimNodeType.GetMembers("_tags");
		tagsField.Should().BeEmpty(
			"_tags field should NOT exist - not inlined from external assembly");
	}

	[Fact]
	public void CrossAssembly_Workaround_CopyTagsToSameProject()
	{
		// SOLUTION: Copy Tags.cs to the same project as SlimNode
		// This makes the source available to the generator
		const string input = """
			using NotNot.MixinsAttributes;
			using System;
			using System.Collections.Generic;

			namespace NotNot.Mixins
			{
				// Tags copied to PaxPagi project (same compilation)
				public class Tags
				{
					private Dictionary<object, object> _tags;

					public TValue _GetOrCreateTag<TValue>(object key, TValue defaultValue)
					{
						if (_TryGetTag<TValue>(key, out var value))
							return value;
						_SetTag<TValue>(key, defaultValue);
						return defaultValue;
					}

					public bool _TryGetTag<TValue>(object key, out TValue value)
					{
						if (_tags != null && _tags.TryGetValue(key, out var objValue))
						{
							value = (TValue)objValue;
							return true;
						}
						value = default;
						return false;
					}

					public void _SetTag<TValue>(object key, TValue value)
					{
						_tags ??= new();
						_tags[key] = value;
					}
				}
			}

			namespace NotNot.SlimGraph
			{
				using NotNot.Mixins;

				[Inline<Tags>]
				public abstract partial class SlimNode
				{
					public bool IsInitialized { get; private set; }
				}
			}
			""";

		var sources = SourceGeneratorTestHelper.GenerateUserSourceText(input, out var outputCompilation, out var diagnostics);

		// Generator should not produce errors
		diagnostics.Should().BeEmpty("generator should not produce errors for workaround scenario");

		// PASSES: With Tags in same compilation, inlining works
		sources.Should().HaveCount(1);
		sources[0].Should().Contain("_GetOrCreateTag");

		// Verify: SlimNode now HAS the method
		var slimNodeType = outputCompilation.GetTypeByMetadataName("NotNot.SlimGraph.SlimNode");
		slimNodeType.Should().NotBeNull("SlimNode type should exist in compilation");
		var getOrCreateTagMethod = slimNodeType!.GetMembers("_GetOrCreateTag");
		getOrCreateTagMethod.Should().NotBeEmpty(
			"_GetOrCreateTag SHOULD exist when Tags source is in same compilation");
	}
}
