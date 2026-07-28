public sealed record TileFieldLoweringDescriptor(string FieldName, string GetterName, string SetterName);

/// <summary>
/// Version-locked mapping for Terraria.Tile's complete raw storage layout.
/// Any field outside this table blocks a live lowerer rather than falling back
/// to detached value semantics.
/// </summary>
public static class TileFieldLoweringCatalog
{
    private static readonly IReadOnlyDictionary<string, TileFieldLoweringDescriptor> Descriptors =
        new Dictionary<string, TileFieldLoweringDescriptor>(StringComparer.Ordinal)
        {
            ["type"] = new("type", "GetTypeValue", "SetTypeValue"),
            ["wall"] = new("wall", "GetWall", "SetWall"),
            ["liquid"] = new("liquid", "GetLiquid", "SetLiquid"),
            ["sTileHeader"] = new("sTileHeader", "GetTileHeader", "SetTileHeader"),
            ["bTileHeader"] = new("bTileHeader", "GetHeader", "SetHeader"),
            ["bTileHeader2"] = new("bTileHeader2", "GetHeader2", "SetHeader2"),
            ["bTileHeader3"] = new("bTileHeader3", "GetHeader3", "SetHeader3"),
            ["frameX"] = new("frameX", "GetFrameX", "SetFrameX"),
            ["frameY"] = new("frameY", "GetFrameY", "SetFrameY")
        };

    public static bool TryGet(string tileMember, out TileFieldLoweringDescriptor? descriptor)
    {
        string name = ExtractFieldName(tileMember);
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

    private static string ExtractFieldName(string member)
    {
        const string separator = "::";
        int index = member.LastIndexOf(separator, StringComparison.Ordinal);
        return index >= 0 ? member.Substring(index + separator.Length) : string.Empty;
    }
}
