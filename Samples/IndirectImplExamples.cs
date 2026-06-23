namespace Implyzer.Sample;

/// <summary>
/// Illustrates IMPL003. Interfaces decorated with [IndirectImpl(typeof(T))]
/// cannot be implemented directly by classes/structs. Instead, developers must implement
/// the interface indirectly via the designated sub-interface.
/// </summary>

[IndirectImpl(typeof(IRepository<>))]
public interface IRepositoryMarker {
    object? Find(int id);
}

// Sub-interface that extends the marker interface.
public interface IRepository<T> : IRepositoryMarker {
    new T? Find(int id);
}

// VALID: Class implements the generic sub-interface, which is allowed.
public class BookRepository : IRepository<string> {
    public string? Find(int id) {
        return $"Book {id}";
    }

    object? IRepositoryMarker.Find(int id) => Find(id);
}

// INVALID: Class attempts to implement the marker interface directly.
// Uncomment to see analyzer error IMPL003:
// "Type 'InvalidMarkerRepository' cannot implement 'IRepositoryMarker' directly"
/*
public class InvalidMarkerRepository : IRepositoryMarker {
    public object? Find(int id) {
        return null;
    }
}
*/