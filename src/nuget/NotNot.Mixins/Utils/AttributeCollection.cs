// Ported from InlineComposition (https://github.com/BlackWhiteYoshi/InlineComposition)
// Original work Copyright (c) 2023 BlackWhiteYoshi, licensed under MIT License
// Modified work Copyright (c) 2025 NotNot Project and Contributors
// See LICENSE for full attribution

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Immutable;

namespace NotNot.Mixins;

public readonly struct AttributeCollection(
    TypeDeclarationSyntax inlineClass,
    AttributeData inlineAttribute,
    TypeDeclarationSyntax?[] baseClassArray,
    AttributeData?[] baseAttributeArray,
    string[] baseClassNames,
    bool[] baseClassHasSource) : IEquatable<AttributeCollection> {
    public readonly TypeDeclarationSyntax inlineClass = inlineClass;
    public readonly AttributeData inlineAttribute = inlineAttribute;
    public readonly ImmutableArray<TypeDeclarationSyntax?> baseClasses = ImmutableArray.Create(baseClassArray);
    public readonly ImmutableArray<AttributeData?> baseAttributes = ImmutableArray.Create(baseAttributeArray);
    /// <summary>Fully qualified names of base classes for diagnostic reporting</summary>
    public readonly ImmutableArray<string> baseClassNames = ImmutableArray.Create(baseClassNames);
    /// <summary>Whether each base class has source available (false = external assembly)</summary>
    public readonly ImmutableArray<bool> baseClassHasSource = ImmutableArray.Create(baseClassHasSource);

    public readonly override bool Equals(object? obj) {
        if (obj is not AttributeCollection collection)
            return false;

        return Equals(collection);
    }

    public readonly bool Equals(AttributeCollection other) {
        if (inlineClass != other.inlineClass)
            return false;

        if (!inlineAttribute.Equals(other.inlineAttribute))
            return false;

        if (!baseClasses.SequenceEqual(other.baseClasses))
            return false;

        if (!baseAttributes.SequenceEqual(other.baseAttributes))
            return false;

        if (!baseClassNames.SequenceEqual(other.baseClassNames))
            return false;

        if (!baseClassHasSource.SequenceEqual(other.baseClassHasSource))
            return false;

        return true;
    }

    public static bool operator ==(AttributeCollection left, AttributeCollection right) => left.Equals(right);

    public static bool operator !=(AttributeCollection left, AttributeCollection right) => !(left == right);

    public readonly override int GetHashCode() {
        int hashCode = inlineClass.GetHashCode();

        hashCode = Combine(hashCode, inlineAttribute.GetHashCode());

        foreach (TypeDeclarationSyntax? baseClass in baseClasses)
            hashCode = Combine(hashCode, baseClass?.GetHashCode() ?? 0);

        foreach (AttributeData? attribute in baseAttributes)
            hashCode = Combine(hashCode, attribute?.GetHashCode() ?? 0);

        return hashCode;


        static int Combine(int h1, int h2) {
            uint rol5 = ((uint)h1 << 5) | ((uint)h1 >> 27);
            return ((int)rol5 + h1) ^ h2;
        }
    }
}
