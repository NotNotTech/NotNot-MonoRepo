// Ported from InlineComposition (https://github.com/BlackWhiteYoshi/InlineComposition)
// Original work Copyright (c) 2023 BlackWhiteYoshi, licensed under MIT License
// Modified work Copyright (c) 2025 NotNot Project and Contributors
// See LICENSE for full attribution

namespace NotNot.Mixins;

/// <summary>
/// Represents the content of an inlined method (with head declarations and without closing bracket).
/// Changed from struct to class to avoid mutable struct anti-pattern with reference type fields.
/// </summary>
public sealed class MethodEntry {
    public List<string> headList { get; } = [];
    public List<string> blockList { get; } = [];
    public string? lastBlock { get; set; }
}
