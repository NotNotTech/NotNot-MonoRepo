// Ported from InlineComposition (https://github.com/BlackWhiteYoshi/InlineComposition)
// Original work Copyright (c) 2023 BlackWhiteYoshi, licensed under MIT License
// Modified work Copyright (c) 2025 NotNot Project and Contributors
// See LICENSE for full attribution

using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace NotNot.Mixins;

/// <summary>
/// Holds a reference to a class/struct node and its generic arguments as string array
/// </summary>
public struct BaseClassNode() {
    public TypeDeclarationSyntax? baseClass;
    public bool mapBaseType = false;
    public bool ignoreInheritenceAndImplements = false;
    public bool inlineAttributes = false;
    public string[] genericArguments = [];
}
