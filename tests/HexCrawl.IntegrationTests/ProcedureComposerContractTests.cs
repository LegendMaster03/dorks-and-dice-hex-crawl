using HexCrawl.Application;
using HexCrawl.Domain.Procedure;
using HexCrawl.Web.Modules.Procedures;

namespace HexCrawl.IntegrationTests;

public sealed class ProcedureComposerContractTests
{
    [Fact]
    public void ContractDistinguishesNativeDeclarativeAndUnsupportedMechanics()
    {
        var dnd2024 = CrawlProcedureCatalog.Resolve(CrawlProcedureCatalog.Dnd2024PresetKey).MaterializeGeneric();
        var dndContract = ProcedureComposerContract.From(new ProcedureComposerDraft(
            dnd2024.Procedure,
            dnd2024.Origin,
            null,
            null,
            dnd2024.Procedure.EvaluateDependencies()));

        var native = dndContract.Modules.Single(module =>
            module.ModuleKey == GenericProcedureCatalog.TimeIntervalModule);
        var declarative = dndContract.Modules.Single(module =>
            module.ModuleKey == GenericProcedureCatalog.TerrainMovementModule);
        Assert.Equal("Native", native.Mechanic.ExecutionSupport);
        Assert.Equal("Declarative", declarative.Mechanic.ExecutionSupport);

        var simple = CrawlProcedureCatalog.Resolve("simple-fixed-distance").MaterializeGeneric();
        var selected = simple.Procedure.Modules.Single(module =>
            module.Module.Key == GenericProcedureCatalog.MovementResolutionModule);
        const string futureMechanicKey = "future-movement-resolution";
        var futureModule = selected with
        {
            Module = selected.Module with
            {
                CompatibleMechanicTypes = selected.Module.CompatibleMechanicTypes
                    .Concat([futureMechanicKey])
                    .ToArray()
            },
            Mechanic = selected.Mechanic with
            {
                Key = futureMechanicKey,
                DisplayName = "Future movement resolution",
                ExecutionHandler = "future.runtime.handler",
                Version = 42
            }
        };
        var future = simple.Procedure with
        {
            ProcedureId = Guid.NewGuid(),
            Modules = simple.Procedure.Modules
                .Select(module =>
                    module.Module.Key == GenericProcedureCatalog.MovementResolutionModule
                        ? futureModule
                        : module)
                .ToArray()
        };
        future.Validate();

        var futureContract = ProcedureComposerContract.From(new ProcedureComposerDraft(
            future,
            null,
            null,
            null,
            future.EvaluateDependencies()));
        var unsupported = futureContract.Modules.Single(module =>
            module.ModuleKey == GenericProcedureCatalog.MovementResolutionModule);

        Assert.Equal(futureMechanicKey, unsupported.Mechanic.Key);
        Assert.Equal(42, unsupported.Mechanic.Version);
        Assert.Equal("future.runtime.handler", unsupported.Mechanic.ExecutionHandler);
        Assert.Equal("Unsupported", unsupported.Mechanic.ExecutionSupport);
        Assert.DoesNotContain(unsupported.Alternatives, alternative =>
            alternative.Key == futureMechanicKey && alternative.Version == 42);
    }
}
