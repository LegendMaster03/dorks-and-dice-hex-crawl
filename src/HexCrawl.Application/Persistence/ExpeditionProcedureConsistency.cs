using HexCrawl.Domain.Procedure;

namespace HexCrawl.Application.Persistence;

public static class ExpeditionProcedureConsistency
{
    public static void ValidateForPersistence(StoredExpedition expedition)
    {
        ArgumentNullException.ThrowIfNull(expedition);
        expedition.CompatibilityProfile?.Validate();

        if (expedition.CampaignProcedure is null)
        {
            if (expedition.CompatibilityProfile is null)
            {
                throw new InvalidOperationException(
                    "An expedition requires either a generic CampaignProcedure snapshot or a historical CrawlProcedureProfile compatibility snapshot.");
            }
            return;
        }

        expedition.CampaignProcedure.Validate();
        if (!CampaignProcedureCompatibilityProjector.TryProject(expedition.CampaignProcedure, out var projected))
        {
            // A non-projectable generic snapshot is authoritative. A retained compatibility profile may
            // still exist on historical/forward-compatible data, but new materialization does not invent one.
            return;
        }

        if (expedition.CompatibilityProfile is null)
        {
            throw new InvalidOperationException(
                "A compatibility-projectable campaign procedure must retain its CrawlProcedureProfile compatibility representation.");
        }

        if (projected != expedition.CompatibilityProfile)
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
