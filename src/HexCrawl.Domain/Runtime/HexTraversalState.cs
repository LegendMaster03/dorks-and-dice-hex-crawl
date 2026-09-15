using HexCrawl.Domain.Spatial;

namespace HexCrawl.Domain.Runtime;

public readonly record struct HexDirection
{
    public HexDirection(int value)
    {
        if (value is < 0 or > 5)
        {
            throw new ArgumentOutOfRangeException(nameof(value), "Hex direction must be between 0 and 5.");
        }

        Value = value;
    }

    public int Value { get; }

    public HexDirection Rotate(int steps) => new(Mod(Value + steps, 6));

    public HexDirection Opposite => Rotate(3);

    public int SeparationFrom(HexDirection other)
    {
        var delta = Math.Abs(Value - other.Value);
        return Math.Min(delta, 6 - delta);
    }

    public override string ToString() => Value.ToString(System.Globalization.CultureInfo.InvariantCulture);

    private static int Mod(int value, int modulus) => ((value % modulus) + modulus) % modulus;
}

public sealed record HexTraversalState
{
    public required HexCoordinate CurrentHex { get; init; }

    // Direction used to enter CurrentHex. Null means the expedition began at an
    // abstract point inside the hex rather than crossing a known entry face.
    public HexDirection? EntryDirection { get; init; }

    public HexDirection? LastTravelDirection { get; init; }
    public required DistanceMeasure Progress { get; init; }
    public DistanceMeasure? CurrentExitRequirement { get; init; }

    public static HexTraversalState StartingIn(HexCoordinate hex, DistanceUnit unit) => new()
    {
        CurrentHex = hex,
        Progress = new DistanceMeasure(0, unit)
    };
}
