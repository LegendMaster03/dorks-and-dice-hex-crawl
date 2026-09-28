using HexCrawl.Domain.Procedure;

namespace HexCrawl.Application.Persistence;

public static class ExpeditionProcedureConsistency
{
    public static void ValidateForPersistence(StoredExpedition expedition)
    {
        ArgumentNullException.ThrowIfNull(expedition);
        expedition.Procedure.Validate();

        if (expedition.CampaignProcedure is null)
        {
            return;
        }

        expedition.CampaignProcedure.Validate();
        if (!CampaignProcedureCompatibilityProjector.TryProject(expedition.CampaignProcedure, out var projected))
        {
            // Forward-compatible generic snapshots remain authoritative data even when this
            // runtime version can not project them into the retained CrawlProcedureProfile compatibility representation.
            return;
        }

        if (projected != expedition.Procedure)
        {
            throw new InvalidOperationException(
                "The expedition's generic campaign procedure does not match its retained CrawlProcedureProfile compatibility representation.");
        }
    }

    public static void ValidateLoaded(StoredExpedition expedition)
    {
        try
        {
            ValidateForPersistence(expedition);
        }
        catch (InvalidOperationException exception)
        {
            throw new InvalidDataException(
                "Persisted expedition procedure state is invalid or contradictory.",
                exception);
        }
    }
}
