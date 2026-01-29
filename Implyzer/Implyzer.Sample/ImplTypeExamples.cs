namespace Implyzer.Sample;

[ImplType(ImplKind.ReferenceType)]
public interface IService {
    void Serve();
}

[ImplType(ImplKind.ValueType)]
public interface IData {
    int Value { get; }
}

public class BaseEntity { }

[ImplType(typeof(BaseEntity))]
public interface IEntity { }

// Should be valid
public class EmailService : IService {
    public void Serve() { }
}


// Should be invalid (struct implementing ReferenceType interface)
// Uncomment to see error IMPL001
// public struct StructService : IService
// {
//     public void Serve() { }
// }


// Should be valid
public struct PointData : IData {
    public int Value => 0;
}


// Should be invalid (class implementing ValueType interface)
// Uncomment to see error IMPL001
// public class ClassData : IData
// {
//     public int Value => 0;
// }


// Should be valid (inherits from BaseEntity)
public class UserEntity : BaseEntity, IEntity { }


// Should be invalid (does not inherit from BaseEntity)
// Uncomment to see error IMPL002
// public class OrderEntity : IEntity
// {
// }
