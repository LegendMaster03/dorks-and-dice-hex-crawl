using HexCrawl.Application.Persistence;
using HexCrawl.Domain.World;

namespace HexCrawl.Application;

public sealed record ReplaceWorldEnvironmentCommand(
    long ExpectedVersion,
    IReadOnlyList<EnvironmentAnnotation> Annotations);

public sealed record UpdateExpeditionEnvironmentCommand(
    long ExpectedVersion,
    IReadOnlyList<EnvironmentFact> CurrentFacts,
    IReadOnlyList<EnvironmentFact> Overrides);

public sealed partial class HexCrawlService
{
    public async Task<StoredOverworld> ReplaceWorldEnvironmentAsync(
        Guid overworldId,
        string ownerUserId,
        ReplaceWorldEnvironmentCommand command,
        CancellationToken cancellationToken = default)
    {
        var current = await GetOverworldAsync(overworldId, ownerUserId, cancellationToken);
        RequireVersion(command.ExpectedVersion, current.Version);
        var updatedDefinition = current.World with { EnvironmentAnnotations = command.Annotations.ToArray() };
        updatedDefinition.ValidateEnvironmentAnnotations();
        return await SaveWorldAsync(
            current with { World = updatedDefinition },
            command.ExpectedVersion,
            cancellationToken);
    }

    public async Task<StoredExpedition> UpdateExpeditionEnvironmentAsync(
        Guid expeditionId,
        string ownerUserId,
        UpdateExpeditionEnvironmentCommand command,
        CancellationToken cancellationToken = default)
    {
        var current = await GetExpeditionAsync(expeditionId, ownerUserId, cancellationToken);
        RequireVersion(command.ExpectedVersion, current.Version);
        var environment = new ExpeditionEnvironmentState
        {
            CurrentFacts = command.CurrentFacts.ToArray(),
            Overrides = command.Overrides.ToArray()
        };
        environment.Validate();
        return await SaveExpeditionAsync(
            current with { Environment = environment },
            command.ExpectedVersion,
            cancellationToken);
    }
}
