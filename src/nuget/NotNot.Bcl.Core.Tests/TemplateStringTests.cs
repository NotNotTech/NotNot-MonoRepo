using System;
using System.Collections.Generic;
using Xunit;

namespace NotNot.Bcl.Core.Tests;

/// <summary>
/// Tests for TemplateString class and _ResolveTemplate extension methods.
/// Verifies %key% placeholder resolution with callback and dictionary APIs.
/// </summary>
public class TemplateStringTests
{
	#region Basic Resolution Tests

	[Fact]
	public void ResolveTemplate_WithResolver_ReplacesKeys()
	{
		var result = "Hello %name%!"._ResolveTemplate(key => key == "name" ? "World" : null);
		Assert.Equal("Hello World!", result);
	}

	[Fact]
	public void ResolveTemplate_WithDictionary_ReplacesKeys()
	{
		var values = new Dictionary<string, string> { ["name"] = "World" };
		var result = "Hello %name%!"._ResolveTemplate(values);
		Assert.Equal("Hello World!", result);
	}

	[Fact]
	public void ResolveTemplate_MissingKey_LeavesUnchanged()
	{
		var result = "Hello %name%!"._ResolveTemplate(_ => null);
		Assert.Equal("Hello %name%!", result);
	}

	[Fact]
	public void ResolveTemplate_MultiplePlaceholders_ResolvesAll()
	{
		var values = new Dictionary<string, string>
		{
			["greeting"] = "Hello",
			["name"] = "World",
			["punctuation"] = "!"
		};
		var result = "%greeting% %name%%punctuation%"._ResolveTemplate(values);
		Assert.Equal("Hello World!", result);
	}

	[Fact]
	public void ResolveTemplate_RepeatedPlaceholder_ResolvesEachOccurrence()
	{
		var values = new Dictionary<string, string> { ["x"] = "Y" };
		var result = "%x% and %x% again"._ResolveTemplate(values);
		Assert.Equal("Y and Y again", result);
	}

	#endregion

	#region Edge Cases

	[Fact]
	public void ResolveTemplate_EmptyTemplate_ReturnsEmpty()
	{
		var result = ""._ResolveTemplate(_ => "should not be called");
		Assert.Equal("", result);
	}

	[Fact]
	public void ResolveTemplate_NoPlaceholders_ReturnsOriginal()
	{
		var result = "Hello World!"._ResolveTemplate(_ => "should not be called");
		Assert.Equal("Hello World!", result);
	}

	[Fact]
	public void ResolveTemplate_NullResolver_Throws()
	{
		Assert.Throws<ArgumentNullException>(() =>
			"Hello %name%!"._ResolveTemplate((Func<string, string?>)null!));
	}

	[Fact]
	public void ResolveTemplate_NullDictionary_Throws()
	{
		Assert.Throws<ArgumentNullException>(() =>
			"Hello %name%!"._ResolveTemplate((IReadOnlyDictionary<string, string>)null!));
	}

	[Fact]
	public void ResolveTemplate_NullTemplate_Throws()
	{
		string? template = null;
		Assert.Throws<ArgumentNullException>(() =>
			template!._ResolveTemplate(_ => "value"));
	}

	[Fact]
	public void ResolveTemplate_ReturnsEmptyString_RemovesPlaceholder()
	{
		var result = "Hello %name%!"._ResolveTemplate(_ => "");
		Assert.Equal("Hello !", result);
	}

	[Fact]
	public void ResolveTemplate_DictionaryMissingKey_LeavesUnchanged()
	{
		var values = new Dictionary<string, string> { ["other"] = "value" };
		var result = "Hello %name%!"._ResolveTemplate(values);
		Assert.Equal("Hello %name%!", result);
	}

	#endregion

	#region Syntax Tests

	[Fact]
	public void ResolveTemplate_InvalidKeyFormat_NotMatched()
	{
		// Keys starting with digit are not matched
		var result = "Test %123% here"._ResolveTemplate(_ => "REPLACED");
		Assert.Equal("Test %123% here", result);
	}

	[Fact]
	public void ResolveTemplate_KeyWithHyphen_NotMatched()
	{
		// Keys with hyphens are not valid identifiers
		var result = "Test %a-b% here"._ResolveTemplate(_ => "REPLACED");
		Assert.Equal("Test %a-b% here", result);
	}

	[Fact]
	public void ResolveTemplate_PartialMatch_NotMatched()
	{
		// Spaces inside %...% break the pattern
		var result = "Test % key% and %key % here"._ResolveTemplate(_ => "REPLACED");
		Assert.Equal("Test % key% and %key % here", result);
	}

	[Fact]
	public void ResolveTemplate_CaseSensitive_DifferentKeys()
	{
		var values = new Dictionary<string, string> { ["name"] = "lower" };
		var result = "Hello %Name% and %name%!"._ResolveTemplate(values);
		Assert.Equal("Hello %Name% and lower!", result);
	}

	[Fact]
	public void ResolveTemplate_UnderscorePrefix_ValidKey()
	{
		var values = new Dictionary<string, string> { ["_private"] = "value" };
		var result = "Test %_private% here"._ResolveTemplate(values);
		Assert.Equal("Test value here", result);
	}

	[Fact]
	public void ResolveTemplate_UnderscoreAndDigits_ValidKey()
	{
		var values = new Dictionary<string, string> { ["var_123"] = "value" };
		var result = "Test %var_123% here"._ResolveTemplate(values);
		Assert.Equal("Test value here", result);
	}

	[Fact]
	public void ResolveTemplate_SinglePercent_NotMatched()
	{
		var result = "50% off and %name%"._ResolveTemplate(key => key == "name" ? "sale" : null);
		Assert.Equal("50% off and sale", result);
	}

	#endregion

	#region Security Tests

	[Fact]
	public void ResolveTemplate_ValueContainsPlaceholder_NoRecursiveResolution()
	{
		// Value contains another placeholder - should NOT be resolved (single-pass)
		var values = new Dictionary<string, string>
		{
			["outer"] = "%inner%",
			["inner"] = "SHOULD_NOT_APPEAR"
		};
		var result = "Value: %outer%"._ResolveTemplate(values);
		Assert.Equal("Value: %inner%", result); // NOT "Value: SHOULD_NOT_APPEAR"
	}

	#endregion

	#region Chaining Tests

	[Fact]
	public void ResolveTemplate_MultipleChained_ResolvesSequentially()
	{
		// User-driven chaining: multiple explicit calls
		var pass1 = new Dictionary<string, string> { ["outer"] = "%inner%" };
		var pass2 = new Dictionary<string, string> { ["inner"] = "resolved" };

		var result = "Value: %outer%"
			._ResolveTemplate(pass1)
			._ResolveTemplate(pass2);

		Assert.Equal("Value: resolved", result);
	}

	#endregion

	#region Fallback Overload Tests

	[Fact]
	public void ResolveTemplate_WithFallback_DictionaryFirst()
	{
		var values = new Dictionary<string, string> { ["name"] = "FromDict" };
		var result = "Hello %name%!"._ResolveTemplate(values, _ => "FromFallback");
		Assert.Equal("Hello FromDict!", result);
	}

	[Fact]
	public void ResolveTemplate_WithFallback_UsesFallbackWhenMissing()
	{
		var values = new Dictionary<string, string> { ["other"] = "value" };
		var result = "Hello %name%!"._ResolveTemplate(values, key => key == "name" ? "FromFallback" : null);
		Assert.Equal("Hello FromFallback!", result);
	}

	[Fact]
	public void ResolveTemplate_WithFallback_NullFallback_Throws()
	{
		var values = new Dictionary<string, string>();
		Assert.Throws<ArgumentNullException>(() =>
			"test"._ResolveTemplate(values, null!));
	}

	#endregion

	#region TemplateString Class Tests

	[Fact]
	public void TemplateString_Resolve_WithDictionary_Works()
	{
		var ts = new TemplateString
		{
			Template = "Hello %name%!",
			Values = new Dictionary<string, string> { ["name"] = "World" }
		};
		Assert.Equal("Hello World!", ts.Resolve());
	}

	[Fact]
	public void TemplateString_Resolve_WithCallback_Works()
	{
		var ts = new TemplateString
		{
			Template = "Hello %name%!",
			OnResolveKey = key => key == "name" ? "Callback" : null
		};
		Assert.Equal("Hello Callback!", ts.Resolve());
	}

	[Fact]
	public void TemplateString_Resolve_CallbackPriority()
	{
		// Callback takes priority over dictionary (different from extension method fallback overload)
		var ts = new TemplateString
		{
			Template = "Hello %name%!",
			OnResolveKey = key => key == "name" ? "Callback" : null,
			Values = new Dictionary<string, string> { ["name"] = "Dictionary" }
		};
		Assert.Equal("Hello Callback!", ts.Resolve());
	}

	[Fact]
	public void TemplateString_Resolve_DictionaryFallback()
	{
		// When callback returns null, dictionary is used
		var ts = new TemplateString
		{
			Template = "Hello %name% and %other%!",
			OnResolveKey = key => key == "name" ? "Callback" : null,
			Values = new Dictionary<string, string> { ["name"] = "Dict", ["other"] = "Value" }
		};
		Assert.Equal("Hello Callback and Value!", ts.Resolve());
	}

	[Fact]
	public void TemplateString_Resolve_BothNullSources_LeavesUnchanged()
	{
		var ts = new TemplateString
		{
			Template = "Hello %name%!",
			OnResolveKey = null,
			Values = null
		};
		Assert.Equal("Hello %name%!", ts.Resolve());
	}

	[Fact]
	public void TemplateString_Resolve_EmptyDictionary_LeavesUnchanged()
	{
		var ts = new TemplateString
		{
			Template = "Hello %name%!",
			Values = new Dictionary<string, string>()
		};
		Assert.Equal("Hello %name%!", ts.Resolve());
	}

	#endregion
}
