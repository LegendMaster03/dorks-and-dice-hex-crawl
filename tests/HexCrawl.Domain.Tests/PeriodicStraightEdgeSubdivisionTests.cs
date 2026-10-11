using System.Text.Json;
using HexCrawl.Domain.Spatial;

namespace HexCrawl.Domain.Tests;

public sealed class PeriodicStraightEdgeSubdivisionTests
{
    private const string OffsetSquares =
        "<12:2 4 6 8 10 12,12 3 5 7 9 11,8 7 10 9 12 11:6,3 3>";

    private static PeriodicTopologyWitness ReferenceTopology()
    {
        var canonical = DelaneyDressTopology.Inspect(OffsetSquares).Symbol!.Canonical;
        var edges = new[]
        {
            new PeriodicEdgeInterface(0, 0, "square", new(0, -1), 3),
            new PeriodicEdgeInterface(1, 1, "square", new(1, -1), 4),
            new PeriodicEdgeInterface(2, 2, "square", new(1, 0), 5),
            new PeriodicEdgeInterface(3, 3, "square", new(0, 1), 0),
            new PeriodicEdgeInterface(4, 4, "square", new(-1, 1), 1),
            new PeriodicEdgeInterface(5, 5, "square", new(-1, 0), 2)
        };
        return new PeriodicTopologyWitness(
            PeriodicTopologyContractVersion.Current, canonical, canonical,
            [new PeriodicMotifCell("square", edges)], "test:offset-square-exact-cover");
    }

    private static readonly PeriodicStraightEdgeSubdivision[] Splits =
    [
        new("square", 1, .5),
        new("square", 4, .5)
    ];

    private static PeriodicWorldTiling ReferenceWorld()
    {
        var topology = ReferenceTopology();
        topology.ValidateAdjacency();
        var grouped = PeriodicStraightEdgeGeometry.Apply(
            topology, PeriodicStraightEdgeGeometry.Normalize(topology, Splits));
        var metric = new PeriodicMetricRealization(
            "world-unit", new(1, 0), new(.5, 1),
            new Dictionary<string, IReadOnlyList<TilingWorldPoint>>
            {
                ["square"] =
                [
                    new(0, 0), new(.5, 0), new(1, 0),
                    new(1, 1), new(.5, 1), new(0, 1)
                ]
            }, Splits.Select(PeriodicStraightEdgeGeometry.Constraint).ToArray());
        var world = new PeriodicWorldTiling(Guid.NewGuid(), grouped, metric, new WorldPoint(0, 0));
        world.Validate();
        return world;
    }

    [Fact]
    public void ExactOffsetSquareIsFourSidedWithSixReciprocalAtomicInterfaces()
    {
        var world = ReferenceWorld();
        var cell = Assert.Single(world.Topology.MotifCells);
        Assert.Equal(6, cell.Boundary.Count);
        Assert.Equal(new[] { 0, 0, 1, 2, 2, 3 },
            cell.Boundary.Select(edge => edge.BoundarySideIndex));
        Assert.Equal(4, cell.Boundary.Select(edge => edge.BoundarySideIndex).Distinct().Count());
        var polygon = world.Realization.Polygons["square"];
        for (int vertex = 0; vertex < polygon.Count; vertex++)
        {
            var a = polygon[vertex];
            var b = polygon[(vertex + 1) % polygon.Count];
            var delta = new TilingWorldPoint(b.X - a.X, b.Y - a.Y);
            Assert.True(Math.Abs(delta.X) < 1e-12 || Math.Abs(delta.Y) < 1e-12);
        }
        Assert.Equal(1, polygon.Max(p => p.X) - polygon.Min(p => p.X), 10);
        Assert.Equal(1, polygon.Max(p => p.Y) - polygon.Min(p => p.Y), 10);
        Assert.Equal(1, Math.Abs((world.Realization.TranslationU.X * world.Realization.TranslationV.Y)
            - (world.Realization.TranslationU.Y * world.Realization.TranslationV.X)), 10);

        var address = new PeriodicCellAddress("square", new(4, -7));
        var boundaries = world.Boundaries(address);
        Assert.Equal(6, boundaries.Count);
        Assert.Equal(6, boundaries.Select(edge => edge.To).Distinct().Count());
        foreach (var edge in boundaries)
        {
            var peer = world.Reciprocal(edge);
            Assert.Equal(edge.From, peer.To);
            Assert.Equal(edge.To, peer.From);
            Assert.Equal(edge.Start, peer.End);
            Assert.Equal(edge.End, peer.Start);
            Assert.Equal(edge.InterfaceIndex, peer.ReciprocalInterfaceIndex);
            Assert.Equal(edge.ReciprocalInterfaceIndex, peer.InterfaceIndex);
        }
    }

    [Fact]
    public void EachHalfOfBothSplitSidesCrossesAndReturnsAcrossLatticeRegions()
    {
        var world = ReferenceWorld();
        foreach (var address in new[]
        {
            new PeriodicCellAddress("square", new(0, 0)),
            new PeriodicCellAddress("square", new(-4, 7))
        })
        {
            var from = world.Resolve(address);
            foreach (var edge in world.Boundaries(address))
            {
                var midpoint = new WorldPoint((edge.Start.X + edge.End.X) / 2,
                    (edge.Start.Y + edge.End.Y) / 2);
                var state = new PeriodicCellTraversal
                {
                    CurrentCell = from.Id,
                    Position = from.Center,
                    TravelHeading = midpoint - from.Center,
                    SelectedExitInterfaceIndex = edge.InterfaceIndex
                };
                var advanced = PeriodicCellTraversalGeometry.Advance(world, state, 3);
                Assert.Equal(CellCrossingStatus.Crosses, advanced.Crossing.Status);
                Assert.Equal(edge.InterfaceIndex, advanced.Crossing.Exit!.InterfaceIndex);
                Assert.Equal(edge.To, advanced.Traversal.CurrentCell);
                Assert.Equal(edge.ReciprocalInterfaceIndex, advanced.Traversal.EntryInterfaceIndex);
                advanced.Traversal.Validate(world);

                var destination = world.Resolve(edge.To.Address);
                var returnState = new PeriodicCellTraversal
                {
                    CurrentCell = destination.Id,
                    Position = destination.Center,
                    TravelHeading = midpoint - destination.Center,
                    SelectedExitInterfaceIndex = edge.ReciprocalInterfaceIndex
                };
                var returned = PeriodicCellTraversalGeometry.Advance(world, returnState, 3);
                Assert.Equal(CellCrossingStatus.Crosses, returned.Crossing.Status);
                Assert.Equal(from.Id, returned.Traversal.CurrentCell);
                Assert.Equal(edge.InterfaceIndex, returned.Traversal.EntryInterfaceIndex);
            }
        }
    }

    [Fact]
    public void TjunctionRequiresAnExplicitAndGeometricallyValidExit()
    {
        var world = ReferenceWorld();
        var square = world.Resolve(new PeriodicCellAddress("square", new(0, 0)));
        var split = square.Polygon[1];
        var state = new PeriodicCellTraversal
        {
            CurrentCell = square.Id,
            Position = square.Center,
            TravelHeading = split - square.Center
        };
        var unresolved = PeriodicCellTraversalGeometry.Advance(world, state, 3);
        Assert.Equal(CellCrossingStatus.RequiresAdjudication, unresolved.Crossing.Status);
        Assert.Equal(0, unresolved.ConsumedWorldUnits);
        Assert.Equal(state, unresolved.Traversal);
        // Selecting one of two touching edges does not authorize a phantom
        // destination if the heading runs along the neighbors' common edge.
        var selected = PeriodicCellTraversalGeometry.NextCrossing(
            world, state with { SelectedExitInterfaceIndex = 0 });
        Assert.Equal(CellCrossingStatus.RequiresAdjudication, selected.Status);
        Assert.Equal(WorldCellLookupStatus.Ambiguous, world.Containing(split).Status);
    }

    [Fact]
    public void RejectsInvalidFractionsDisconnectedGroupsAndFalseCollinearity()
    {
        var topology = ReferenceTopology();
        topology.ValidateAdjacency();
        Assert.Throws<ArgumentException>(() => PeriodicStraightEdgeGeometry.Normalize(topology,
            [new("square", 1, 0)]));
        Assert.Throws<ArgumentException>(() => PeriodicStraightEdgeGeometry.Normalize(topology,
            [new("square", 1, .5), new("square", 1, .6)]));
        Assert.Throws<ArgumentException>(() => PeriodicStraightEdgeGeometry.Normalize(topology,
            [new("missing", 1, .5)]));
        Assert.Throws<ArgumentException>(() => PeriodicStraightEdgeGeometry.Normalize(topology,
            Enumerable.Range(0, 6).Select(i =>
                new PeriodicStraightEdgeSubdivision("square", i, .5)).ToArray()));

        var world = ReferenceWorld();
        var moved = world.Realization.Polygons["square"].ToArray();
        moved[1] = new(.6, .05);
        var bent = world.Realization with
        {
            Polygons = new Dictionary<string, IReadOnlyList<TilingWorldPoint>> { ["square"] = moved }
        };
        Assert.Throws<InvalidOperationException>(() =>
            PeriodicMetricWitnessValidator.Validate(world.Topology, bent));

        var wrongRatio = world.Realization with
        {
            Constraints = [new TilingMetricConstraint(
                "straight-edge-fraction:square:1", "ratio", .25, .25)]
        };
        Assert.Throws<InvalidOperationException>(() =>
            PeriodicMetricWitnessValidator.Validate(world.Topology, wrongRatio));

        var invalidLabels = topology with
        {
            MotifCells = [topology.MotifCells[0] with
            {
                Boundary = topology.MotifCells[0].Boundary.Select((edge, i) =>
                    edge with { BoundarySideIndex = i % 2 }).ToArray()
            }]
        };
        Assert.Throws<InvalidOperationException>(invalidLabels.ValidateAdjacency);
    }

    [Fact]
    public void SerializationRetainsAtomicIdsGroupingRatiosAndUncalibratedTravel()
    {
        var world = ReferenceWorld();
        var copy = JsonSerializer.Deserialize<PeriodicWorldTiling>(
            JsonSerializer.Serialize(world))!;
        copy.Validate();
        Assert.Equal(JsonSerializer.Serialize(world.Topology), JsonSerializer.Serialize(copy.Topology));
        Assert.Equal(JsonSerializer.Serialize(world.Realization), JsonSerializer.Serialize(copy.Realization));
        Assert.Null(copy.PhysicalDistancePerWorldUnit);
        var at = new PeriodicCellAddress("square", new(2, -2));
        Assert.Equal(world.Boundaries(at).Select(e => e.InterfaceIndex),
            copy.Boundaries(at).Select(e => e.InterfaceIndex));
    }

    [Fact]
    public void HarmonicConstrainedConstructorDoesNotSilentlyIgnoreShape()
    {
        var generated = DelaneyDressGeneralQuotientUnfolding.Construct(OffsetSquares);
        Assert.Equal(DelaneyDressCoverStatus.Constructed, generated.Status);
        var motif = Assert.Single(generated.Witness!.MotifCells);
        Assert.Equal(6, motif.Boundary.Count);
        var result = DelaneyDressHarmonicMetricRealization.Construct(
            OffsetSquares, 1, "world-unit", straightEdgeSubdivisions:
            [
                new(motif.Id, 1, .5), new(motif.Id, 4, .5)
            ]);
        if (result.Status == "realized")
        {
            Assert.True(PeriodicMetricWitnessValidator.Validate(result.Topology!, result.Realization!));
            Assert.Equal(4, result.Topology!.MotifCells[0].Boundary
                .Select(e => e.BoundarySideIndex).Distinct().Count());
        }
        else
        {
            Assert.Equal("unresolved-geometry", result.Status);
            Assert.Null(result.Realization);
            Assert.False(string.IsNullOrWhiteSpace(result.Reason));
        }
    }
}
