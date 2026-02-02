namespace Implyzer.Sample;

[ImplType(ImplKind.ReferenceTypeNew)]
public interface I {
    
}

public class Valid : I {
    public Valid() {
        
    }
}

// Uncomment to see IMPL004
// public class Invalid : I {
//     public Invalid(object? param) {
//         
//     }
// }