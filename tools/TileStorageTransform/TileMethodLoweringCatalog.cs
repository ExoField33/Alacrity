public enum TileMethodLoweringKind
{
    InstanceMethodBecomesHandleMethod,
    StaticMethodRemainsStatic
}

public sealed record TileMethodLoweringDescriptor(string MethodName, TileMethodLoweringKind Kind);

/// <summary>
/// Complete method-name allow-list from the verified Terraria 1.4.5.6 Tile
/// call surface. Instance bodies are lowered in place after their receiver is
/// converted to a compact handle; static methods retain their original shape.
/// </summary>
public static class TileMethodLoweringCatalog
{
    private static readonly IReadOnlyDictionary<string, TileMethodLoweringDescriptor> Descriptors =
        new Dictionary<string, TileMethodLoweringDescriptor>(StringComparer.Ordinal)
        {
            ["actColor"] = Instance("actColor"), ["active"] = Instance("active"), ["actuator"] = Instance("actuator"),
            ["anyHoney"] = Instance("anyHoney"), ["anyLava"] = Instance("anyLava"), ["anyShimmer"] = Instance("anyShimmer"),
            ["anyWater"] = Instance("anyWater"), ["anyWire"] = Instance("anyWire"), ["BlockColorAndCoating"] = Instance("BlockColorAndCoating"),
            ["blockType"] = Instance("blockType"), ["bottomSlope"] = Instance("bottomSlope"), ["checkingLiquid"] = Instance("checkingLiquid"),
            ["Clear"] = Instance("Clear"), ["ClearBlockPaintAndCoating"] = Instance("ClearBlockPaintAndCoating"), ["ClearEverything"] = Instance("ClearEverything"),
            ["ClearMetadata"] = Instance("ClearMetadata"), ["ClearSlope"] = Instance("ClearSlope"), ["ClearTile"] = Instance("ClearTile"),
            ["ClearTileAndPaint"] = Instance("ClearTileAndPaint"), ["ClearWallPaintAndCoating"] = Instance("ClearWallPaintAndCoating"),
            ["color"] = Instance("color"), ["CopyFrom"] = Instance("CopyFrom"), ["CopyPaintAndCoating"] = Instance("CopyPaintAndCoating"),
            ["frameNumber"] = Instance("frameNumber"), ["fullbrightBlock"] = Instance("fullbrightBlock"), ["fullbrightWall"] = Instance("fullbrightWall"),
            ["halfBrick"] = Instance("halfBrick"), ["HasSameSlope"] = Instance("HasSameSlope"), ["honey"] = Instance("honey"),
            ["inActive"] = Instance("inActive"), ["invisibleBlock"] = Instance("invisibleBlock"), ["invisibleWall"] = Instance("invisibleWall"),
            ["isTheSameAs"] = Instance("isTheSameAs"), ["lava"] = Instance("lava"), ["leftSlope"] = Instance("leftSlope"),
            ["liquidType"] = Instance("liquidType"), ["nactive"] = Instance("nactive"), ["ResetToType"] = Instance("ResetToType"),
            ["rightSlope"] = Instance("rightSlope"), ["shimmer"] = Instance("shimmer"), ["skipLiquid"] = Instance("skipLiquid"),
            ["slope"] = Instance("slope"), ["SmoothSlope"] = new("SmoothSlope", TileMethodLoweringKind.StaticMethodRemainsStatic),
            ["topSlope"] = Instance("topSlope"), ["UseBlockColors"] = Instance("UseBlockColors"), ["wallColor"] = Instance("wallColor"),
            ["wallFrameNumber"] = Instance("wallFrameNumber"), ["wallFrameX"] = Instance("wallFrameX"), ["wallFrameY"] = Instance("wallFrameY"),
            ["water"] = Instance("water"), ["wire"] = Instance("wire"), ["wire2"] = Instance("wire2"), ["wire3"] = Instance("wire3"), ["wire4"] = Instance("wire4")
        };

    public static bool TryGet(string tileMember, out TileMethodLoweringDescriptor? descriptor)
    {
        string name = ExtractMethodName(tileMember);
        return Descriptors.TryGetValue(name, out descriptor);
    }

    public static IReadOnlyList<string> FindUnsupportedMembers(IEnumerable<TileMemberInstructionSnapshot> instructions)
    {
        ArgumentNullException.ThrowIfNull(instructions);
        return instructions
            .Where(instruction => !TryGet(instruction.Member, out _))
            .Select(instruction => instruction.Member)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(member => member, StringComparer.Ordinal)
            .ToArray();
    }

    private static TileMethodLoweringDescriptor Instance(string name)
    {
        return new TileMethodLoweringDescriptor(name, TileMethodLoweringKind.InstanceMethodBecomesHandleMethod);
    }

    private static string ExtractMethodName(string member)
    {
        const string separator = "::";
        int start = member.LastIndexOf(separator, StringComparison.Ordinal);
        if (start < 0)
            return string.Empty;
        start += separator.Length;
        int end = member.IndexOf('(', start);
        return end >= start ? member.Substring(start, end - start) : member.Substring(start);
    }
}
