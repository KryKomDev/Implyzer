namespace Implyzer.Sample;

[UseInstead(typeof(Usages))]
public class UseInsteadExamples {
    
    [UseInstead(typeof(UseInsteadExamples), [typeof(int)])]
    public UseInsteadExamples() { }
    public UseInsteadExamples(int i) { }

    [UseInstead(nameof(Method2))]
    public void Method1() { }
    public void Method2() { }

    [UseInstead(nameof(Field2))]
    public int Field1 = 0;
    public int Field2 = 0;
    
    [UseInstead(nameof(Property2))]
    public int Property1 { get; set; }
    public int Property2 { get; set; }
}

public class Usages {
    public static void Method() {
        var uie = new UseInsteadExamples(1);
        uie.Method1();
        var f = uie.Field1;
        var p = uie.Property1;
    }
}