using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

internal static class Program
{
    private const string GeneratedForVersion = "1.4.5.6";

    private static readonly string[] DynamicBooleanSets =
    {
        "DontDrawTileSliced",
        "DontDrawTileSlopes",
        "Platforms",
        "NotReallySolid",
        "HasOutlines",
        "IgnoreDrawLightConditions",
        "IsVine",
        "VineThreads",
        "ReverseVineThreads",
        "IsBeam",
        "IsLivingFire",
        "SwaysInWindBasic",
        "IsATreeTrunk",
        "CountsAsGemTree",
        "DrawsWalls",
        "Falling",
        "Boulders",
        "Clouds",
        "MergesWithClouds",
        "CrackedBricks",
        "IsAMechanism",
        "IsATrigger",
    };

    private static int Main(string[] args)
    {
        if (args.Length != 2)
        {
            Console.Error.WriteLine("Usage: Alacrity.StaticTileChunkAudit <decompiled-terraria-root> <generated-source-path>");
            return 2;
        }

        string root = Path.GetFullPath(args[0]);
        string mainPath = Path.Combine(root, "Terraria", "Main.cs");
        string tileIdPath = Path.Combine(root, "Terraria.ID", "TileID.cs");
        if (!File.Exists(mainPath) || !File.Exists(tileIdPath))
        {
            Console.Error.WriteLine("The decompiled Terraria root must contain Terraria/Main.cs and Terraria.ID/TileID.cs.");
            return 2;
        }

        string mainSource = File.ReadAllText(mainPath);
        string tileIdSource = File.ReadAllText(tileIdPath);
        bool[] eligible = BuildEligibilityTable(mainSource, tileIdSource);
        string outputPath = Path.GetFullPath(args[1]);
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        File.WriteAllText(outputPath, RenderGeneratedSource(eligible), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        Console.WriteLine("Generated " + CountEligibleTypes(eligible) + " static tile types for Terraria " + GeneratedForVersion + ".");
        return 0;
    }

    private static bool[] BuildEligibilityTable(string mainSource, string tileIdSource)
    {
        int tileTypeCount = ParseTileTypeCount(tileIdSource);
        var tileSolid = ParseTrueAssignments(mainSource, "tileSolid");
        var tileSolidTop = ParseTrueAssignments(mainSource, "tileSolidTop");
        var tileBrick = ParseTrueAssignments(mainSource, "tileBrick");
        var tileFrameImportant = ParseTrueAssignments(mainSource, "tileFrameImportant");
        var tileShine = ParseTrueAssignments(mainSource, "tileShine2");
        var tileFlame = ParseTrueAssignments(mainSource, "tileFlame");
        var tileGlowMask = ParseNonNegativeAssignments(mainSource, "tileGlowMask");
        var tileFrame = ParseNonZeroAssignments(mainSource, "tileFrame");
        var dynamicTypes = new HashSet<int>();
        foreach (string setName in DynamicBooleanSets)
        {
            dynamicTypes.UnionWith(ParseBooleanSet(tileIdSource, setName));
        }

        dynamicTypes.UnionWith(ParseIntSetWithNonDefaultValue(tileIdSource, "DrawFlipMode", 0));
        dynamicTypes.UnionWith(ParseIntSetWithNonDefaultValue(tileIdSource, "CritterCageLidStyle", -1));

        var eligible = new bool[tileTypeCount];
        for (int type = 0; type < eligible.Length; type++)
        {
            eligible[type] = tileSolid.Contains(type) &&
                !tileSolidTop.Contains(type) &&
                tileBrick.Contains(type) &&
                !tileFrameImportant.Contains(type) &&
                !tileShine.Contains(type) &&
                !tileFlame.Contains(type) &&
                !tileGlowMask.Contains(type) &&
                !tileFrame.Contains(type) &&
                !dynamicTypes.Contains(type);
        }

        return eligible;
    }

    private static int ParseTileTypeCount(string source)
    {
        Match match = Regex.Match(source, @"public\s+static\s+readonly\s+ushort\s+Count\s*=\s*(?<value>\d+)");
        if (!match.Success)
        {
            throw new InvalidOperationException("Could not locate TileID.Count in the decompiled source.");
        }

        return int.Parse(match.Groups["value"].Value, CultureInfo.InvariantCulture);
    }

    private static HashSet<int> ParseTrueAssignments(string source, string fieldName)
    {
        return ParseIndexedAssignments(source, fieldName, @"true");
    }

    private static HashSet<int> ParseNonNegativeAssignments(string source, string fieldName)
    {
        return ParseIndexedAssignments(source, fieldName, @"(?!-)\d+");
    }

    private static HashSet<int> ParseNonZeroAssignments(string source, string fieldName)
    {
        return ParseIndexedAssignments(source, fieldName, @"(?!0(?:\D|$))\d+");
    }

    private static HashSet<int> ParseIndexedAssignments(string source, string fieldName, string valuePattern)
    {
        var result = new HashSet<int>();
        foreach (Match match in Regex.Matches(source, @"\b" + Regex.Escape(fieldName) + @"\[(?<index>\d+)\]\s*=\s*" + valuePattern + @"\s*;"))
        {
            result.Add(int.Parse(match.Groups["index"].Value, CultureInfo.InvariantCulture));
        }

        return result;
    }

    private static HashSet<int> ParseBooleanSet(string source, string fieldName)
    {
        Match match = Regex.Match(source, @"public\s+static\s+bool\[\]\s+" + Regex.Escape(fieldName) + @"\s*=\s*Factory\.CreateBoolSet\((?<values>.*?)\);", RegexOptions.Singleline);
        if (!match.Success)
        {
            throw new InvalidOperationException("Could not locate TileID.Sets." + fieldName + " in the decompiled source.");
        }

        return new HashSet<int>(ParseIntegerLiterals(match.Groups["values"].Value));
    }

    private static HashSet<int> ParseIntSetWithNonDefaultValue(string source, string fieldName, int defaultValue)
    {
        Match match = Regex.Match(source, @"public\s+static\s+int\[\]\s+" + Regex.Escape(fieldName) + @"\s*=\s*Factory\.CreateIntSet\((?<values>.*?)\);", RegexOptions.Singleline);
        if (!match.Success)
        {
            throw new InvalidOperationException("Could not locate TileID.Sets." + fieldName + " in the decompiled source.");
        }

        List<int> values = ParseIntegerLiterals(match.Groups["values"].Value);
        var result = new HashSet<int>();
        for (int index = 1; index + 1 < values.Count; index += 2)
        {
            if (values[index + 1] != defaultValue)
            {
                result.Add(values[index]);
            }
        }

        return result;
    }

    private static List<int> ParseIntegerLiterals(string source)
    {
        var values = new List<int>();
        foreach (Match match in Regex.Matches(source, @"-?\d+"))
        {
            values.Add(int.Parse(match.Value, CultureInfo.InvariantCulture));
        }

        return values;
    }

    private static string RenderGeneratedSource(bool[] eligible)
    {
        int wordCount = (eligible.Length + 63) / 64;
        var builder = new StringBuilder(wordCount * 24 + 900);
        builder.AppendLine("// <auto-generated />");
        builder.AppendLine("// Generated by tools/StaticTileChunkAudit against Terraria " + GeneratedForVersion + ".");
        builder.AppendLine("// Do not edit by hand. Regenerate after changing the audit rules or Terraria version.");
        builder.AppendLine("namespace AlacrityTerraria.Rendering.TileChunks;");
        builder.AppendLine();
        builder.AppendLine("internal static class StaticTileChunkEligibility");
        builder.AppendLine("{");
        builder.AppendLine("    private static readonly ulong[] EligibleTypeWords =");
        builder.AppendLine("    {");
        for (int wordIndex = 0; wordIndex < wordCount; wordIndex++)
        {
            ulong word = 0;
            int firstType = wordIndex * 64;
            int lastType = Math.Min(firstType + 64, eligible.Length);
            for (int type = firstType; type < lastType; type++)
            {
                if (eligible[type])
                {
                    word |= 1UL << (type & 63);
                }
            }

            builder.Append("        0x");
            builder.Append(word.ToString("X16", CultureInfo.InvariantCulture));
            builder.AppendLine("UL,");
        }

        builder.AppendLine("    };");
        builder.AppendLine();
        builder.AppendLine("    internal static bool IsEligible(ushort type)");
        builder.AppendLine("    {");
        builder.AppendLine("        int wordIndex = type >> 6;");
        builder.AppendLine("        return wordIndex < EligibleTypeWords.Length &&");
        builder.AppendLine("            (EligibleTypeWords[wordIndex] & (1UL << (type & 63))) != 0;");
        builder.AppendLine("    }");
        builder.AppendLine("}");
        return builder.ToString();
    }

    private static int CountEligibleTypes(bool[] eligible)
    {
        int count = 0;
        for (int index = 0; index < eligible.Length; index++)
        {
            if (eligible[index])
            {
                count++;
            }
        }

        return count;
    }
}
