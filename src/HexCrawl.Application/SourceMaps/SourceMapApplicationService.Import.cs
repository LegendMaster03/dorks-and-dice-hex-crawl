using HexCrawl.Application.Persistence;
using HexCrawl.Domain.World;

namespace HexCrawl.Application;

public sealed record ImportSourceMapContentCommand(
    IReadOnlyList<SourceMapContentElement> Content,
    SourceMapImportProvenance Provenance,
    SourceMapSourceArchive SourceArchive,
    MapRegistrationTransform? Alignment,
    long ExpectedVersion);

public sealed partial class SourceMapApplicationService
{
    public async Task<StoredOverworld> ImportContentAsync(
        Guid overworldId,
        Guid sourceMapId,
        string ownerUserId,
        ImportSourceMapContentCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var current = await GetOverworldAsync(overworldId, ownerUserId, cancellationToken);
        RequireVersion(command.ExpectedVersion, current.Version);
        var existing = Find(current, sourceMapId);
        var alignment = command.Alignment ?? existing.Alignment;
        var replacement = existing with
        {
            ImportedContent = command.Content.ToArray(),
            ImportProvenance = command.Provenance,
            SourceArchive = command.SourceArchive,
            Alignment = alignment,
            WorldCoverageBoundary = alignment is null
                ? existing.WorldCoverageBoundary
                : AffineRegistrationSolver.Coverage(alignment, existing.PixelWidth, existing.PixelHeight)
        };

        return await ReplaceAsync(
            current,
            sourceMapId,
            replacement,
            command.ExpectedVersion,
            cancellationToken);
    }
}
