using HexCrawl.Application;
using HexCrawl.Application.Persistence;
using HexCrawl.Domain.Procedure;
using NpgsqlTypes;

namespace HexCrawl.Infrastructure.Persistence;

public sealed partial class PostgresHexCrawlStore
{
    public async Task<StoredCampaignProcedureRevision> CreateCampaignProcedureRevisionAsync(
        StoredCampaignProcedureRevision revision,
        CancellationToken cancellationToken = default)
    {
        revision.Procedure.Validate();
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO campaign_procedure_revisions(
                procedure_id, revision, owner_user_id, campaign_id, procedure_json, origin_json, created_at)
            VALUES(@id, @revision, @owner, @campaign, @procedure, @origin, @created);
            """;
        BindCampaignProcedure(command, revision);
        try
        {
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (Npgsql.PostgresException exception) when (exception.SqlState == Npgsql.PostgresErrorCodes.UniqueViolation)
        {
            throw new HexCrawlConcurrencyException(
                $"Campaign procedure {revision.ProcedureId} revision {revision.Revision} already exists.");
        }
        return revision;
    }

    public async Task<StoredCampaignProcedureRevision?> GetCampaignProcedureRevisionAsync(
        Guid procedureId,
        int revision,
        string ownerUserId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT campaign_id, procedure_json::text, origin_json::text, created_at
            FROM campaign_procedure_revisions
            WHERE procedure_id = @id AND revision = @revision AND owner_user_id = @owner;
            """;
        command.Parameters.AddWithValue("id", NpgsqlDbType.Uuid, procedureId);
        command.Parameters.AddWithValue("revision", NpgsqlDbType.Integer, revision);
        command.Parameters.AddWithValue("owner", NpgsqlDbType.Text, ownerUserId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? ReadCampaignProcedure(reader, ownerUserId)
            : null;
    }

    public async Task<StoredCampaignProcedureRevision?> GetLatestCampaignProcedureRevisionAsync(
        Guid procedureId,
        string ownerUserId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT campaign_id, procedure_json::text, origin_json::text, created_at
            FROM campaign_procedure_revisions
            WHERE procedure_id = @id AND owner_user_id = @owner
            ORDER BY revision DESC
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("id", NpgsqlDbType.Uuid, procedureId);
        command.Parameters.AddWithValue("owner", NpgsqlDbType.Text, ownerUserId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? ReadCampaignProcedure(reader, ownerUserId)
            : null;
    }

    public async Task<IReadOnlyList<StoredCampaignProcedureRevision>> ListCampaignProcedureRevisionsAsync(
        Guid procedureId,
        string ownerUserId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT campaign_id, procedure_json::text, origin_json::text, created_at
            FROM campaign_procedure_revisions
            WHERE procedure_id = @id AND owner_user_id = @owner
            ORDER BY revision;
            """;
        command.Parameters.AddWithValue("id", NpgsqlDbType.Uuid, procedureId);
        command.Parameters.AddWithValue("owner", NpgsqlDbType.Text, ownerUserId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var result = new List<StoredCampaignProcedureRevision>();
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(ReadCampaignProcedure(reader, ownerUserId));
        }
        return result;
    }

    public async Task<IReadOnlyList<StoredCampaignProcedureRevision>> ListLatestCampaignProcedureRevisionsAsync(
        string ownerUserId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT campaign_id, procedure_json::text, origin_json::text, created_at
            FROM campaign_procedure_revisions revision
            WHERE owner_user_id = @owner
              AND revision.revision = (
                  SELECT MAX(candidate.revision)
                  FROM campaign_procedure_revisions candidate
                  WHERE candidate.procedure_id = revision.procedure_id
                    AND candidate.owner_user_id = revision.owner_user_id)
            ORDER BY created_at DESC, procedure_id;
            """;
        command.Parameters.AddWithValue("owner", NpgsqlDbType.Text, ownerUserId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var result = new List<StoredCampaignProcedureRevision>();
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(ReadCampaignProcedure(reader, ownerUserId));
        }
        return result;
    }

    private static StoredCampaignProcedureRevision ReadCampaignProcedure(Npgsql.NpgsqlDataReader reader, string ownerUserId)
    {
        Guid? campaignId = reader.IsDBNull(0) ? null : reader.GetGuid(0);
        var procedure = Deserialize<CampaignProcedure>(reader.GetString(1));
        procedure.Validate();
        var origin = reader.IsDBNull(2) ? null : Deserialize<ProcedureOriginMetadata>(reader.GetString(2));
        return new StoredCampaignProcedureRevision(
            procedure,
            ownerUserId,
            campaignId,
            origin,
            ReadTimestamp(reader, 3));
    }

    private static void BindCampaignProcedure(Npgsql.NpgsqlCommand command, StoredCampaignProcedureRevision revision)
    {
        command.Parameters.AddWithValue("id", NpgsqlDbType.Uuid, revision.ProcedureId);
        command.Parameters.AddWithValue("revision", NpgsqlDbType.Integer, revision.Revision);
        command.Parameters.AddWithValue("owner", NpgsqlDbType.Text, revision.OwnerUserId);
        command.Parameters.AddWithValue("campaign", NpgsqlDbType.Uuid, revision.CampaignId.HasValue ? revision.CampaignId.Value : DBNull.Value);
        AddJsonb(command, "procedure", Serialize(revision.Procedure));
        AddJsonb(command, "origin", revision.ProcedureOrigin is null ? null : Serialize(revision.ProcedureOrigin));
        AddTimestamp(command, "created", revision.CreatedAt);
    }
}
