namespace Implyzer.Sample;

/// <summary>
/// Illustrates IMPL001 (ReferenceType vs ValueType mismatch)
/// and IMPL002 (Base class inheritance requirement).
/// </summary>

// --- Reference Type Enforcements ---
[ImplType(ImplKind.ReferenceType)]
public interface IRepository {
    void Save();
}

// VALID: Class implements the interface.
public class DbRepository : IRepository {
    public void Save() { }
}

// INVALID: Struct implements the ReferenceType interface.
// Uncomment to see analyzer error IMPL001:
// "Type 'StructRepository' must be a class because it implements interface 'IRepository' whose implementing types must be ReferenceType"
/*
public struct StructRepository : IRepository {
    public void Save() { }
}
*/


// --- Value Type Enforcements ---
[ImplType(ImplKind.ValueType)]
public interface IDataPayload {
    byte[] Bytes { get; }
}

// VALID: Struct implements the interface.
public struct MemoryPayload : IDataPayload {
    public byte[] Bytes => [];
}

// INVALID: Class implements the ValueType interface.
// Uncomment to see analyzer error IMPL001:
// "Type 'ClassPayload' must be a struct because it implements interface 'IDataPayload' whose implementing types must be ValueType"
/*
public class ClassPayload : IDataPayload {
    public byte[] Bytes => [];
}
*/


// --- Base Class Inheritance Enforcements ---
public abstract class EntityBase {
    public int Id { get; set; }
}

[ImplType(typeof(EntityBase))]
public interface IEntity { }

// VALID: Class inherits from EntityBase and implements IEntity.
public class ProductEntity : EntityBase, IEntity { }

// INVALID: Class does not inherit from EntityBase.
// Uncomment to see analyzer error IMPL002:
// "Type 'UserDto' must inherit from 'EntityBase' because it is required by interface 'IEntity'"
/*
public class UserDto : IEntity { }
*/