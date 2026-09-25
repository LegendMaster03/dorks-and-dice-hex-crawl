using System.Text.Json.Serialization;

namespace HexCrawl.Domain.Spatial;

public enum DistanceUnitKind
{
    Mile,
    Kilometer,
    Custom
}

public readonly record struct DistanceUnit
{
    [JsonConstructor]
    public DistanceUnit(DistanceUnitKind kind, string symbol, double? metersPerUnit)
    {
        Kind = kind;
        Symbol = symbol;
        MetersPerUnit = metersPerUnit;
    }

    public DistanceUnitKind Kind { get; }
    public string Symbol { get; }
    public double? MetersPerUnit { get; }

    public static DistanceUnit Miles { get; } = new(DistanceUnitKind.Mile, "mi", 1609.344);
    public static DistanceUnit Kilometers { get; } = new(DistanceUnitKind.Kilometer, "km", 1000);

    public static DistanceUnit Custom(string symbol, double? metersPerUnit = null)
    {
        if (string.IsNullOrWhiteSpace(symbol))
        {
            throw new ArgumentException("A custom distance unit requires a symbol.", nameof(symbol));
        }

        if (metersPerUnit.HasValue
            && (!double.IsFinite(metersPerUnit.Value) || metersPerUnit.Value <= 0))
        {
            throw new ArgumentOutOfRangeException(
                nameof(metersPerUnit),
                "A custom distance conversion must be finite and positive.");
        }

        return new DistanceUnit(DistanceUnitKind.Custom, symbol.Trim(), metersPerUnit);
    }
}

public readonly record struct DistanceMeasure
{
    [JsonConstructor]
    public DistanceMeasure(double value, DistanceUnit unit)
    {
        if (value < 0 || double.IsNaN(value) || double.IsInfinity(value))
        {
            throw new ArgumentOutOfRangeException(nameof(value), "Distance must be finite and non-negative.");
        }

        Value = value;
        Unit = unit;
    }

    public double Value { get; }
    public DistanceUnit Unit { get; }

    public DistanceMeasure ConvertTo(DistanceUnit target)
    {
        if (Unit == target)
        {
            return this;
        }

        if (Unit.MetersPerUnit is null || target.MetersPerUnit is null)
        {
            throw new InvalidOperationException("Both distance units must define a physical conversion factor.");
        }

        var meters = Value * Unit.MetersPerUnit.Value;
        return new DistanceMeasure(meters / target.MetersPerUnit.Value, target);
    }
}
