using HexCrawl.Domain.Spatial;

namespace HexCrawl.Web.Api;

public sealed record DistanceUnitContract(DistanceUnitKind Kind, string Symbol, double? MetersPerUnit)
{
    public static DistanceUnitContract From(DistanceUnit unit) => new(unit.Kind, unit.Symbol, unit.MetersPerUnit);

    public DistanceUnit ToDomain() => Kind switch
    {
        DistanceUnitKind.Mile => DistanceUnit.Miles,
        DistanceUnitKind.Kilometer => DistanceUnit.Kilometers,
        DistanceUnitKind.Custom => DistanceUnit.Custom(Symbol, MetersPerUnit),
        _ => throw new ArgumentOutOfRangeException(nameof(Kind))
    };
}

public sealed record DistanceContract(double Value, DistanceUnitContract Unit)
{
    public static DistanceContract From(DistanceMeasure value) => new(value.Value, DistanceUnitContract.From(value.Unit));
}

