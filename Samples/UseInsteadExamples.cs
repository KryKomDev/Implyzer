namespace Implyzer.Sample;

/// <summary>
/// Illustrates IMPL005. The [UseInstead] attribute provides diagnostics
/// suggesting alternative APIs/classes/methods/constructors/properties to developers,
/// complete with automated Code Fix support in modern IDEs.
/// </summary>

[UseInstead(typeof(ModernCalculator))]
public class LegacyCalculator {
    [UseInstead(nameof(Add))]
    public int ComputeSum(int a, int b) => Add(a, b);

    public int Add(int a, int b) => a + b;

    [UseInstead(typeof(LegacyCalculator), [typeof(int)])]
    public LegacyCalculator() { }

    public LegacyCalculator(int precision) { }
}

public class ModernCalculator {
    public int Add(int a, int b) => a + b;
}

public class Usage {
    public static void Run() {
        // Info IMPL005 on constructor: "Use 'LegacyCalculator(int)' instead of 'LegacyCalculator()'"
        var calc = new LegacyCalculator();

        // Info IMPL005 on method: "Use 'Add' instead of 'ComputeSum'"
        var sum = calc.ComputeSum(10, 20);

        // Info IMPL005 on class type: "Use 'ModernCalculator' instead of 'LegacyCalculator'"
        var legacy = new LegacyCalculator(5);
    }
}