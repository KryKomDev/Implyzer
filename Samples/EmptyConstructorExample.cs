namespace Implyzer.Sample;

/// <summary>
/// Illustrates IMPL004. Interfaces configured with ImplKind.ReferenceTypeNew
/// require all implementing classes to have a public parameterless constructor.
/// This constraint is useful for dynamic creation patterns (e.g. factory registration).
/// </summary>
[ImplType(ImplKind.ReferenceTypeNew)]
public interface IFactoryComponent {
    void Initialize();
}

// VALID: Has a default parameterless constructor.
public class SqlLoggerComponent : IFactoryComponent {
    public void Initialize() {
        // Init logic...
    }
}

// INVALID: Missing a public parameterless constructor.
// Uncomment to see analyzer error IMPL004:
// "Type 'InvalidComponent' must have a public parameterless constructor because it implements interface 'IFactoryComponent'"
/*
public class InvalidComponent : IFactoryComponent {
    private readonly string _connectionString;

    public InvalidComponent(string connectionString) {
        _connectionString = connectionString;
    }

    public void Initialize() { }
}
*/