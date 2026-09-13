using System;
using Implyzer;

namespace Implyzer.Sample.Subproject;

public delegate string FormatItem<T>(T item);
public delegate bool TryParseItem<T>(string input, out T result);

public static class SubDefaults {
    public static string FormatItem<T>(T item) where T : ISubItem<T> => $"SubItem: {item}";
}

[StaticAbstract("TryParseItem", typeof(TryParseItem<object>), "TSelf", "T")]
[StaticVirtual("FormatItem", typeof(FormatItem<object>), "TSelf", "T", DefaultType = typeof(SubDefaults), ImplementInTargetTypes = true)]
public partial interface ISubItem<TSelf> where TSelf : ISubItem<TSelf> {
}

public partial class SubModel : ISubItem<SubModel> {
    public string Value { get; set; } = "";

    public static bool TryParseItem(string input, out SubModel result) {
        result = new SubModel { Value = input };
        return true;
    }

    public override string ToString() => Value;
}
