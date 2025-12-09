using NotNot.Mixins.Tests.TestHelpers;

namespace NotNot.Mixins.Tests;

/// <summary>
/// Tests for same-project inline composition - these should all work correctly.
/// When base class source is in the same compilation, generator can access and inline members.
/// </summary>
public class SameProjectInlineTests
{
	[Fact]
	public void Inline_BasicField_ShouldGeneratePartialWithField()
	{
		const string input = """
			using NotNot.MixinsAttributes;

			namespace MyCode;

			public class Base {
				public int Value;
			}

			[Inline<Base>]
			public partial class Derived;
			""";

		var sources = SourceGeneratorTestHelper.GenerateUserSourceText(input, out _, out var diagnostics);

		diagnostics.ErrorsAndWarnings().Should().BeEmpty("generator should not produce errors");
		sources.Should().HaveCount(1, "should generate one partial class");
		sources[0].Should().Contain("public int Value;", "field should be inlined");
	}

	[Fact]
	public void Inline_BasicProperty_ShouldGeneratePartialWithProperty()
	{
		const string input = """
			using NotNot.MixinsAttributes;

			namespace MyCode;

			public class Base {
				public string Name { get; set; }
			}

			[Inline<Base>]
			public partial class Derived;
			""";

		var sources = SourceGeneratorTestHelper.GenerateUserSourceText(input, out _, out var diagnostics);

		diagnostics.ErrorsAndWarnings().Should().BeEmpty();
		sources.Should().HaveCount(1);
		sources[0].Should().Contain("public string Name { get; set; }");
	}

	[Fact]
	public void Inline_BasicMethod_ShouldGeneratePartialWithMethod()
	{
		const string input = """
			using NotNot.MixinsAttributes;

			namespace MyCode;

			public class Base {
				public int Calculate(int x) => x * 2;
			}

			[Inline<Base>]
			public partial class Derived;
			""";

		var sources = SourceGeneratorTestHelper.GenerateUserSourceText(input, out _, out var diagnostics);

		diagnostics.ErrorsAndWarnings().Should().BeEmpty();
		sources.Should().HaveCount(1);
		sources[0].Should().Contain("public int Calculate(int x)");
	}

	[Fact]
	public void Inline_MultipleMembers_ShouldGeneratePartialWithAllMembers()
	{
		const string input = """
			using NotNot.MixinsAttributes;
			using System.Collections.Generic;

			namespace MyCode;

			public class Tags {
				private Dictionary<object, object> _tags;

				public bool TryGetTag<T>(object key, out T value) {
					if (_tags != null && _tags.TryGetValue(key, out var obj)) {
						value = (T)obj;
						return true;
					}
					value = default;
					return false;
				}

				public void SetTag<T>(object key, T value) {
					_tags ??= new();
					_tags[key] = value;
				}
			}

			[Inline<Tags>]
			public partial class MyNode;
			""";

		var sources = SourceGeneratorTestHelper.GenerateUserSourceText(input, out _, out var diagnostics);

		diagnostics.ErrorsAndWarnings().Should().BeEmpty();
		sources.Should().HaveCount(1);
		sources[0].Should().Contain("private Dictionary<object, object> _tags;");
		sources[0].Should().Contain("public bool TryGetTag<T>");
		sources[0].Should().Contain("public void SetTag<T>");
	}

	[Fact]
	public void Inline_WithXmlDocumentation_ShouldPreserveDocumentation()
	{
		const string input = """
			using NotNot.MixinsAttributes;

			namespace MyCode;

			public class Base {
				/// <summary>
				/// Gets or sets the name.
				/// </summary>
				public string Name { get; set; }
			}

			[Inline<Base>]
			public partial class Derived;
			""";

		var sources = SourceGeneratorTestHelper.GenerateUserSourceText(input, out _, out var diagnostics);

		diagnostics.ErrorsAndWarnings().Should().BeEmpty();
		sources.Should().HaveCount(1);
		sources[0].Should().Contain("/// <summary>");
		sources[0].Should().Contain("/// Gets or sets the name.");
	}

	[Fact]
	public void Inline_WithMethodXmlDocumentation_ShouldPreserveDocumentation()
	{
		const string input = """
			using NotNot.MixinsAttributes;

			namespace MyCode;

			public class Base {
				/// <summary>
				/// Performs an important calculation.
				/// </summary>
				/// <param name="value">The input value.</param>
				/// <returns>The calculated result.</returns>
				public int Calculate(int value) => value * 2;
			}

			[Inline<Base>]
			public partial class Derived;
			""";

		var sources = SourceGeneratorTestHelper.GenerateUserSourceText(input, out _, out var diagnostics);

		diagnostics.ErrorsAndWarnings().Should().BeEmpty();
		sources.Should().HaveCount(1);
		sources[0].Should().Contain("/// <summary>");
		sources[0].Should().Contain("/// Performs an important calculation.");
		sources[0].Should().Contain("/// <param name=\"value\">The input value.</param>");
		sources[0].Should().Contain("/// <returns>The calculated result.</returns>");
	}

	[Fact]
	public void Inline_WithConstructorXmlDocumentation_ShouldPreserveDocumentation()
	{
		const string input = """
			using NotNot.MixinsAttributes;

			namespace MyCode;

			public class Base {
				/// <summary>
				/// Initializes a new instance.
				/// </summary>
				/// <param name="id">The identifier.</param>
				public Base(int id) { Id = id; }
				public int Id { get; }
			}

			[Inline<Base>]
			public partial class Derived;
			""";

		var sources = SourceGeneratorTestHelper.GenerateUserSourceText(input, out _, out var diagnostics);

		diagnostics.ErrorsAndWarnings().Should().BeEmpty();
		sources.Should().HaveCount(1);
		sources[0].Should().Contain("/// <summary>");
		sources[0].Should().Contain("/// Initializes a new instance.");
		sources[0].Should().Contain("/// <param name=\"id\">The identifier.</param>");
	}

	[Fact]
	public void Inline_MethodWithAttributesAndXmlDoc_ShouldPreserveBoth()
	{
		const string input = """
			using NotNot.MixinsAttributes;
			using System;

			namespace MyCode;

			public class Base {
				/// <summary>
				/// A deprecated method.
				/// </summary>
				[Obsolete("Use NewMethod instead")]
				public void OldMethod() { }
			}

			[Inline<Base>]
			public partial class Derived;
			""";

		var sources = SourceGeneratorTestHelper.GenerateUserSourceText(input, out _, out var diagnostics);

		diagnostics.ErrorsAndWarnings().Should().BeEmpty();
		sources.Should().HaveCount(1);
		sources[0].Should().Contain("/// <summary>");
		sources[0].Should().Contain("/// A deprecated method.");
		sources[0].Should().Contain("[Obsolete");
		sources[0].Should().Contain("public void OldMethod()");
	}

	[Fact]
	public void Inline_MethodWithGenericConstraint_ShouldPreserveConstraint()
	{
		const string input = """
			using NotNot.MixinsAttributes;

			namespace MyCode;

			public class Base {
				public T Create<T>() where T : new() => new T();
			}

			[Inline<Base>]
			public partial class Derived;
			""";

		var sources = SourceGeneratorTestHelper.GenerateUserSourceText(input, out _, out var diagnostics);

		diagnostics.ErrorsAndWarnings().Should().BeEmpty();
		sources.Should().HaveCount(1);
		sources[0].Should().Contain("where T : new()");
	}

	[Fact]
	public void Inline_MethodWithMultipleGenericConstraints_ShouldPreserveAllConstraints()
	{
		const string input = """
			using NotNot.MixinsAttributes;
			using System;

			namespace MyCode;

			public class Base {
				public void Process<T, U>(T item, U other)
					where T : class, IDisposable
					where U : struct
				{
				}
			}

			[Inline<Base>]
			public partial class Derived;
			""";

		var sources = SourceGeneratorTestHelper.GenerateUserSourceText(input, out _, out var diagnostics);

		diagnostics.ErrorsAndWarnings().Should().BeEmpty();
		sources.Should().HaveCount(1);
		sources[0].Should().Contain("where T : class, IDisposable");
		sources[0].Should().Contain("where U : struct");
	}

	[Fact]
	public void Inline_MultipleBaseClasses_ShouldGeneratePartialWithAllMembers()
	{
		const string input = """
			using NotNot.MixinsAttributes;

			namespace MyCode;

			public class MixinA {
				public int ValueA;
			}

			public class MixinB {
				public int ValueB;
			}

			[Inline<MixinA, MixinB>]
			public partial class Derived;
			""";

		var sources = SourceGeneratorTestHelper.GenerateUserSourceText(input, out _, out var diagnostics);

		diagnostics.ErrorsAndWarnings().Should().BeEmpty();
		sources.Should().HaveCount(1);
		sources[0].Should().Contain("public int ValueA;");
		sources[0].Should().Contain("public int ValueB;");
	}

	[Fact]
	public void Inline_OnStruct_ShouldGeneratePartialStruct()
	{
		const string input = """
			using NotNot.MixinsAttributes;

			namespace MyCode;

			public class Base {
				public int X;
				public int Y;
			}

			[Inline<Base>]
			public partial struct Point;
			""";

		var sources = SourceGeneratorTestHelper.GenerateUserSourceText(input, out _, out var diagnostics);

		diagnostics.ErrorsAndWarnings().Should().BeEmpty();
		sources.Should().HaveCount(1);
		sources[0].Should().Contain("public partial struct Point");
		sources[0].Should().Contain("public int X;");
		sources[0].Should().Contain("public int Y;");
	}
}
