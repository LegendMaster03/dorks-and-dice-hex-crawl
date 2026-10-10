using HexCrawl.Application.Persistence;
using HexCrawl.Domain.Spatial;
using HexCrawl.Domain.World;

namespace HexCrawl.Application;

public sealed record ApplyRasterGridAlignmentCommand(
    HexGridDefinition Grid,
    MapRegistrationTransform Alignment,
    long ExpectedVersion);

public sealed partial class SourceMapApplicationService
{
    public async Task<StoredOverworld> ApplyRasterGridAlignmentAsync(
        Guid overworldId,
        Guid sourceMapId,
        string ownerUserId,
        ApplyRasterGridAlignmentCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(command.Grid);
        ArgumentNullException.ThrowIfNull(command.Alignment);

        var current = await GetOverworldAsync(overworldId, ownerUserId, cancellationToken);
        RequireVersion(command.ExpectedVersion, current.Version);
        var existing = Find(current, sourceMapId);
        if (existing.PixelWidth <= 0 || existing.PixelHeight <= 0)
        {
            throw new InvalidOperationException(
                "This source map has no raster dimensions and can not receive detected grid alignment.");
        }

        command.Grid.Validate();
        if (command.Grid.Id != current.World.Grid.Id)
        {
            throw new InvalidOperationException("Grid identity can not be replaced during raster-grid alignment.");
        }

        ValidateAffine(command.Alignment);
        if (command.Grid != current.World.Grid
            && await store.HasExpeditionsAsync(overworldId, current.OwnerUserId, cancellationToken))
        {
            throw new HexCrawlConflictException(
                "Grid geometry can not be changed after an expedition has been created for this overworld. Recreate the test world or keep the existing grid geometry.");
        }

        var replacement = existing with
        {
            Alignment = command.Alignment,
            WorldCoverageBoundary = AffineRegistrationSolver.Coverage(
                command.Alignment,
                existing.PixelWidth,
                existing.PixelHeight)
        };

        var updated = current with
        {
            World = current.World with
            {
                Grid = command.Grid,
                Tiling = LegacyHexTilingCompatibility.Create(command.Grid) with
                {
                    Revision = current.World.SpatialTiling.Revision +
                        (command.Grid == current.World.Grid ? 0 : 1)
                },
                SourceMaps = current.World.SourceMaps
                    .Select(item => item.Id == sourceMapId ? replacement : item)
                    .ToArray()
            }
        };

        return await SaveAsync(updated, command.ExpectedVersion, cancellationToken);
    }

    private static void ValidateAffine(MapRegistrationTransform transform)
    {
        if (transform.Kind != MapRegistrationTransformKind.Affine
            || Math.Abs(transform.M31) > 1e-12
            || Math.Abs(transform.M32) > 1e-12)
        {
            throw new ArgumentException(
                "Detected raster-grid alignment must use an affine transform.",
                nameof(transform));
        }

        var values = new[]
        {
            transform.M11, transform.M12, transform.M13,
            transform.M21, transform.M22, transform.M23
        };
        if (values.Any(value => !double.IsFinite(value)))
        {
            throw new ArgumentException(
                "Detected raster-grid alignment contains a non-finite transform value.",
                nameof(transform));
        }

        var determinant = (transform.M11 * transform.M22) - (transform.M12 * transform.M21);
        if (!double.IsFinite(determinant) || Math.Abs(determinant) <= 1e-10)
        {
            throw new ArgumentException(
                "Detected raster-grid alignment is degenerate.",
                nameof(transform));
        }
    }
}
