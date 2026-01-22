using System;
// ReSharper disable All

namespace ImplTypeCheck;

#nullable enable

public enum ImplKind {
    ReferenceType,
    ValueType
}

[AttributeUsage(AttributeTargets.Interface)]
public sealed class ImplTypeAttribute : Attribute {
    public ImplTypeAttribute(ImplKind kind) {
        Kind = kind;
    }

    public ImplTypeAttribute(Type baseType) {
        Kind = ImplKind.ReferenceType;
        BaseType = baseType;
    }

    public ImplKind Kind { get; }
    public Type? BaseType { get; }
}

#nullable restore