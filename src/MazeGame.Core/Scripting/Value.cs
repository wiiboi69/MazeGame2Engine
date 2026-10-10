using System.Globalization;

namespace MazeGame.Core.Scripting;

public enum ValueKind : byte { Nil, Bool, Num, Str }

/// <summary>A script value: nil, boolean, number (double) or string.</summary>
public readonly struct Value : IEquatable<Value>
{
    public readonly ValueKind Kind;
    public readonly double Num;
    public readonly string? Str;

    private Value(ValueKind kind, double num, string? str) { Kind = kind; Num = num; Str = str; }

    public static readonly Value Nil = default;
    public static readonly Value True = new(ValueKind.Bool, 1, null);
    public static readonly Value False = new(ValueKind.Bool, 0, null);

    public static Value From(bool b) => b ? True : False;
    public static Value From(double d) => new(ValueKind.Num, d, null);
    public static Value From(string s) => new(ValueKind.Str, 0, s);

    public bool IsNil => Kind == ValueKind.Nil;

    /// <summary>nil, false, 0 and "" are false; everything else is true.</summary>
    public bool Truthy => Kind switch
    {
        ValueKind.Nil => false,
        ValueKind.Bool => Num != 0,
        ValueKind.Num => Num != 0,
        _ => !string.IsNullOrEmpty(Str),
    };

    public double AsNumber(double fallback = 0) => Kind switch
    {
        ValueKind.Num => Num,
        ValueKind.Bool => Num,
        ValueKind.Str => double.TryParse(Str, NumberStyles.Float, CultureInfo.InvariantCulture, out double d) ? d : fallback,
        _ => fallback,
    };

    public int AsInt(int fallback = 0) => (int)Math.Floor(AsNumber(fallback));

    public string AsString() => ToString();

    public bool Equals(Value o) => Kind == o.Kind && Kind switch
    {
        ValueKind.Nil => true,
        ValueKind.Str => string.Equals(Str, o.Str, StringComparison.Ordinal),
        _ => Num == o.Num,
    };

    public override bool Equals(object? obj) => obj is Value v && Equals(v);
    public override int GetHashCode() => HashCode.Combine(Kind, Num, Str);

    public override string ToString() => Kind switch
    {
        ValueKind.Nil => "nil",
        ValueKind.Bool => Num != 0 ? "true" : "false",
        ValueKind.Num => Num.ToString("0.######", CultureInfo.InvariantCulture),
        _ => Str ?? "",
    };

    /// <summary>Parses a saved flag string back into a value ("true", "12", "abc").</summary>
    public static Value Parse(string s)
    {
        if (s == "true") return True;
        if (s == "false") return False;
        if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double d)) return From(d);
        return From(s);
    }
}
