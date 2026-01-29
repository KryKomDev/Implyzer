using System;

namespace Implyzer;

[AttributeUsage(AttributeTargets.Interface)]
public class IndirectImplAttribute : Attribute {
    public Type? ImplementInstead { get; }
    
    public IndirectImplAttribute(Type? implementInstead = null) {
        ImplementInstead = implementInstead;
    }
}