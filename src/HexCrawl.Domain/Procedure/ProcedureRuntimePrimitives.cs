using System.Globalization;

namespace HexCrawl.Domain.Procedure;

public enum EncounterCheckCadence
{
    None,
    PerWatch,
    PerDay,
    Custom
}

public enum TravelResolutionMode
{
    ContinuousDistance,
    HexSteps
}

public enum ActualDistanceResolutionMode
{
    Fixed,
    VariableResolved
}

public sealed record DiceRollFormula(int DiceCount, int DieSides, int Modifier = 0)
{
    public int MinimumTotal => checked(DiceCount + Modifier);
    public int MaximumTotal => checked((DiceCount * DieSides) + Modifier);

    public void Validate(string label)
    {
        if (DiceCount <= 0)
        {
            throw new InvalidOperationException($"{label} must roll at least one die.");
        }
        if (DieSides < 2 || DieSides == int.MaxValue)
        {
            throw new InvalidOperationException($"{label} must use dice with between 2 and {int.MaxValue - 1} sides.");
        }
        try
        {
            checked
            {
                _ = DiceCount + Modifier;
                _ = (DiceCount * DieSides) + Modifier;
            }
        }
        catch (OverflowException exception)
        {
            throw new InvalidOperationException($"{label} exceeds the supported integer roll range.", exception);
        }
    }
}

public sealed record DiceRollResultSet(string Canonical)
{
    public IReadOnlyList<int> Values
    {
        get
        {
            if (string.IsNullOrWhiteSpace(Canonical))
            {
                return Array.Empty<int>();
            }
            try
            {
                return Canonical
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Select(value => int.Parse(value, NumberStyles.Integer, CultureInfo.InvariantCulture))
                    .ToArray();
            }
            catch (Exception exception) when (exception is FormatException or OverflowException)
            {
                throw new InvalidOperationException("Dice-roll result set contains an invalid integer value.", exception);
            }
        }
    }

    public bool Contains(int value) => Values.Contains(value);

    public static DiceRollResultSet From(IEnumerable<int> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        return new DiceRollResultSet(string.Join(
            ",",
            values.Distinct().Order().Select(value => value.ToString(CultureInfo.InvariantCulture))));
    }
}

public sealed record TravelResolutionHelperProfile(
    DiceRollFormula Roll,
    double DistanceFactorPerRollPoint)
{
    public void Validate()
    {
        Roll.Validate("Travel helper roll");
        if (!double.IsFinite(DistanceFactorPerRollPoint) || DistanceFactorPerRollPoint <= 0)
        {
            throw new InvalidOperationException("Travel helper distance factor must be finite and positive.");
        }
        if (Roll.MinimumTotal <= 0)
        {
            throw new InvalidOperationException("Travel helper roll must always produce a positive multiplier.");
        }
    }
}

public sealed record NavigationResolutionHelperProfile(DiceRollFormula CheckRoll)
{
    public void Validate() => CheckRoll.Validate("Navigation helper check roll");
}

public sealed record EncounterResolutionHelperProfile(
    DiceRollFormula CheckRoll,
    DiceRollResultSet WanderingResults,
    DiceRollResultSet KeyedLocationResults,
    int TimingSlots)
{
    public void Validate()
    {
        CheckRoll.Validate("Encounter helper check roll");
        if (TimingSlots <= 0 || TimingSlots == int.MaxValue)
        {
            throw new InvalidOperationException("Encounter helper timing slots must be between 1 and int.MaxValue - 1.");
        }

        var minimum = CheckRoll.MinimumTotal;
        var maximum = CheckRoll.MaximumTotal;
        var wandering = WanderingResults.Values.Distinct().ToHashSet();
        var keyed = KeyedLocationResults.Values.Distinct().ToHashSet();
        if (wandering.Any(result => result < minimum || result > maximum)
            || keyed.Any(result => result < minimum || result > maximum))
        {
            throw new InvalidOperationException("Encounter helper trigger results must be possible totals for its check roll.");
        }
        if (wandering.Overlaps(keyed))
        {
            throw new InvalidOperationException("Encounter helper wandering and keyed-location trigger results can not overlap.");
        }
    }
}

public sealed record ProcedureResolutionHelperProfile(
    TravelResolutionHelperProfile? Travel = null,
    NavigationResolutionHelperProfile? Navigation = null,
    EncounterResolutionHelperProfile? Encounter = null)
{
    public void Validate()
    {
        Travel?.Validate();
        Navigation?.Validate();
        Encounter?.Validate();
    }
}
