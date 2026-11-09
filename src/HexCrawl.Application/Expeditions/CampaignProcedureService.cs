using HexCrawl.Application.Persistence;
using HexCrawl.Domain.Procedure;

namespace HexCrawl.Application;

/// <summary>
/// Persists owner-scoped procedure revisions as immutable materialized rules.
/// A new revision does not rewrite procedures already pinned to expeditions.
/// </summary>
public sealed class CampaignProcedureService(IHexCrawlStore store)
{
    // A preset is a recipe for a stored revision, not a mutable dependency
    // to resolve again during later expedition execution.
    public async Task<StoredCampaignProcedureRevision> CreateFromPresetAsync(
        string ownerUserId,
        string presetKey,
        Guid? campaignId = null,
        CancellationToken cancellationToken = default)
    {
        var materialized = CrawlProcedureCatalog.Resolve(presetKey).MaterializeGeneric();
        return await CreateAsync(
            ownerUserId,
            materialized.Procedure,
            materialized.Origin,
            campaignId,
            cancellationToken);
    }

    public async Task<StoredCampaignProcedureRevision> CreateAsync(
        string ownerUserId,
        CampaignProcedure procedure,
        ProcedureOriginMetadata? origin,
        Guid? campaignId = null,
        CancellationToken cancellationToken = default)
    {
        var owner = RequireOwner(ownerUserId);
        ArgumentNullException.ThrowIfNull(procedure);
        if (procedure.Revision != 1)
        {
            throw new InvalidOperationException("A newly persisted campaign procedure must begin at revision 1.");
        }
        procedure.Validate();

        return await store.CreateCampaignProcedureRevisionAsync(
            new StoredCampaignProcedureRevision(
                CampaignProcedureSnapshot.Copy(procedure),
                owner,
                campaignId,
                origin,
                DateTimeOffset.UtcNow),
            cancellationToken);
    }

    public async Task<StoredCampaignProcedureRevision> CreateRevisionAsync(
        string ownerUserId,
        Guid procedureId,
        int expectedRevision,
        IReadOnlyList<CampaignProcedureOverride> overrides,
        CancellationToken cancellationToken = default)
    {
        var owner = RequireOwner(ownerUserId);
        if (procedureId == Guid.Empty)
        {
            throw new ArgumentException("Procedure id can not be empty.", nameof(procedureId));
        }
        if (expectedRevision <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(expectedRevision));
        }
        ArgumentNullException.ThrowIfNull(overrides);

        var current = await store.GetLatestCampaignProcedureRevisionAsync(procedureId, owner, cancellationToken)
            ?? throw new HexCrawlNotFoundException("Campaign procedure was not found.");
        if (current.Revision != expectedRevision)
        {
            throw new HexCrawlConcurrencyException(
                $"Campaign procedure revision {expectedRevision} is stale; the current revision is {current.Revision}.");
        }
        var next = CampaignProcedureMaterializer.CreateRevision(current.Procedure, overrides);
        return await store.CreateCampaignProcedureRevisionAsync(
            new StoredCampaignProcedureRevision(
                next,
                owner,
                current.CampaignId,
                current.ProcedureOrigin,
                DateTimeOffset.UtcNow),
            cancellationToken);
    }

    // Canonical JSON revisions follow the same version and ownership
    // controls as revisions produced through structured authoring.
    public async Task<StoredCampaignProcedureRevision> CreateCanonicalRevisionAsync(
        string ownerUserId,
        Guid procedureId,
        int expectedRevision,
        CampaignProcedure editedProcedure,
        CancellationToken cancellationToken = default)
    {
        var owner = RequireOwner(ownerUserId);
        if (procedureId == Guid.Empty)
        {
            throw new ArgumentException("Procedure id can not be empty.", nameof(procedureId));
        }
        if (expectedRevision <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(expectedRevision));
        }
        ArgumentNullException.ThrowIfNull(editedProcedure);

        var current = await store.GetLatestCampaignProcedureRevisionAsync(procedureId, owner, cancellationToken)
            ?? throw new HexCrawlNotFoundException("Campaign procedure was not found.");
        if (current.Revision != expectedRevision)
        {
            throw new HexCrawlConcurrencyException(
                $"Campaign procedure revision {expectedRevision} is stale; the current revision is {current.Revision}.");
        }
        if (editedProcedure.ProcedureId != procedureId)
        {
            throw new InvalidOperationException("Canonical JSON can not change the campaign procedure id.");
        }
        if (editedProcedure.Revision != expectedRevision)
        {
            throw new HexCrawlConcurrencyException(
                $"Canonical JSON revision {editedProcedure.Revision} does not match expected revision {expectedRevision}.");
        }

        editedProcedure.Validate();
        if (CampaignProcedureSnapshot.Equivalent(current.Procedure, editedProcedure))
        {
            throw new InvalidOperationException("The submitted canonical procedure does not change the current revision.");
        }

        var next = CampaignProcedureSnapshot.Copy(editedProcedure) with
        {
            Revision = checked(expectedRevision + 1)
        };
        next.Validate();

        return await store.CreateCampaignProcedureRevisionAsync(
            new StoredCampaignProcedureRevision(
                next,
                owner,
                current.CampaignId,
                current.ProcedureOrigin,
                DateTimeOffset.UtcNow),
            cancellationToken);
    }

    public async Task<StoredCampaignProcedureRevision> GetAsync(
        string ownerUserId,
        Guid procedureId,
        int revision,
        CancellationToken cancellationToken = default)
    {
        var owner = RequireOwner(ownerUserId);
        if (procedureId == Guid.Empty)
        {
            throw new ArgumentException("Procedure id can not be empty.", nameof(procedureId));
        }
        if (revision <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(revision));
        }

        return await store.GetCampaignProcedureRevisionAsync(procedureId, revision, owner, cancellationToken)
            ?? throw new HexCrawlNotFoundException("Campaign procedure revision was not found.");
    }

    public async Task<StoredCampaignProcedureRevision> GetLatestAsync(
        string ownerUserId,
        Guid procedureId,
        CancellationToken cancellationToken = default)
    {
        var owner = RequireOwner(ownerUserId);
        if (procedureId == Guid.Empty)
        {
            throw new ArgumentException("Procedure id can not be empty.", nameof(procedureId));
        }

        return await store.GetLatestCampaignProcedureRevisionAsync(procedureId, owner, cancellationToken)
            ?? throw new HexCrawlNotFoundException("Campaign procedure was not found.");
    }

    public async Task<IReadOnlyList<StoredCampaignProcedureRevision>> ListRevisionsAsync(
        string ownerUserId,
        Guid procedureId,
        CancellationToken cancellationToken = default)
    {
        var owner = RequireOwner(ownerUserId);
        if (procedureId == Guid.Empty)
        {
            throw new ArgumentException("Procedure id can not be empty.", nameof(procedureId));
        }

        return await store.ListCampaignProcedureRevisionsAsync(procedureId, owner, cancellationToken);
    }

    public async Task<IReadOnlyList<StoredCampaignProcedureRevision>> ListLatestAsync(
        string ownerUserId,
        CancellationToken cancellationToken = default)
    {
        var owner = RequireOwner(ownerUserId);
        return await store.ListLatestCampaignProcedureRevisionsAsync(owner, cancellationToken);
    }

    private static string RequireOwner(string? ownerUserId) =>
        !string.IsNullOrWhiteSpace(ownerUserId)
            ? ownerUserId.Trim()
            : throw new ArgumentException("Owner user id is required.", nameof(ownerUserId));
}
