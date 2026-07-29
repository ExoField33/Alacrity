/// <summary>
/// A deterministic, version-bound ledger of the exact methods and metadata
/// surfaces that must migrate together before compact tile storage can replace
/// Terraria's Tile[,] representation. The manifest is planning data only; it
/// cannot authorize a production rewrite by itself.
/// </summary>
public sealed class TileMigrationManifest
{
    public int SchemaVersion { get; init; } = 1;
    public string InputSha256 { get; init; } = string.Empty;
    public string StorageRepresentation { get; init; } = "Tile[,] -> flat compact value storage";
    public bool CanTransform { get; init; }
    public IReadOnlyList<string> Blockers { get; init; } = Array.Empty<string>();
    public IReadOnlyList<TileMigrationMethodDomain> MethodDomains { get; init; } = Array.Empty<TileMigrationMethodDomain>();
    public IReadOnlyList<TileMigrationSignatureDomain> SignatureDomains { get; init; } = Array.Empty<TileMigrationSignatureDomain>();
    public IReadOnlyList<string> TileReferenceFields { get; init; } = Array.Empty<string>();
    public IReadOnlyList<TileMigrationRuntimeDomain> RuntimeDomains { get; init; } = Array.Empty<TileMigrationRuntimeDomain>();
}

public sealed class TileMigrationMethodDomain
{
    public string Method { get; init; } = string.Empty;
    public IReadOnlyList<TileMigrationOperation> Operations { get; init; } = Array.Empty<TileMigrationOperation>();
    public IReadOnlyList<string> CalledTileSignatureTargets { get; init; } = Array.Empty<string>();
}

public sealed class TileMigrationSignatureDomain
{
    public string Method { get; init; } = string.Empty;
    public bool ReturnsTile { get; init; }
    public IReadOnlyList<TileParameterContractSnapshot> TileParameters { get; init; } = Array.Empty<TileParameterContractSnapshot>();
    public bool HasDirectTileFieldWrite { get; init; }
    public bool CallsTileMutator { get; init; }
    public bool HasNullLiteral { get; init; }
    public IReadOnlyList<string> CallSites { get; init; } = Array.Empty<string>();
}

public sealed class TileMigrationRuntimeDomain
{
    public string RuntimeUse { get; init; } = string.Empty;
    public int Count { get; init; }
}

public sealed class TileMigrationOperation
{
    public string Category { get; init; } = string.Empty;
    public string Location { get; init; } = string.Empty;
    public string Detail { get; init; } = string.Empty;
}

/// <summary>
/// Converts the validated audit into atomic method and signature domains. Every
/// input location is checked because an imprecise ledger must never feed an IL
/// rewriter.
/// </summary>
public static class TileMigrationManifestBuilder
{
    public static TileMigrationManifest Create(TileStorageAuditSnapshot audit)
    {
        ArgumentNullException.ThrowIfNull(audit);
        if (string.IsNullOrWhiteSpace(audit.Sha256))
            throw new InvalidOperationException("A tile migration manifest requires the audited executable hash.");
        if (audit.UnsupportedTileArrayCalls.Count != 0 || audit.UnclassifiedTileStores.Count != 0)
            throw new InvalidOperationException("An audit with unsupported Tile[,] operations cannot produce a migration manifest.");

        TileLoweringPreflight preflight = TileLoweringPreflight.Evaluate(audit);
        if (preflight.Violations.Count != 0)
            throw new InvalidOperationException("An audit with unsupported Tile lowering shapes cannot produce a migration manifest.");

        var operations = new List<TileMigrationOperation>();
        Add(operations, "TileArrayGet", audit.TileArrayGetFlows, flow => flow.Location, flow => $"{flow.Strategy}; {flow.Consumer}");
        Add(operations, "TileFieldInstruction", audit.TileFieldInstructions, instruction => instruction.Location, instruction => $"{instruction.OpCode}; {instruction.Member}");
        Add(operations, "TileMethodInstruction", audit.TileMethodInstructions, instruction => instruction.Location, instruction => $"{instruction.OpCode}; {instruction.Member}");
        Add(operations, "TileConstructorInstruction", audit.TileConstructorInstructions, instruction => instruction.Location, instruction => $"{instruction.OpCode}; {instruction.Member}");
        Add(operations, "TileArraySet", audit.TileStores, store => store.Location, store => $"{store.Kind}; {store.Producer}");
        Add(operations, "TileNullCheck", audit.TileNullChecks, check => check.Location, check => $"{check.Kind}; {check.BranchOffset}");
        Add(operations, "TileLocalAlias", audit.TileLocalAliasFlows, alias => alias.Location, alias => $"local {alias.Local}; {string.Join(", ", alias.UseKinds)}");
        Add(operations, "TileStackFlow", audit.TileStackFlows, flow => flow.Location, flow => string.Join(", ", flow.Outcomes));
        Add(operations, "TileSignatureCall", audit.TileSignatureCalls, call => call.Location, call => $"{call.Target}; return={call.CarriesTileReturn}; parameters={call.TileParameterCount}");

        Dictionary<string, string[]> signatureCallSites = audit.TileSignatureCalls
            .GroupBy(call => call.Target, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.Select(call => call.Location).OrderBy(location => location, StringComparer.Ordinal).ToArray(),
                StringComparer.Ordinal);

        TileMigrationMethodDomain[] methodDomains = operations
            .GroupBy(operation => GetMethod(operation.Location), StringComparer.Ordinal)
            .Select(group => new TileMigrationMethodDomain
            {
                Method = group.Key,
                Operations = group.OrderBy(operation => operation.Location, StringComparer.Ordinal)
                    .ThenBy(operation => operation.Category, StringComparer.Ordinal)
                    .ThenBy(operation => operation.Detail, StringComparer.Ordinal)
                    .ToArray(),
                CalledTileSignatureTargets = group.Where(operation => operation.Category == "TileSignatureCall")
                    .Select(operation => ExtractSignatureTarget(operation.Detail))
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(target => target, StringComparer.Ordinal)
                    .ToArray()
            })
            .OrderBy(domain => domain.Method, StringComparer.Ordinal)
            .ToArray();

        TileMigrationSignatureDomain[] signatureDomains = audit.TileSignatureContracts
            .Select(contract => new TileMigrationSignatureDomain
            {
                Method = contract.Method,
                ReturnsTile = contract.ReturnsTile,
                TileParameters = contract.TileParameters.OrderBy(parameter => parameter.Index).ToArray(),
                HasDirectTileFieldWrite = contract.HasDirectTileFieldWrite,
                CallsTileMutator = contract.CallsTileMutator,
                HasNullLiteral = contract.HasNullLiteral,
                CallSites = signatureCallSites.TryGetValue(contract.Method, out string[]? locations) ? locations : Array.Empty<string>()
            })
            .OrderBy(domain => domain.Method, StringComparer.Ordinal)
            .ToArray();

        TileTransformationPlan plan = TileTransformationPlanner.CreatePlan(audit);
        return new TileMigrationManifest
        {
            InputSha256 = audit.Sha256,
            CanTransform = false,
            Blockers = plan.Blockers,
            MethodDomains = methodDomains,
            SignatureDomains = signatureDomains,
            TileReferenceFields = audit.TileReferenceFields.OrderBy(field => field, StringComparer.Ordinal).ToArray(),
            RuntimeDomains = audit.TileRuntimeTypeUses
                .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => new TileMigrationRuntimeDomain { RuntimeUse = pair.Key, Count = pair.Value })
                .ToArray()
        };
    }

    private static void Add<T>(List<TileMigrationOperation> operations, string category, IEnumerable<T> source, Func<T, string> location, Func<T, string> detail)
    {
        foreach (T entry in source)
        {
            string value = location(entry);
            _ = GetMethod(value);
            operations.Add(new TileMigrationOperation { Category = category, Location = value, Detail = detail(entry) });
        }
    }

    private static string GetMethod(string location)
    {
        int marker = location.LastIndexOf("@IL_", StringComparison.Ordinal);
        if (marker <= 0 || marker + 4 == location.Length || !location[(marker + 4)..].All(Uri.IsHexDigit))
            throw new InvalidOperationException($"The audited location is not an exact IL location: {location}");
        return location[..marker];
    }

    private static string ExtractSignatureTarget(string detail)
    {
        int separator = detail.IndexOf("; return=", StringComparison.Ordinal);
        if (separator <= 0)
            throw new InvalidOperationException($"The tile signature call detail is malformed: {detail}");
        return detail[..separator];
    }
}
