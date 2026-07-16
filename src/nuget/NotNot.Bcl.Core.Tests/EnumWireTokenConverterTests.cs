using System.Text.Json;
using System.Text.Json.Serialization;
using Xunit;

namespace NotNot.Bcl.Core.Tests;

/// <summary>
/// Regression tests for the shared persistence enum converter (<c>CaseInsensitiveEnumConverter</c>,
/// registered first in <c>__.SerializationHelper._roundtripJsonOptions</c>) honoring
/// <see cref="JsonStringEnumMemberNameAttribute"/>.
/// <para>
/// Before the fix, the converter emitted <c>value.ToString()</c> (the member identifier) and read via
/// case-insensitive name-parse only — attribute-blind in both directions, so a member's pinned wire token
/// was silently ignored on the persistence path. These tests pin the corrected contract: a token-bearing
/// member serializes AS its token, both the token and the legacy member-name forms still read (case-
/// insensitively), and an attribute-less enum is byte-identical to the historical output (no regression /
/// no persisted-data flip).
/// </para>
/// </summary>
public class EnumWireTokenConverterTests
{
	// The exact shared PERSISTENCE options that register the converter under test.
	private static readonly JsonSerializerOptions Options =
		NotNot.LoLoRoot.__.SerializationHelper._roundtripJsonOptions;

	/// <summary>Every member pins a wire token DIFFERENT from its identifier — the discriminating case.</summary>
	public enum TokenPinned
	{
		[JsonStringEnumMemberName("active")] Active,
		[JsonStringEnumMemberName("closing-down")] Closing,
	}

	/// <summary>No member opts into a token — must keep the historical <c>value.ToString()</c> form.</summary>
	public enum PlainEnum
	{
		First,
		Second,
	}

	public sealed record TokenHolder(TokenPinned State);
	public sealed record PlainHolder(PlainEnum Kind);

	[Fact]
	public void Write_MemberWithToken_EmitsToken_NotMemberName()
	{
		var json = JsonSerializer.Serialize(new TokenHolder(TokenPinned.Closing), Options);
		// EXPECTED: the pinned token "closing-down", NEVER the member identifier "Closing".
		Assert.Contains("\"closing-down\"", json);
		Assert.DoesNotContain("Closing", json);
	}

	[Fact]
	public void Read_Token_ResolvesToMember()
	{
		var back = JsonSerializer.Deserialize<TokenHolder>("{\"state\":\"active\"}", Options);
		Assert.Equal(TokenPinned.Active, back!.State);
	}

	[Fact]
	public void Read_LegacyMemberName_StillResolves()
	{
		// Data written before the token was honored used the raw member identifier — must still read.
		var back = JsonSerializer.Deserialize<TokenHolder>("{\"state\":\"Closing\"}", Options);
		Assert.Equal(TokenPinned.Closing, back!.State);
	}

	[Fact]
	public void Read_Token_IsCaseInsensitive()
	{
		var back = JsonSerializer.Deserialize<TokenHolder>("{\"state\":\"CLOSING-DOWN\"}", Options);
		Assert.Equal(TokenPinned.Closing, back!.State);
	}

	[Fact]
	public void RoundTrip_TokenPinned_Preserved()
	{
		var original = new TokenHolder(TokenPinned.Closing);
		var json = JsonSerializer.Serialize(original, Options);
		var back = JsonSerializer.Deserialize<TokenHolder>(json, Options);
		Assert.Equal(original.State, back!.State);
	}

	[Fact]
	public void Write_AttributelessEnum_EmitsMemberName_NoRegression()
	{
		// Enums that do NOT opt into a token keep the historical value.ToString() (member name) form,
		// so no already-persisted data changes shape.
		var json = JsonSerializer.Serialize(new PlainHolder(PlainEnum.Second), Options);
		Assert.Contains("Second", json);
	}

	[Fact]
	public void Read_AttributelessEnum_CaseInsensitive_Preserved()
	{
		// The converter's original reason to exist — case-insensitive reads — must survive the fix.
		var back = JsonSerializer.Deserialize<PlainHolder>("{\"kind\":\"second\"}", Options);
		Assert.Equal(PlainEnum.Second, back!.Kind);
	}
}
