using System.Text.Json;
using HexCrawl.Domain.Procedure;
using HexCrawl.Domain.Runtime;
using HexCrawl.Domain.Spatial;

namespace HexCrawl.Domain.Tests;

public sealed class PeriodicVertexAdjacencyTests
{
    private const string Triangle = "<1:1,1,1:3,6>";
    private const string Square = "<1:1,1,1:4,4>";
    private const string Hexagon = "<1:1,1,1:6,3>";
    private const string Mixed = "<10:2 5 4 6 7 8 10,1 4 6 5 9 10,3 5 7 8 9 10:3 3 4,5 5>";

    [Theory]
    [InlineData(Triangle, true)]
    [InlineData(Square, true)]
    [InlineData(Hexagon, false)]
    [InlineData(Mixed, true)]
    public void VertexOnlyContactsComeFromReciprocalGeometryNotTilingNames(
        string symbol, bool expectedVertexOnly)
    {
        var world = CreateWorld(symbol);
        Assert.Equal(expectedVertexOnly, world.HasVertexOnlyContacts());
        foreach (var motif in world.Topology.MotifCells)
        {
            var current = new PeriodicCellAddress(motif.Id, new(3, -4));
            var edges = world.Neighbors(current);
            var vertices = world.VertexOnlyContacts(current);
            Assert.All(vertices, contact =>
            {
                Assert.Equal(new WorldCellId(world.Id, current), contact.From);
                Assert.DoesNotContain(contact.To, edges);
                Assert.DoesNotContain(contact.To, world.Neighbors(current));
                Assert.Contains(world.VertexOnlyContacts(contact.To.Address), reverse =>
                    reverse.To == contact.From
                    && reverse.FromVertexIndex == contact.ToVertexIndex
                    && reverse.ToVertexIndex == contact.FromVertexIndex);
            });
            Assert.Equal(edges, world.MovementNeighbors(current, CellAdjacencyMode.EdgeOnly));
            Assert.True(edges.Concat(vertices.Select(c => c.To)).ToHashSet()
                .SetEquals(world.MovementNeighbors(current, CellAdjacencyMode.EdgeAndVertex)));
        }
    }

    [Fact]
    public void SquareDiagonalCrossingRequiresOptInAndPersistsWithoutInventingAnEdge()
    {
        var world = CreateWorld(Square);
        var startCell = world.Resolve(new PeriodicCellAddress(
            world.Topology.MotifCells[0].Id, new(0, 0)));
        var contact = Assert.Single(
            world.VertexOnlyContacts(startCell.Id.Address),
            c => c.FromVertexIndex == 0);
        var start = new PeriodicCellTraversal
        {
            CurrentCell = startCell.Id,
            Position = startCell.Center,
            TravelHeading = contact.Point - startCell.Center
        };

        var forbidden = PeriodicTraversalExecution.AdvanceCellSteps(
            world, start, 1, true, CellAdjacencyMode.EdgeOnly);
        Assert.True(forbidden.RequiresAdjudication);
        Assert.Empty(forbidden.Transitions);
        Assert.Equal(start, forbidden.Traversal);

        var allowed = PeriodicTraversalExecution.AdvanceCellSteps(
            world, start, 1, true, CellAdjacencyMode.EdgeAndVertex);
        var transition = Assert.Single(allowed.Transitions);
        Assert.Null(transition.ExitInterfaceIndex);
        Assert.Null(transition.EntryInterfaceIndex);
        Assert.Equal(contact, transition.VertexContact);
        Assert.Equal(contact.To, allowed.Traversal.CurrentCell);
        Assert.Equal(contact.From, allowed.Traversal.EnteredFrom);
        Assert.Null(allowed.Traversal.EntryInterfaceIndex);
        Assert.Equal(contact.ToVertexIndex, allowed.Traversal.EntryVertexIndex);
        Assert.InRange(allowed.Traversal.Position.DistanceTo(contact.Point), 0, 1e-8);
        allowed.Traversal.Validate(world);

        var copy = JsonSerializer.Deserialize<PeriodicCellTraversal>(
            JsonSerializer.Serialize(allowed.Traversal))!;
        Assert.Equal(allowed.Traversal, copy);
        copy.Validate(world);
    }

    [Fact]
    public void DiagonalTravelAlsoConsumesItsRealContinuousDistance()
    {
        var world = CreateWorld(Square, calibrated: true);
        var startCell = world.Resolve(new PeriodicCellAddress(
            world.Topology.MotifCells[0].Id, new(0, 0)));
        var contact = world.VertexOnlyContacts(startCell.Id.Address)[0];
        var start = new PeriodicCellTraversal
        {
            CurrentCell = startCell.Id,
            Position = startCell.Center,
            TravelHeading = contact.Point - startCell.Center,
            SelectedExitVertexNeighbor = contact.To
        };
        var distance = start.Position.DistanceTo(contact.Point);
        var travel = PeriodicTraversalExecution.AdvanceDistance(
            world, start, new DistanceMeasure(distance * 2, DistanceUnit.Miles), false,
            CellAdjacencyMode.EdgeAndVertex);
        Assert.Equal(contact.To, travel.Traversal.CurrentCell);
        Assert.Equal(1, travel.CompletedCellSteps);
        Assert.InRange(travel.ConsumedWorldUnits, distance - 1e-8, distance + 1e-8);
        Assert.InRange(travel.ConsumedPhysicalDistance!.Value.Value,
            distance * 2 - 1e-8, distance * 2 + 1e-8);
        Assert.Single(travel.Transitions);
    }

    [Fact]
    public void RuntimePolicyControlsAdjacencyWithoutASeparateGeometryExecutor()
    {
        var world = CreateWorld(Square);
        var startCell = world.Resolve(new PeriodicCellAddress(
            world.Topology.MotifCells[0].Id, new(0, 0)));
        var contact = world.VertexOnlyContacts(startCell.Id.Address)[0];
        var start = new PeriodicCellTraversal
        {
            CurrentCell = startCell.Id,
            Position = startCell.Center,
            TravelHeading = contact.Point - startCell.Center
        };
        var engine = new CrawlRuntimeEngine();
        foreach (var (mode, success) in new[]
        {
            (CellAdjacencyMode.EdgeOnly, false),
            (CellAdjacencyMode.EdgeAndVertex, true)
        })
        {
            var procedure = new TestProcedureFixture
            {
                TravelResolution = TravelResolutionMode.CellSteps,
                Adjacency = mode
            }.Materialize() with { TilingDsSymbol = Square };
            Assert.Equal(mode, GenericProcedureRuntime.Bind(procedure).Movement.Adjacency);
            var result = engine.ResolveCellTravel(
                world, procedure, start,
                ResolvedTravelAmount.CellTransitions(1, ResolutionProvenance.ProcedureDefault),
                true);
            Assert.Equal(success, result.CompletedCellSteps == 1);
            Assert.Equal(!success, result.RequiresAdjudication);
        }
    }

    [Fact]
    public void ASelectedVertexNeighborMustBeGeometricallyReachable()
    {
        var world = CreateWorld(Square);
        var start = world.Resolve(new PeriodicCellAddress(
            world.Topology.MotifCells[0].Id, new(0, 0)));
        var edge = world.Boundaries(start.Id.Address)[0];
        var invalid = new PeriodicCellTraversal
        {
            CurrentCell = start.Id,
            Position = start.Center,
            TravelHeading = world.Resolve(edge.To.Address).Center - start.Center,
            SelectedExitVertexNeighbor = edge.To
        };
        Assert.Throws<InvalidOperationException>(() => invalid.Validate(world));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            world.MovementNeighbors(start.Id.Address, (CellAdjacencyMode)99));
    }

    private static PeriodicWorldTiling CreateWorld(string symbol, bool calibrated = false)
    {
        var generated = DelaneyDressHarmonicMetricRealization.Construct(symbol, 1.5, "world-unit");
        Assert.Equal("realized", generated.Status);
        var world = new PeriodicWorldTiling(Guid.NewGuid(), generated.Topology!,
            generated.Realization!, new WorldPoint(12, -7),
            PhysicalDistancePerWorldUnit: calibrated
                ? new DistanceMeasure(2, DistanceUnit.Miles) : null);
        world.Validate();
        return world;
    }
}
