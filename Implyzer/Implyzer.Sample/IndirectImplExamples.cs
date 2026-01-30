// Implyzer
// Copyright (c) KryKom 2026

using System;

namespace Implyzer.Sample;

[IndirectImpl(typeof(IGeneric<>))]
public interface INotGeneric {
    public object? Get();
    public void Set(object? value);
}

public interface IGeneric<T> : INotGeneric {
    public new T Get();
    public void Set(T value);
    object? INotGeneric.Get() => Get();
    void INotGeneric.Set(object? value) {
        if (value is T t) Set(t);
        else throw new ArgumentOutOfRangeException();
    }
}

public class ValidImpl : IGeneric<string> {
    private string _value;
    public ValidImpl(string value) {
        _value = value;
    }

    public string Get() => _value;
    public void Set(string value) => _value = value;
}

// uncomment to see IMPL003
// public class InvalidImpl : INotGeneric {
//     public object? Get() {
//         throw new NotImplementedException();
//     }
//     public void Set(object? value) {
//         throw new NotImplementedException();
//     }
// }