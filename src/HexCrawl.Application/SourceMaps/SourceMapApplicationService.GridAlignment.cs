using HexCrawl.Application.Persistence;
using HexCrawl.Domain.Spatial;
using HexCrawl.Domain.World;

namespace HexCrawl.Application;

public sealed partial class SourceMapApplicationService
{
    /// <summary>
    /// Save an independently rechecked detected alignment in a single
    /// optimistic-versioned world aggregate write. Geometry and pinned
    /// procedure revisions remain untouched.
    /// </summary>
    public async Task<StoredOverworld> RegisterComputedAffineAsync(
        Guid overworldId, Guid sourceMapId, string ownerUserId,
        MapRegistrationTransform alignment, long expectedVersion,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(alignment);
        var current = await GetOverworldAsync(overworldId, ownerUserId, cancellationToken);
        RequireVersion(expectedVersion, current.Version);
        if (current.World.HasLegacyHexGrid)
            throw new NotSupportedException(
                "Detected motif placement is not the legacy hex-grid alignment workflow.");
        var existing = Find(current, sourceMapId);
        if (existing.PixelWidth <= 0 || existing.PixelHeight <= 0)
            throw new InvalidOperationException("This map must have valid raster dimensions.");
        ValidateAffine(alignment);
        var updated = existing with
        {
            Alignment = alignment,
            WorldCoverageBoundary = AffineRegistrationSolver.Coverage(
                alignment, existing.PixelWidth, existing.PixelHeight)
        };
        return await ReplaceAsync(current, sourceMapId, updated, expectedVersion, cancellationToken);
    }

    /// <summary>
    /// Correct an unused, uncalibrated generalized world in one database
    /// transaction. Existing image assets, source-map metadata and world
    /// identity are preserved; the tiling gets a NEW stable cell identity.
    /// The persistence layer locks the world against concurrent starts.
    /// </summary>
    public async Task<StoredOverworld> ReplaceUnusedPeriodicWorldAsync(
        Guid overworldId, Guid sourceMapId, string ownerUserId,
        PeriodicWorldTiling replacementTiling, MapRegistrationTransform alignment,
        long expectedVersion, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(replacementTiling);
        ArgumentNullException.ThrowIfNull(alignment);
        var current = await GetOverworldAsync(overworldId, ownerUserId, cancellationToken);
        RequireVersion(expectedVersion, current.Version);
        var old = current.World;
        if (old.HasLegacyHexGrid || old.Tiling is null
            || old.Tiling.PhysicalDistancePerWorldUnit is not null
            || old.SourceMaps.Any(map => map.Alignment is not null)
            || old.Locations.Count != 0 || old.Features.Count != 0
            || old.EnvironmentAnnotations.Count != 0)
            throw new HexCrawlConflictException(
                "This world already has geometry-dependent data or physical calibration. Its tiles can not be replaced automatically.");
        if (replacementTiling.Id == old.Tiling.Id
            || replacementTiling.Revision != old.Tiling.Revision + 1
            || replacementTiling.Origin != old.Tiling.Origin
            || replacementTiling.PhysicalDistancePerWorldUnit is not null)
            throw new ArgumentException(
                "An unused-world correction must have a new tiling identity, next revision, and unchanged world origin.");

        replacementTiling.Validate();
        ValidateAffine(alignment);
        var source = Find(current, sourceMapId);
        if (source.PixelWidth <= 0 || source.PixelHeight <= 0)
            throw new ArgumentException("The image has no usable raster dimensions.");
        var registered = source with
        {
            Alignment = alignment,
            WorldCoverageBoundary = AffineRegistrationSolver.Coverage(
                alignment, source.PixelWidth, source.PixelHeight)
        };
        var updatedWorld = old with
        {
            Tiling = replacementTiling,
            SourceMaps = old.SourceMaps.Select(map =>
                map.Id == sourceMapId ? registered : map).ToArray()
        };
        updatedWorld.ValidateEnvironmentAnnotations();
        var result = await store.ReplaceUnusedPeriodicWorldAsync(
            current with { World = updatedWorld }, expectedVersion, cancellationToken);
        return result.Outcome switch
        {
            SaveOutcome.Saved => result.Value!,
            SaveOutcome.NotFound => throw new HexCrawlNotFoundException(
                "The world was removed before its geometry could be updated."),
            SaveOutcome.Conflict => throw new HexCrawlConflictException(
                "The world changed or an expedition started before its cells could be replaced. No changes were saved. Reload and check the map again."),
            _ => throw new InvalidOperationException("Unexpected correction persistence result.")
        };
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
