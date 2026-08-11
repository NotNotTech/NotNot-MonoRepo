using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using NotNot.Templating;
using Xunit;

namespace NotNot.Bcl.Core.Tests;

/// <summary>
/// The core-side fence for the unified <c>%key%</c> grammar owned by <see cref="TemplateString"/>.
/// </summary>
/// <remarks>
/// <para>
/// Organized as eleven pin families; each region names the defect class it alone catches. A pin that
/// carries no unique defect does not belong here.
/// </para>
/// <para>
/// The RichText suite (<c>NotNot.BlazorDesign.PublicTests</c>) fences the same grammar from the
/// conditional-dialect consumer's side; this file fences it at the layer that owns it.
/// </para>
/// </remarks>
public class TemplateStringTests
{
	/// <summary>A value carrying every byte the two dialects give meaning to, plus Stage-B markup bytes.</summary>
	private const string GrammarSoup = @"%inner% ?{group} } \ \?{ \} ** `code` [fg=red] <tag>";

	#region Family 1 — Plain-dialect byte-inertness
	// UNIQUE_DEFECT: a flipped dialect default, or group/escape handling leaking into the plain path.

	[Fact]
	public void PlainDialect_ConditionalBytes_ByteInert_GroupAndEscapeBytesSurvive()
	{
		// Every byte the conditional dialect owns — ?{ } \ \?{ \} — while the scanner is demonstrably
		// active (the trailing %k% is substituted).
		var result = new TemplateString
		{
			Template = @"a?{b}c\d\?{e\}f%k%",
			Values = new Dictionary<string, string> { ["k"] = "V" },
		}.Resolve();

		Assert.Equal(@"a?{b}c\d\?{e\}fV", result.Text);
		Assert.Empty(result.UnresolvedKeys);
	}

	[Fact]
	public void PlainDialect_ConditionalBytes_ByteInert_KeylessGroupSurvivesWhole()
	{
		var result = new TemplateString
		{
			Template = "?{keyless}%k%",
			Values = new Dictionary<string, string> { ["k"] = "V" },
		}.Resolve();

		Assert.Equal("?{keyless}V", result.Text);
	}

	[Fact]
	public void PlainDialect_ConditionalBytes_ByteInert_SubstitutionStillRunsInsideGroupBytes()
	{
		// The group delimiters are literal, but the key inside them still resolves — proving the plain
		// dialect is "group branch never entered", not "scanner disabled".
		var result = new TemplateString
		{
			Template = "?{%name%}",
			OnResolveKey = key => key == "name" ? "World" : null,
		}.Resolve();

		Assert.Equal("?{World}", result.Text);
	}

	#endregion

	#region Family 2 — Conditional-dialect grammar meanings (keyless group + escapes + termination)
	// UNIQUE_DEFECT: locks keyless/group/escape semantics at the core layer so the RichText suite is not
	// the only fence for them.

	[Fact]
	public void ConditionalDialect_KeylessGroup_EmitsBodyWithoutDelimiters()
	{
		var result = new TemplateString
		{
			Template = "?{keyless}",
			EnableConditionalGroups = true,
		}.Resolve();

		Assert.Equal("keyless", result.Text);
	}

	[Fact]
	public void ConditionalDialect_GroupWithResolvedKey_EmitsBodyWithoutDelimiters()
	{
		var result = new TemplateString
		{
			Template = "pre ?{[%a%]} post",
			Values = new Dictionary<string, string> { ["a"] = "A" },
			EnableConditionalGroups = true,
		}.Resolve();

		Assert.Equal("pre [A] post", result.Text);
	}

	[Fact]
	public void ConditionalDialect_GroupWithBlankKey_DropsWholeGroupIncludingDelimiters()
	{
		var result = new TemplateString
		{
			Template = "pre ?{[%a%]} post",
			Values = new Dictionary<string, string> { ["a"] = "   " },
			EnableConditionalGroups = true,
		}.Resolve();

		Assert.Equal("pre  post", result.Text);
	}

	[Fact]
	public void ConditionalDialect_GroupWithDeclinedKey_DropsWholeGroup()
	{
		var result = new TemplateString
		{
			Template = "pre ?{[%a%]} post",
			Values = new Dictionary<string, string>(),
			EnableConditionalGroups = true,
		}.Resolve();

		Assert.Equal("pre  post", result.Text);
	}

	[Fact]
	public void ConditionalDialect_Escapes_UnescapeOwnedSequencesOnly()
	{
		// \?{ and \} unescape; every other backslash passes through untouched.
		var result = new TemplateString
		{
			Template = @"\?{a\}\d",
			EnableConditionalGroups = true,
		}.Resolve();

		Assert.Equal(@"?{a}\d", result.Text);
	}

	[Fact]
	public void ConditionalDialect_GroupsDoNotNest_FirstCloseBraceEndsTheGroup()
	{
		var result = new TemplateString
		{
			Template = "?{a?{b}c}",
			EnableConditionalGroups = true,
		}.Resolve();

		Assert.Equal("a?{bc}", result.Text);
	}

	[Fact]
	public void ConditionalDialect_UnterminatedGroup_LeavesRemainderLiteral()
	{
		var result = new TemplateString
		{
			Template = "head ?{%a%",
			Values = new Dictionary<string, string> { ["a"] = "A" },
			EnableConditionalGroups = true,
		}.Resolve();

		Assert.Equal("head ?{%a%", result.Text);
		Assert.Empty(result.UnresolvedKeys);
	}

	#endregion

	#region Family 3 — Windows-path template pass-through
	// UNIQUE_DEFECT: a resolved %env:USERPROFILE% output becomes a phase-2 TEMPLATE downstream, so escape
	// narrowness is a cross-repo invariant, not a local nicety.

	[Fact]
	public void WindowsPathTemplate_BackslashesIntact_InBothDialects()
	{
		const string template = @"C:\Users\x\.claude/projects/%ProjectId%/%SessionId%.jsonl";
		var values = new Dictionary<string, string> { ["ProjectId"] = "P", ["SessionId"] = "S" };
		const string expected = @"C:\Users\x\.claude/projects/P/S.jsonl";

		var plain = new TemplateString { Template = template, Values = values }.Resolve();
		var conditional = new TemplateString
		{
			Template = template,
			Values = values,
			EnableConditionalGroups = true,
		}.Resolve();

		Assert.Equal(expected, plain.Text);
		Assert.Equal(expected, conditional.Text);
	}

	[Fact]
	public void WindowsPathTemplate_BraceBearingPath_ByteIdenticalInPlainDialect()
	{
		var result = new TemplateString
		{
			Template = @"C:\build\}tmp\%Name%",
			Values = new Dictionary<string, string> { ["Name"] = "out" },
		}.Resolve();

		Assert.Equal(@"C:\build\}tmp\out", result.Text);
	}

	#endregion

	#region Family 4 — Two-phase resolution metamorphic
	// UNIQUE_DEFECT: a sentinel-based two-pass implementation that promotes substituted values to live
	// grammar.

	[Fact]
	public void TwoPhase_DisjointKeySets_Metamorphic()
	{
		const string template = "%greeting%, %name%! Home=%home%";
		var phase1Values = new Dictionary<string, string> { ["greeting"] = "Hello", ["home"] = "/root" };
		var phase2Values = new Dictionary<string, string> { ["name"] = "World" };
		var unionValues = new Dictionary<string, string>
		{
			["greeting"] = "Hello",
			["home"] = "/root",
			["name"] = "World",
		};

		var phase1 = new TemplateString { Template = template, Values = phase1Values }.Resolve();
		var phase2 = new TemplateString { Template = phase1.Text, Values = phase2Values }.Resolve();
		var single = new TemplateString { Template = template, Values = unionValues }.Resolve();

		Assert.Equal(new[] { "name" }, phase1.UnresolvedKeys);
		Assert.Equal(single.Text, phase2.Text);
		Assert.Empty(phase2.UnresolvedKeys);
		Assert.Empty(single.UnresolvedKeys);
	}

	[Fact]
	public void TwoPhase_UnresolvedKeySurvivesPhaseOne_ResolvesInPhaseTwo()
	{
		var result = "Value: %outer% %inner%"
			._ResolveTemplate(key => key == "outer" ? "OUT" : null)
			._ResolveTemplate(key => key == "inner" ? "IN" : null);

		Assert.Equal("Value: OUT IN", result);
	}

	#endregion

	#region Family 5 — Substituted-value inertness (the R4' decision)
	// UNIQUE_DEFECT: the decision itself, AND the output-re-scan false-positive class — a %-shaped
	// substring inside a VALUE must not register as unresolved.

	[Fact]
	public void SubstitutedValue_GrammarBytes_InertAndUnreported()
	{
		var result = new TemplateString
		{
			Template = "pre %v% post",
			Values = new Dictionary<string, string>
			{
				["v"] = GrammarSoup,
				["inner"] = "SHOULD_NOT_APPEAR",
			},
		}.Resolve();

		Assert.Equal("pre " + GrammarSoup + " post", result.Text);
		Assert.DoesNotContain("SHOULD_NOT_APPEAR", result.Text);
		Assert.Empty(result.UnresolvedKeys);
	}

	[Fact]
	public void SubstitutedValue_GrammarBytes_InertAndUnreported_InConditionalDialect()
	{
		// The value carries ?{ , } and \} — none of them may re-enter the scanner, and the enclosing
		// group must not be closed early by the value's own '}'.
		var result = new TemplateString
		{
			Template = "?{%v%}",
			Values = new Dictionary<string, string>
			{
				["v"] = GrammarSoup,
				["inner"] = "SHOULD_NOT_APPEAR",
			},
			EnableConditionalGroups = true,
		}.Resolve();

		Assert.Equal(GrammarSoup, result.Text);
		Assert.DoesNotContain("SHOULD_NOT_APPEAR", result.Text);
		Assert.Empty(result.UnresolvedKeys);
	}

	#endregion

	#region Family 6 — Comparer preservation (Values held by reference)
	// UNIQUE_DEFECT: any defensive copy or re-wrap of Values, which silently replaces the caller's comparer.

	[Fact]
	public void Comparer_OrdinalIgnoreCase_ResolvesOffCase()
	{
		var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
		{
			["SessionName"] = "Chat",
		};

		var result = new TemplateString { Template = "%sessionname%", Values = values }.Resolve();

		Assert.Equal("Chat", result.Text);
		Assert.Empty(result.UnresolvedKeys);
	}

	[Fact]
	public void Comparer_OrdinalIgnoreCase_ResolvesOffCase_ThroughNonDictionaryMap()
	{
		// A NON-Dictionary<string,string> IReadOnlyDictionary: a re-wrap into a fresh Ordinal dictionary
		// would lose the comparer here even where it survives for a plain Dictionary.
		IReadOnlyDictionary<string, string> values = new ReadOnlyDictionary<string, string>(
			new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["SessionName"] = "Chat" });

		var result = new TemplateString { Template = "%sessionname%", Values = values }.Resolve();

		Assert.Equal("Chat", result.Text);
	}

	#endregion

	#region Family 7 — Drop-residual-whitespace shape
	// UNIQUE_DEFECT: a "clean up the drop" refactor silently breaking byte-equivalence for the display-name
	// consumer that compensates downstream with a whitespace collapse.

	[Fact]
	public void DropResidualWhitespace_InterGroupSpaceRetained()
	{
		var result = new TemplateString
		{
			Template = "?{%a%} ?{%b%}",
			Values = new Dictionary<string, string> { ["a"] = "A", ["b"] = "" },
			EnableConditionalGroups = true,
		}.Resolve();

		Assert.Equal("A ", result.Text);
	}

	#endregion

	#region Family 8 — Unresolved-key reporting
	// UNIQUE_DEFECT: drift between the reported set and the resolver's actual declines; the positional
	// special case.

	[Fact]
	public void UnresolvedKeys_DistinctSourceOrdered_ReportsEachDeclinedKeyOnce()
	{
		var result = new TemplateString
		{
			Template = "%b% %a% %b% %c%",
			Values = new Dictionary<string, string> { ["a"] = "A" },
		}.Resolve();

		Assert.Equal(new[] { "b", "c" }, result.UnresolvedKeys);
		Assert.Equal("%b% A %b% %c%", result.Text);
	}

	[Fact]
	public void UnresolvedKeys_DistinctSourceOrdered_EmptyWhenEveryKeyResolves()
	{
		var result = new TemplateString
		{
			Template = "%a%/%b%",
			Values = new Dictionary<string, string> { ["a"] = "A", ["b"] = "B" },
		}.Resolve();

		Assert.Empty(result.UnresolvedKeys);
	}

	[Fact]
	public void UnresolvedKeys_NoResolverSupplied_DeclinedSetIsTheKeySet()
	{
		// The ENUMERATE role: with neither callback nor dictionary, every key is declined.
		var result = new TemplateString { Template = @"%a%/%env:HOME%\%a%" }.Resolve();

		Assert.Equal(new[] { "a", "env:HOME" }, result.UnresolvedKeys);
		Assert.Equal(@"%a%/%env:HOME%\%a%", result.Text);
	}

	[Fact]
	public void UnresolvedKeys_DeclinedInsideGroup_IsReported()
	{
		var result = new TemplateString
		{
			Template = "?{%missing%}",
			Values = new Dictionary<string, string>(),
			EnableConditionalGroups = true,
		}.Resolve();

		Assert.Equal("", result.Text);
		Assert.Equal(new[] { "missing" }, result.UnresolvedKeys);
	}

	[Fact]
	public void UnresolvedKeys_BlankResolvedInsideGroup_IsNotReported()
	{
		// The resolver ANSWERED — blank merely drops the group. Reporting it would make the VALIDATE
		// consumer throw on a correct render.
		var result = new TemplateString
		{
			Template = "?{%blank%}",
			Values = new Dictionary<string, string> { ["blank"] = "" },
			EnableConditionalGroups = true,
		}.Resolve();

		Assert.Equal("", result.Text);
		Assert.Empty(result.UnresolvedKeys);
	}

	[Fact]
	public void UnresolvedKeys_DeclinedInsideUnterminatedGroup_IsStillReported()
	{
		// The remainder emits literally, but "the resolver declined" is uniform — the scan already asked.
		var result = new TemplateString
		{
			Template = "?{%missing%",
			Values = new Dictionary<string, string>(),
			EnableConditionalGroups = true,
		}.Resolve();

		Assert.Equal("?{%missing%", result.Text);
		Assert.Equal(new[] { "missing" }, result.UnresolvedKeys);
	}

	#endregion

	#region Family 9 — No-hit fast path
	// UNIQUE_DEFECT: the skip guard is unreachable for core consumers (they always supply a resolver), so a
	// regression there is invisible to a resolver-less test.

	[Fact]
	public void NoHitFastPath_WithResolver_ByteIdentical()
	{
		// Built at runtime so reference identity is a real discriminator: a scanner that always fills a
		// StringBuilder returns an equal-but-different instance.
		var template = string.Concat("no tokens here", new string('!', 1));

		var result = new TemplateString
		{
			Template = template,
			OnResolveKey = _ => "SHOULD_NOT_BE_CALLED",
			Values = new Dictionary<string, string> { ["anything"] = "X" },
		}.Resolve();

		Assert.Equal(template, result.Text);
		Assert.Same(template, result.Text);
		Assert.Empty(result.UnresolvedKeys);
	}

	[Fact]
	public void NoHitFastPath_WithResolver_ByteIdenticalInConditionalDialect()
	{
		var template = string.Concat("no tokens here", new string('!', 1));

		var result = new TemplateString
		{
			Template = template,
			OnResolveKey = _ => "SHOULD_NOT_BE_CALLED",
			EnableConditionalGroups = true,
		}.Resolve();

		Assert.Same(template, result.Text);
	}

	#endregion

	#region Family 10 — Null contracts
	// UNIQUE_DEFECT: retained from the pre-change suite; the engine's throw-on-null-Template contract and the
	// sugar's two argument guards.

	[Fact]
	public void NullContracts_ResolveWithNullTemplate_Throws()
	{
		var ts = new TemplateString { Template = null! };
		Assert.Throws<ArgumentNullException>(() => ts.Resolve());
	}

	[Fact]
	public void NullContracts_ResolveTemplateSugar_NullTemplate_Throws()
	{
		string? template = null;
		Assert.Throws<ArgumentNullException>(() => template!._ResolveTemplate(_ => "value"));
	}

	[Fact]
	public void NullContracts_ResolveTemplateSugar_NullResolver_Throws()
	{
		Assert.Throws<ArgumentNullException>(() =>
			"Hello %name%!"._ResolveTemplate((Func<string, string?>)null!));
	}

	#endregion

	#region Family 11 — Single-pass + callback-first (the ONE priority contract)
	// UNIQUE_DEFECT: retained from the pre-change suite; callback-first is now the only resolution-priority
	// contract, and key-shape narrowness keeps '%' a legal ordinary character.

	[Fact]
	public void SinglePassCallbackFirst_CallbackBeatsDictionary()
	{
		var result = new TemplateString
		{
			Template = "Hello %name%!",
			OnResolveKey = key => key == "name" ? "Callback" : null,
			Values = new Dictionary<string, string> { ["name"] = "Dictionary" },
		}.Resolve();

		Assert.Equal("Hello Callback!", result.Text);
	}

	[Fact]
	public void SinglePassCallbackFirst_DictionaryUsedWhenCallbackDeclines()
	{
		var result = new TemplateString
		{
			Template = "Hello %name% and %other%!",
			OnResolveKey = key => key == "name" ? "Callback" : null,
			Values = new Dictionary<string, string> { ["name"] = "Dict", ["other"] = "Value" },
		}.Resolve();

		Assert.Equal("Hello Callback and Value!", result.Text);
	}

	[Fact]
	public void SinglePassCallbackFirst_SubstitutedValueIsNeverReExpanded()
	{
		var result = new TemplateString
		{
			Template = "Value: %outer%",
			Values = new Dictionary<string, string>
			{
				["outer"] = "%inner%",
				["inner"] = "SHOULD_NOT_APPEAR",
			},
		}.Resolve();

		Assert.Equal("Value: %inner%", result.Text);
		Assert.Empty(result.UnresolvedKeys);
	}

	[Fact]
	public void SinglePassCallbackFirst_EmptyStringReturn_RemovesPlaceholder()
	{
		var result = "Hello %name%!"._ResolveTemplate(_ => "");
		Assert.Equal("Hello !", result);
	}

	[Fact]
	public void SinglePassCallbackFirst_InvalidKeyShapes_NotMatchedAndNotReported()
	{
		var result = new TemplateString
		{
			Template = "%123% %a-b% % key% %key %",
			OnResolveKey = _ => "REPLACED",
		}.Resolve();

		Assert.Equal("%123% %a-b% % key% %key %", result.Text);
		Assert.Empty(result.UnresolvedKeys);
	}

	[Fact]
	public void SinglePassCallbackFirst_LonePercentSign_Untouched()
	{
		var result = "50% off and %name%"._ResolveTemplate(key => key == "name" ? "sale" : null);
		Assert.Equal("50% off and sale", result);
	}

	[Fact]
	public void SinglePassCallbackFirst_ValidKeyShapes_Matched()
	{
		var values = new Dictionary<string, string>
		{
			["_private"] = "p",
			["var_123"] = "v",
			["env:HOME"] = "h",
		};

		var result = new TemplateString { Template = "%_private%|%var_123%|%env:HOME%", Values = values }.Resolve();

		Assert.Equal("p|v|h", result.Text);
	}

	[Fact]
	public void SinglePassCallbackFirst_KeysAreCaseSensitive()
	{
		var result = new TemplateString
		{
			Template = "Hello %Name% and %name%!",
			Values = new Dictionary<string, string> { ["name"] = "lower" },
		}.Resolve();

		Assert.Equal("Hello %Name% and lower!", result.Text);
		Assert.Equal(new[] { "Name" }, result.UnresolvedKeys);
	}

	[Fact]
	public void SinglePassCallbackFirst_RepeatedAndAdjacentPlaceholders_ResolveEachOccurrence()
	{
		var values = new Dictionary<string, string>
		{
			["greeting"] = "Hello",
			["name"] = "World",
			["punctuation"] = "!",
		};

		var result = new TemplateString { Template = "%greeting% %name%%punctuation% %name%", Values = values }.Resolve();

		Assert.Equal("Hello World! World", result.Text);
	}

	[Fact]
	public void SinglePassCallbackFirst_NoSourcesSupplied_LeavesPlaceholderLiteral()
	{
		var result = new TemplateString
		{
			Template = "Hello %name%!",
			OnResolveKey = null,
			Values = null,
		}.Resolve();

		Assert.Equal("Hello %name%!", result.Text);
		Assert.Equal(new[] { "name" }, result.UnresolvedKeys);
	}

	[Fact]
	public void SinglePassCallbackFirst_EmptyTemplate_ReturnsEmpty()
	{
		var result = ""._ResolveTemplate(_ => "should not be called");
		Assert.Equal("", result);
	}

	#endregion
}
