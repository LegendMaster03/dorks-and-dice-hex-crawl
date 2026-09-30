using HexCrawl.Application.Rules;
using HexCrawl.Domain.Procedure;
using HexCrawl.Domain.Runtime;

namespace HexCrawl.Application.Tests;

public sealed class OptionalProviderArchitectureTests
{
    [Fact]
    public void ApplicationAssemblyHasNoRetiredRulesCoreApplicationContracts()
    {
        var assembly = typeof(ITravelEnvironmentProvider).Assembly;

        Assert.Null(assembly.GetType("HexCrawl.Application.Rules.IRulesCoreTravelGateway"));
        Assert.Null(assembly.GetType("HexCrawl.Application.Rules.RulesCoreTravelGatewayException"));
        Assert.Null(assembly.GetType("HexCrawl.Application.Rules.ProcedureResolutionRulesCoreAdapter"));
        Assert.DoesNotContain(
            assembly.GetExportedTypes(),
            type => type.Name.Contains("RulesCore", StringComparison.Ordinal));
    }

    [Fact]
    public void ProcedureResolutionHelperDependsOnProviderNeutralEnrichment()
    {
        var constructor = Assert.Single(typeof(ProcedureResolutionHelperService).GetConstructors());
        var parameters = constructor.GetParameters().Select(parameter => parameter.ParameterType).ToArray();

        Assert.Contains(typeof(ProcedureResolutionProviderEnricher), parameters);
        Assert.DoesNotContain(
            parameters,
            type => type.Name.Contains("RulesCore", StringComparison.Ordinal));
    }

    [Fact]
    public void ProviderContractsContainNoToolHostOrRulesCoreTransportIdentity()
    {
        var contractTypes = new[]
        {
            typeof(ITravelEnvironmentProvider),
            typeof(TravelEnvironmentProviderMetadata),
            typeof(TravelEnvironmentProviderCatalogResult),
            typeof(TravelEnvironmentProviderResolutionResult),
            typeof(TravelEnvironmentResolutionRequest)
        };

        foreach (var type in contractTypes)
        {
            Assert.DoesNotContain("RulesCore", type.FullName ?? type.Name, StringComparison.Ordinal);
            Assert.DoesNotContain("ToolHost", type.FullName ?? type.Name, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void DeterministicRuntimeDoesNotReferenceApplicationProviderLayer()
    {
        var applicationAssemblyName = typeof(ITravelEnvironmentProvider).Assembly.GetName().Name;
        var runtimeAssembly = typeof(GenericProcedureRuntime).Assembly;

        Assert.DoesNotContain(
            runtimeAssembly.GetReferencedAssemblies(),
            reference => string.Equals(reference.Name, applicationAssemblyName, StringComparison.Ordinal));
    }

    [Fact]
    public void OptionalProviderDependencySourceRemainsGenericAndProcedureHasNoProviderIdentity()
    {
        Assert.Equal(4, (int)ProcedureInputSource.OptionalProvider);
        Assert.DoesNotContain(
            typeof(CampaignProcedure).GetProperties(),
            property => property.Name.Contains("Provider", StringComparison.OrdinalIgnoreCase));
    }
}
