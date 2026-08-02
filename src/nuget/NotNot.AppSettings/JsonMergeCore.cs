// SHARED-SOURCE: this file is compiled into BOTH NotNot.AppSettings (netstandard2.0 generator)
// AND NotNot.Bcl.Core (net10.0 runtime) via <Compile Include> in NotNot.Bcl.Core.csproj.
// Canonical location: NotNot.AppSettings/JsonMergeCore.cs. Edits here flow into both assemblies.
// Do not fork behavior between JsonMergeCore and JsonSettingsUtils — the latter delegates here.
//
// LANGUAGE CONSTRAINT: keep netstandard2.0-compatible — no System.Range, no init-only
// properties, no records. Nullable annotations are fine (enabled via <Nullable>annotations</Nullable>).
//
// CONTRACT (RFC-7396-ish JSON merge-patch): deep-merge objects, REPLACE arrays wholesale
// (not concatenate), null literal in diff DELETES the key from target.
//
// DUAL-COMPILATION NOTE: because this type lives in two assemblies under the same fully-qualified
// name (NotNot.AppSettingsInternal.JsonMergeCore), any test or consumer that references BOTH
// NotNot.AppSettings AND NotNot.Bcl.Core will hit CS0433 "type exists in both". Use MSBuild
// <ProjectReference Aliases="..."> + `extern alias` to disambiguate (see
// NotNot.AppSettings.Tests.csproj for the canonical pattern).

using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace NotNot.AppSettingsInternal;

/// <summary>
/// Internal merge/diff core for layered JSON settings. Shared between the
/// netstandard2.0 source generator (NotNot.AppSettings) and the net10.0 runtime
/// (NotNot.Bcl.Core). Canonical implementation of the null-sentinel deletion
/// semantics used by both compile-time `JsonMerger` and runtime `JsonSettingsUtils`.
/// </summary>
internal static class JsonMergeCore
{
	/// <summary>
	/// Canonical <see cref="JsonDocumentOptions"/> used by BOTH the generator (build-time parse in
	/// <c>JsonMerger.MergeJsonFiles</c>) AND the runtime (<c>JsonSettingsUtils.MergeStreamsAsync</c>
	/// in NotNot.Bcl.Core). Human-authored <c>appsettings*.json</c> files commonly contain trailing
	/// commas and line/block comments; both are accepted here. Keep the two parse sites in sync
	/// via this single source of truth — do not redeclare per site.
	/// </summary>
	/// <remarks>
	/// REQ-3 completion for parser-options parity (Phase-5 H2 closure). Prior to this field,
	/// build-time used lenient options while runtime <c>JsonNode.ParseAsync</c> used default strict
	/// options, violating the ReadMe contract that "same semantics apply at build-time and at runtime".
	/// </remarks>
	internal static readonly JsonDocumentOptions DefaultDocumentOptions = new()
	{
		AllowTrailingCommas = true,
		CommentHandling = JsonCommentHandling.Skip,
		MaxDepth = 64,
	};

	/// <summary>
	/// Deep merges <paramref name="diff"/> into <paramref name="target"/>, preserving existing keys in target.
	/// Null sentinels in the diff remove the corresponding key from the target (not set to null).
	/// </summary>
	/// <param name="target">Target JSON to merge into. If null, treated as empty object.</param>
	/// <param name="diff">Diff containing changes to apply. If null, returns a clone of target.</param>
	/// <returns>A new JsonNode with the merged result, or null if both inputs are null.</returns>
	internal static JsonNode? Merge(JsonNode? target, JsonNode? diff)
	{
		// Nothing to apply — return clone of target (may itself be null)
		if (diff == null)
		{
			return target?.DeepClone();
		}

		// If diff is not an object, it replaces target entirely
		if (diff is not JsonObject diffObj)
		{
			return diff.DeepClone();
		}

		JsonObject targetObj;
		if (target is JsonObject existingObj)
		{
			// Clone to avoid modifying original
			targetObj = JsonNode.Parse(existingObj.ToJsonString())!.AsObject();
		}
		else
		{
			targetObj = new JsonObject();
		}

		foreach (var prop in diffObj)
		{
			// Deletion semantics: null value means remove the key
			if (prop.Value == null || prop.Value.GetValueKind() == JsonValueKind.Null)
			{
				targetObj.Remove(prop.Key);
			}
			else if (prop.Value is JsonObject childDiff)
			{
				// Recursive merge for nested objects
				targetObj.TryGetPropertyValue(prop.Key, out var existing);
				targetObj[prop.Key] = Merge(existing ?? new JsonObject(), childDiff);
			}
			else
			{
				// Direct value replacement
				targetObj[prop.Key] = prop.Value.DeepClone();
			}
		}

		return targetObj;
	}

	/// <summary>
	/// Sequentially applies all <paramref name="sources"/> via <see cref="Merge"/>, later wins per key.
	/// Each source MUST be a JSON object at root — non-object roots (arrays, primitives) are rejected
	/// with a <see cref="JsonException"/> per fail-fast RULE_4 (errors VISIBLE, not masked).
	/// </summary>
	/// <param name="sources">Sources to merge, in order of priority (last wins).</param>
	/// <returns>A merged JsonObject (always non-null; may be empty).</returns>
	/// <exception cref="JsonException">Thrown when any source has a non-object root.</exception>
	internal static JsonObject MergeAll(IEnumerable<JsonNode> sources)
	{
		// NON_OBJECT_ROOT_PARITY (Phase-5 M1 closure): previously the generator path silently
		// reset the accumulator to empty {} on non-object source, while the runtime path silently
		// preserved prior state. Both behaviors hide malformed appsettings from the operator.
		// Fail-fast here unifies both paths under the same visible error contract.
		JsonObject accumulator = new JsonObject();
		foreach (var source in sources)
		{
			if (source == null)
			{
				continue;
			}
			if (source is not JsonObject sourceObj)
			{
				throw new JsonException(
					$"appsettings JSON sources must have a JSON object at the root; got '{source.GetValueKind()}'. " +
					"Arrays and primitives are not valid root shapes for a settings file.");
			}
			var merged = Merge(accumulator, sourceObj);
			accumulator = (JsonObject)merged!;
		}
		return accumulator;
	}

	/// <summary>
	/// Computes a diff between <paramref name="original"/> and <paramref name="current"/>, returning only changed values.
	/// Deletion semantics: when original has a value but current is null/missing, the diff contains an explicit
	/// null sentinel for that key so a subsequent <see cref="Merge"/> removes the key from the target.
	/// </summary>
	/// <param name="original">The original JSON state (baseline for comparison).</param>
	/// <param name="current">The current JSON state.</param>
	/// <returns>A JsonNode containing only the differences, or null if no changes. Explicit null values indicate deletions.</returns>
	internal static JsonNode? ComputeDiff(JsonNode? original, JsonNode? current)
	{
		// Both null = no diff
		if (original == null && current == null)
			return null;

		// Original null, current has value = entire current is the diff
		if (original == null)
			return current?.DeepClone();

		// Original has value, current null = deletion sentinel
		// This is the key deletion semantics: return explicit null
		if (current == null)
			return JsonValue.Create<object?>(null);

		// Different types = current wins entirely
		if (original.GetValueKind() != current.GetValueKind())
			return current.DeepClone();

		// Objects: recurse into properties
		if (current is JsonObject currentObj && original is JsonObject originalObj)
		{
			var diff = new JsonObject();

			// Check for changed/added properties
			foreach (var prop in currentObj)
			{
				originalObj.TryGetPropertyValue(prop.Key, out var origValue);
				var propDiff = ComputeDiff(origValue, prop.Value);
				if (propDiff != null)
					diff[prop.Key] = propDiff;
			}

			// Check for deleted properties (in original but not in current)
			foreach (var prop in originalObj)
			{
				if (!currentObj.ContainsKey(prop.Key))
				{
					// Property was deleted - add null sentinel
					diff[prop.Key] = JsonValue.Create<object?>(null);
				}
			}

			return diff.Count > 0 ? diff : null;
		}

		// Arrays: compare serialized form (full replacement if any change)
		if (current is JsonArray currentArr && original is JsonArray originalArr)
		{
			if (currentArr.ToJsonString() != originalArr.ToJsonString())
				return current.DeepClone();
			return null;
		}

		// Values: simple equality via serialized form
		if (current.ToJsonString() != original.ToJsonString())
			return current.DeepClone();

		return null;
	}
}
