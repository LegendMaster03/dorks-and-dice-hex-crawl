using System.Buffers.Binary;
using System.Text;
using HexCrawl.Infrastructure.Wonderdraft;

namespace HexCrawl.Application.Tests;

public sealed class WonderdraftProjectInspectorTests
{
    [Fact]
    public async Task InspectorReadsStructuredMetadataAndSkipsEmbeddedRasterBytes()
    {
        var bytes = BuildProject();

        var summary = await WonderdraftProjectInspector.InspectAsync(
            new MemoryStream(bytes, writable: false));

        Assert.Equal(15, summary.FormatVersion);
        Assert.Equal(1024, summary.PixelWidth);
        Assert.Equal(768, summary.PixelHeight);
        Assert.Equal(2, summary.SymbolCount);
        Assert.Equal(1, summary.LabelCount);
        Assert.Equal(3, summary.PathCount);
        Assert.Equal(2, summary.TerritoryCount);
        Assert.True(summary.HasGrid);
        Assert.Equal(["Custom Pack", "Shared Pack"], summary.IncludedPacks);
        Assert.Equal(["Default Pack"], summary.IncludedDefaultPacks);
    }

    [Fact]
    public async Task ReaderExtractsPointAndShapeCandidatesAcrossWonderdraftEncodings()
    {
        var document = await WonderdraftProjectInspector.ReadAsync(
            new MemoryStream(BuildCandidateProject(), writable: false));

        var label = Assert.Single(document.Candidates, item => item.Kind == WonderdraftCandidateKind.Label);
        Assert.Equal("Old Harbor", label.DisplayName);
        Assert.Equal(new WonderdraftPixelPoint(512, 384), label.Position);
        Assert.Null(label.Problem);

        var symbol = Assert.Single(document.Candidates, item => item.Kind == WonderdraftCandidateKind.Symbol);
        Assert.Equal("castle", symbol.DisplayName);
        Assert.Equal(new WonderdraftPixelPoint(256, 192), symbol.Position);
        Assert.Contains("sprites/symbols/towns/castle", symbol.Descriptor, StringComparison.Ordinal);

        var pathCandidate = Assert.Single(document.Candidates, item => item.Kind == WonderdraftCandidateKind.Path);
        Assert.Equal(
            [new WonderdraftPixelPoint(11, 22), new WonderdraftPixelPoint(13, 24)],
            pathCandidate.Points);
        Assert.Null(pathCandidate.Problem);

        var territory = Assert.Single(document.Candidates, item => item.Kind == WonderdraftCandidateKind.Territory);
        Assert.Equal(3, territory.Points.Count);
        Assert.Equal(new WonderdraftPixelPoint(20, 30), territory.Points[0]);
        Assert.Null(territory.Problem);
    }

    [Fact]
    public async Task ReaderHandlesHumblewoodShapedPopulationAndPreservesSourceMetadata()
    {
        var document = await WonderdraftProjectInspector.ReadAsync(
            new MemoryStream(BuildHumblewoodShapedProject(), writable: false));

        Assert.Equal(86, document.Summary.LabelCount);
        Assert.Equal(2_510, document.Summary.SymbolCount);
        Assert.Equal(19, document.Summary.PathCount);
        Assert.Equal(1, document.Summary.TerritoryCount);
        Assert.Equal(2_616, document.Candidates.Count);

        var firstLabel = Assert.Single(
            document.Candidates,
            item => item.Key == "label:0");
        Assert.Equal("Alderheart", firstLabel.DisplayName);
        Assert.Equal("32", firstLabel.Properties!["font_size"]);
        Assert.Equal("settlement", firstLabel.Properties["style.kind"]);

        var tree = Assert.Single(
            document.Candidates,
            item => item.Key == "symbol:0");
        Assert.Contains("trees", tree.Descriptor, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("0.75", tree.Properties!["scale"]);

        var pathCandidate = Assert.Single(
            document.Candidates,
            item => item.Key == "path:18");
        Assert.Equal(2, pathCandidate.Points.Count);

        var territory = Assert.Single(
            document.Candidates,
            item => item.Kind == WonderdraftCandidateKind.Territory);
        Assert.Contains(territory.Points, point => point.X < 0 || point.Y < 0);
        Assert.Contains(territory.Points, point =>
            point.X > document.Summary.PixelWidth || point.Y > document.Summary.PixelHeight);

        Assert.Equal("hex", document.Summary.GridMetadata!["grid.type"]);
        Assert.Equal("Miles", document.Summary.ScaleMetadata!["scale.unit_label"]);
        Assert.NotNull(document.Summary.PhysicalScale);
        Assert.Equal(10, document.Summary.PhysicalScale!.DistancePerSegment);
        Assert.Equal(3, document.Summary.PhysicalScale.SegmentCount);
        Assert.Equal(220, document.Summary.PhysicalScale.PixelLength);
        Assert.Equal(30d / 220d, document.Summary.PhysicalScale.UnitsPerPixel, 12);
    }

    [Fact]
    public async Task InspectorRejectsNonGcpfInput()
    {
        var bytes = BuildProject();
        bytes[0] = (byte)'X';

        var exception = await Assert.ThrowsAsync<InvalidDataException>(() =>
            WonderdraftProjectInspector.InspectAsync(new MemoryStream(bytes, writable: false)));

        Assert.Contains("GCPF", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task InspectorRejectsDecodedPayloadAboveConfiguredLimitBeforeDecompression()
    {
        var bytes = BuildProject();

        var exception = await Assert.ThrowsAsync<InvalidDataException>(() =>
            WonderdraftProjectInspector.InspectAsync(
                new MemoryStream(bytes, writable: false),
                maxDecodedBytes: 128));

        Assert.Contains("decoded-data safety limit", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task InspectorRejectsMissingGcpfTrailer()
    {
        var bytes = BuildProject();
        bytes[^1] ^= 0xff;

        var exception = await Assert.ThrowsAsync<InvalidDataException>(() =>
            WonderdraftProjectInspector.InspectAsync(new MemoryStream(bytes, writable: false)));

        Assert.Contains("trailer", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task InspectorRejectsVariantLengthThatDoesNotMatchContainer()
    {
        var bytes = BuildProject();
        var blockCount = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(12, 4))
            / BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(8, 4))
            + 1;
        var firstPackedOffset = checked(16 + ((int)blockCount * 4));
        bytes[firstPackedOffset + 1] ^= 0x01;

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            WonderdraftProjectInspector.InspectAsync(new MemoryStream(bytes, writable: false)));
    }

    private static byte[] BuildHumblewoodShapedProject()
    {
        using var body = new MemoryStream();
        WriteHeader(body, 18);
        WriteUInt32(body, 11);
        WriteEntry(body, "version", () => WriteInteger(body, 15));
        WriteEntry(body, "map_width", () => WriteInteger(body, 2048));
        WriteEntry(body, "map_height", () => WriteInteger(body, 1536));
        WriteEntry(body, "labels", () => WriteArray(body, 86, index =>
        {
            if (index == 0)
            {
                WriteDictionary(body,
                    ("text", () => WriteString(body, "Alderheart")),
                    ("position", () => WriteVector2(body, 1024, 768)),
                    ("font_size", () => WriteInteger(body, 32)),
                    ("style", () => WriteDictionary(body,
                        ("kind", () => WriteString(body, "settlement")))));
                return;
            }

            WriteDictionary(body,
                ("text", () => WriteString(body, $"Label {index + 1}")),
                ("position", () => WriteVector2(body, 20 + index, 30 + index)));
        }));
        WriteEntry(body, "symbols", () => WriteArray(body, 2_510, index =>
        {
            var texture = index < 2_097
                ? "res://sprites/symbols/trees/oak"
                : index < 2_372
                    ? "res://sprites/symbols/mountains/peak"
                    : "res://sprites/symbols/other/decorative";
            WriteDictionary(body,
                ("texture", () => WriteString(body, texture)),
                ("position", () => WriteVector2(body, index % 2048, (index * 3) % 1536)),
                ("scale", () => WriteReal(body, 0.75f)));
        }));
        WriteEntry(body, "paths", () => WriteArray(body, 19, index =>
            WriteDictionary(body,
                ("style", () => WriteString(body, "custom/path")),
                ("points", () => WriteString(
                    body,
                    $"[ Vector2( {100 + index}, {200 + index} ), Vector2( {300 + index}, {400 + index} ) ]")))));
        WriteEntry(body, "territories", () =>
        {
            WriteDictionary(body,
                ("territories", () =>
                    WriteArray(body, 1, () =>
                        WriteDictionary(body,
                            ("points", () =>
                                WritePoolVector2Array(
                                    body,
                                    (-100, -50),
                                    (2200, -50),
                                    (2200, 1700),
                                    (-100, 1700)))))));
        });
        WriteEntry(body, "grid", () =>
            WriteDictionary(body,
                ("type", () => WriteString(body, "hex")),
                ("visible", () => WriteInteger(body, 1))));
        WriteEntry(body, "scale", () =>
            WriteDictionary(body,
                ("unit_label", () => WriteString(body, "Miles")),
                ("segment_distance", () => WriteInteger(body, 10)),
                ("segment_count", () => WriteInteger(body, 3)),
                ("pixel_length", () => WriteInteger(body, 220))));
        WriteEntry(body, "included_packs", () => WriteStringArray(body, "Humblewood"));
        WriteEntry(body, "included_default_packs", () => WriteStringArray(body, "Default"));

        var variant = body.ToArray();
        using var raw = new MemoryStream();
        WriteUInt32(raw, checked((uint)variant.Length));
        raw.Write(variant);
        return WrapGcpf(raw.ToArray(), blockSize: 4096);
    }

    private static byte[] BuildCandidateProject()
    {
        using var body = new MemoryStream();
        WriteHeader(body, 18);
        WriteUInt32(body, 7);
        WriteEntry(body, "version", () => WriteInteger(body, 15));
        WriteEntry(body, "map_width", () => WriteInteger(body, 1024));
        WriteEntry(body, "map_height", () => WriteInteger(body, 768));
        WriteEntry(body, "labels", () => WriteArray(body, 1, () =>
            WriteDictionary(body,
                ("text", () => WriteString(body, "Old Harbor")),
                ("position", () => WriteVector2(body, 512, 384)))));
        WriteEntry(body, "symbols", () => WriteArray(body, 1, () =>
            WriteDictionary(body,
                ("texture", () => WriteString(body, "res://sprites/symbols/towns/castle")),
                ("position", () => WriteVector2(body, 256, 192)))));
        WriteEntry(body, "paths", () => WriteArray(body, 1, () =>
            WriteDictionary(body,
                ("points", () => WriteString(body, "[ Vector2( 1, 2 ), Vector2( 3, 4 ) ]")),
                ("position", () => WriteVector2(body, 10, 20)))));
        WriteEntry(body, "territories", () =>
            WriteDictionary(body,
                ("territories", () => WriteArray(body, 1, () =>
                    WriteDictionary(body,
                        ("points", () => WritePoolVector2Array(body, (20, 30), (40, 30), (40, 50))))))));

        var variant = body.ToArray();
        using var raw = new MemoryStream();
        WriteUInt32(raw, checked((uint)variant.Length));
        raw.Write(variant);
        return WrapGcpf(raw.ToArray(), blockSize: 64);
    }

    private static byte[] BuildProject()
    {
        using var body = new MemoryStream();
        WriteHeader(body, 18);
        WriteUInt32(body, 11);

        WriteEntry(body, "version", () => WriteInteger(body, 15));
        WriteEntry(body, "map_width", () => WriteReal(body, 1024));
        WriteEntry(body, "map_height", () => WriteInteger(body, 768));
        WriteEntry(body, "symbols", () => WriteArray(body, 2, () => WriteNil(body)));
        WriteEntry(body, "labels", () => WriteArray(body, 1, () => WriteNil(body)));
        WriteEntry(body, "paths", () => WriteArray(body, 3, () => WriteNil(body)));
        WriteEntry(body, "territories", () =>
        {
            WriteHeader(body, 18);
            WriteUInt32(body, 1);
            WriteEntry(body, "territories", () => WriteArray(body, 2, () => WriteNil(body)));
        });
        WriteEntry(body, "included_packs", () =>
            WriteStringArray(body, "Custom Pack", "Shared Pack", "Shared Pack"));
        WriteEntry(body, "included_default_packs", () =>
            WriteStringArray(body, "Default Pack"));
        WriteEntry(body, "grid", () =>
        {
            WriteHeader(body, 18);
            WriteUInt32(body, 0);
        });
        WriteEntry(body, "mask", () =>
        {
            WriteHeader(body, 17);
            WriteRawString(body, "Image");
            WriteUInt32(body, 1);
            WriteRawString(body, "data");
            WriteHeader(body, 18);
            WriteUInt32(body, 1);
            WriteString(body, "data");
            WriteHeader(body, 20);
            var raster = Enumerable.Repeat((byte)0x7f, 4096).ToArray();
            WriteUInt32(body, checked((uint)raster.Length));
            body.Write(raster);
        });

        var bodyBytes = body.ToArray();
        using var raw = new MemoryStream();
        WriteUInt32(raw, checked((uint)bodyBytes.Length));
        raw.Write(bodyBytes);

        return WrapGcpf(raw.ToArray(), blockSize: 64);
    }

    private static byte[] WrapGcpf(byte[] raw, uint blockSize)
    {
        var blockCount = checked((int)(((uint)raw.Length / blockSize) + 1));
        var blocks = new List<byte[]>(blockCount);
        for (var index = 0; index < blockCount; index++)
        {
            var start = checked((int)(index * blockSize));
            var remaining = Math.Max(0, raw.Length - start);
            var count = Math.Min(checked((int)blockSize), remaining);
            blocks.Add(FastLzLiteral(raw.AsSpan(start, count)));
        }

        using var output = new MemoryStream();
        output.Write("GCPF"u8);
        WriteUInt32(output, 0);
        WriteUInt32(output, blockSize);
        WriteUInt32(output, checked((uint)raw.Length));
        foreach (var block in blocks) WriteUInt32(output, checked((uint)block.Length));
        foreach (var block in blocks) output.Write(block);
        output.Write("GCPF"u8);
        return output.ToArray();
    }

    private static byte[] FastLzLiteral(ReadOnlySpan<byte> data)
    {
        if (data.IsEmpty) return [];
        using var output = new MemoryStream();
        for (var offset = 0; offset < data.Length; offset += 32)
        {
            var count = Math.Min(32, data.Length - offset);
            output.WriteByte((byte)(count - 1));
            output.Write(data.Slice(offset, count));
        }
        return output.ToArray();
    }

    private static void WriteEntry(Stream stream, string key, Action valueWriter)
    {
        WriteString(stream, key);
        valueWriter();
    }

    private static void WriteDictionary(
        Stream stream,
        params (string Key, Action WriteValue)[] entries)
    {
        WriteHeader(stream, 18);
        WriteUInt32(stream, checked((uint)entries.Length));
        foreach (var (key, writeValue) in entries)
        {
            WriteEntry(stream, key, writeValue);
        }
    }

    private static void WriteVector2(Stream stream, float x, float y)
    {
        WriteHeader(stream, 5);
        WriteSingle(stream, x);
        WriteSingle(stream, y);
    }

    private static void WritePoolVector2Array(Stream stream, params (float X, float Y)[] points)
    {
        WriteHeader(stream, 24);
        WriteUInt32(stream, checked((uint)points.Length));
        foreach (var point in points)
        {
            WriteSingle(stream, point.X);
            WriteSingle(stream, point.Y);
        }
    }

    private static void WriteSingle(Stream stream, float value)
    {
        Span<byte> bytes = stackalloc byte[4];
        BinaryPrimitives.WriteSingleLittleEndian(bytes, value);
        stream.Write(bytes);
    }

    private static void WriteStringArray(Stream stream, params string[] values)
    {
        WriteHeader(stream, 19);
        WriteUInt32(stream, checked((uint)values.Length));
        foreach (var value in values) WriteString(stream, value);
    }

    private static void WriteArray(Stream stream, int count, Action writeValue)
    {
        WriteHeader(stream, 19);
        WriteUInt32(stream, checked((uint)count));
        for (var index = 0; index < count; index++) writeValue();
    }

    private static void WriteArray(Stream stream, int count, Action<int> writeValue)
    {
        WriteHeader(stream, 19);
        WriteUInt32(stream, checked((uint)count));
        for (var index = 0; index < count; index++) writeValue(index);
    }

    private static void WriteNil(Stream stream) => WriteHeader(stream, 0);

    private static void WriteInteger(Stream stream, int value)
    {
        WriteHeader(stream, 2);
        Span<byte> bytes = stackalloc byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(bytes, value);
        stream.Write(bytes);
    }

    private static void WriteReal(Stream stream, float value)
    {
        WriteHeader(stream, 3);
        Span<byte> bytes = stackalloc byte[4];
        BinaryPrimitives.WriteSingleLittleEndian(bytes, value);
        stream.Write(bytes);
    }

    private static void WriteString(Stream stream, string value)
    {
        WriteHeader(stream, 4);
        WriteRawString(stream, value);
    }

    private static void WriteRawString(Stream stream, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        WriteUInt32(stream, checked((uint)bytes.Length));
        stream.Write(bytes);
        var padding = (4 - (bytes.Length % 4)) % 4;
        if (padding > 0) stream.Write(new byte[padding]);
    }

    private static void WriteHeader(Stream stream, uint type) => WriteUInt32(stream, type);

    private static void WriteUInt32(Stream stream, uint value)
    {
        Span<byte> bytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, value);
        stream.Write(bytes);
    }
}
