using Mono.Cecil;
using Mono.Cecil.Cil;

/// <summary>
/// Rewrites a deliberately small, separately compiled fixture from <c>Tile[,]</c>
/// objects to a flat value array. It is an executable proof for the direct field
/// lowering primitive; it is not eligible to transform Terraria.
/// </summary>
public sealed class FixtureTileFieldLowerer
{
    private const string FixtureNamespace = "TileStorageTransformFixture";
    private const string TileTypeName = FixtureNamespace + ".Tile";
    private const string MainTypeName = FixtureNamespace + ".Main";
    private const string DataTypeName = "CompactTileData";
    private const string ReferenceTypeName = "CompactTileReference";

    public FixtureTileLoweringResult Rewrite(string inputAssemblyPath, string outputAssemblyPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(inputAssemblyPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputAssemblyPath);
        if (!File.Exists(inputAssemblyPath))
            throw new FileNotFoundException("The fixture assembly was not found.", inputAssemblyPath);

        using AssemblyDefinition assembly = AssemblyDefinition.ReadAssembly(inputAssemblyPath, new ReaderParameters { InMemory = true });
        ModuleDefinition module = assembly.MainModule;
        TypeDefinition tileType = RequireType(module, TileTypeName);
        TypeDefinition mainType = RequireType(module, MainTypeName);
        TypeDefinition runtimeType = RequireType(module, FixtureNamespace + ".TileRuntime");
        TypeDefinition framingType = RequireType(module, FixtureNamespace + ".Framing");
        TypeDefinition playerType = RequireType(module, FixtureNamespace + ".Player");
        TypeDefinition sittingType = RequireType(module, FixtureNamespace + ".PlayerSittingHelper");
        TypeDefinition worldGenType = RequireType(module, FixtureNamespace + ".WorldGen");
        FieldDefinition originalTileField = RequireField(mainType, "tile");
        FieldDefinition storedTileField = RequireField(mainType, "storedTile");
        FieldDefinition widthField = RequireField(mainType, "width");
        FieldDefinition heightField = RequireField(mainType, "height");
        RequireTileField(tileType, "type", module.TypeSystem.UInt16);
        RequireTileField(tileType, "frameX", module.TypeSystem.Int16);
        if (!IsTwoDimensionalTileArray(originalTileField.FieldType, tileType))
            throw new InvalidOperationException("The fixture tile field is not the expected Tile[,] storage shape.");
        if (!IsInt32(widthField.FieldType))
            throw new InvalidOperationException("The fixture width field is not an Int32.");
        if (!IsInt32(heightField.FieldType))
            throw new InvalidOperationException("The fixture height field is not an Int32.");

        TypeDefinition dataType = RequireType(module, FixtureNamespace + "." + DataTypeName);
        TypeDefinition referenceType = RequireType(module, FixtureNamespace + "." + ReferenceTypeName);
        FieldDefinition dataTypeField = RequireField(dataType, "Type");
        FieldDefinition dataFrameXField = RequireField(dataType, "FrameX");
        FieldDefinition tileField = ReplaceTileField(mainType, originalTileField, dataType);
        storedTileField.FieldType = referenceType;
        FieldDefinition materializedField = AddMaterializationField(mainType, module);
        FieldDefinition referenceStateField = AddReferenceStateField(mainType, referenceType);

        RewriteInitialize(mainType, tileField, materializedField, referenceStateField, widthField, heightField, dataType, runtimeType);
        RewriteRead(mainType, "ReadType", tileField, materializedField, widthField, dataType, dataTypeField, runtimeType);
        RewriteWrite(mainType, "WriteType", tileField, materializedField, widthField, dataType, dataTypeField, runtimeType);
        RewriteRead(mainType, "ReadFrameX", tileField, materializedField, widthField, dataType, dataFrameXField, runtimeType);
        RewriteWrite(mainType, "WriteFrameX", tileField, materializedField, widthField, dataType, dataFrameXField, runtimeType);
        RewriteIsMissing(mainType, materializedField, widthField);
        RewriteClear(mainType, referenceStateField, widthField, runtimeType);
        RewriteEnsureAndWrite(mainType, tileField, materializedField, widthField, dataType, dataTypeField);
        RewriteCopy(mainType, tileField, materializedField, referenceStateField, widthField, dataType, runtimeType);
        RewriteReadActive(mainType, tileField, materializedField, widthField, dataType, dataTypeField, runtimeType);
        RewriteGetLength(mainType, "GetWidth", widthField);
        RewriteGetLength(mainType, "GetHeight", heightField);
        RewriteGetCell(mainType, tileType, referenceType, referenceStateField, widthField, runtimeType);
        RewriteReadTypeThroughLocal(mainType, referenceType, referenceStateField, widthField, runtimeType);
        RewriteWriteTypeThroughParameter(mainType, tileType, referenceType, runtimeType);
        RewriteWriteTypeViaParameter(mainType, referenceType, referenceStateField, widthField, runtimeType);
        RewriteWriteTypeThroughReturnedCell(mainType, referenceType, referenceStateField, widthField, runtimeType);
        RewriteReturnedCellIsMissing(mainType, referenceType, referenceStateField, widthField, runtimeType);
        RewriteStoredReferenceMethods(mainType, referenceType, storedTileField, referenceStateField, widthField, runtimeType);
        RewriteByReferenceMethods(mainType, tileType, referenceType, referenceStateField, widthField, runtimeType);
        RewriteAddressMethods(mainType, tileType, referenceType, referenceStateField, widthField, runtimeType);
        RewriteExternalPatterns(mainType, tileType, referenceType, referenceStateField, widthField, runtimeType, framingType, playerType, sittingType, worldGenType);
        RewriteCompactReferenceStaticInitialization(mainType, storedTileField, referenceType);

        ValidateNoFixtureTileStorageReferences(mainType, tileType);
        string? outputDirectory = Path.GetDirectoryName(Path.GetFullPath(outputAssemblyPath));
        if (string.IsNullOrEmpty(outputDirectory))
            throw new InvalidOperationException("The transformed fixture output must have a parent directory.");
        Directory.CreateDirectory(outputDirectory);
        assembly.Write(outputAssemblyPath);
        return new FixtureTileLoweringResult(dataType.FullName, 37);
    }

    private static FieldDefinition AddMaterializationField(TypeDefinition mainType, ModuleDefinition module)
    {
        if (mainType.Fields.Any(field => string.Equals(field.Name, "__alacrityMaterialized", StringComparison.Ordinal)))
            throw new InvalidOperationException("The fixture has already been lowered.");

        var field = new FieldDefinition("__alacrityMaterialized", FieldAttributes.Assembly | FieldAttributes.Static, new ArrayType(module.TypeSystem.Boolean));
        mainType.Fields.Add(field);
        return field;
    }

    private static FieldDefinition AddReferenceStateField(TypeDefinition mainType, TypeDefinition referenceType)
    {
        var field = new FieldDefinition("__alacrityReferenceState", FieldAttributes.Assembly | FieldAttributes.Static, RequireType(mainType.Module, FixtureNamespace + ".CompactTileReferenceState"));
        mainType.Fields.Add(field);
        return field;
    }

    private static FieldDefinition ReplaceTileField(TypeDefinition mainType, FieldDefinition original, TypeDefinition dataType)
    {
        if (!ReferenceEquals(original.DeclaringType, mainType) || !mainType.Fields.Contains(original))
            throw new InvalidOperationException("The fixture tile field is not owned by the expected type.");

        original.FieldType = new ArrayType(dataType);
        return original;
    }

    private static void RewriteInitialize(TypeDefinition mainType, FieldDefinition tileField, FieldDefinition materializedField, FieldDefinition referenceStateField, FieldDefinition widthField, FieldDefinition heightField, TypeDefinition dataType, TypeDefinition runtimeType)
    {
        MethodDefinition method = RequireMethod(mainType, "Initialize", 2, mainType.Module.TypeSystem.Void);
        ILProcessor il = ResetBody(method);
        il.Append(il.Create(OpCodes.Ldarg_0));
        il.Append(il.Create(OpCodes.Stsfld, widthField));
        il.Append(il.Create(OpCodes.Ldarg_1));
        il.Append(il.Create(OpCodes.Stsfld, heightField));
        il.Append(il.Create(OpCodes.Ldarg_0));
        il.Append(il.Create(OpCodes.Ldarg_1));
        il.Append(il.Create(OpCodes.Mul));
        il.Append(il.Create(OpCodes.Newarr, dataType));
        il.Append(il.Create(OpCodes.Stsfld, tileField));
        il.Append(il.Create(OpCodes.Ldarg_0));
        il.Append(il.Create(OpCodes.Ldarg_1));
        il.Append(il.Create(OpCodes.Mul));
        il.Append(il.Create(OpCodes.Newarr, mainType.Module.TypeSystem.Boolean));
        il.Append(il.Create(OpCodes.Stsfld, materializedField));
        il.Append(il.Create(OpCodes.Ldsfld, materializedField));
        il.Append(il.Create(OpCodes.Call, RequireRuntimeMethod(runtimeType, "FillMaterialized", 1)));
        il.Append(il.Create(OpCodes.Ldsfld, tileField));
        il.Append(il.Create(OpCodes.Ldsfld, materializedField));
        il.Append(il.Create(OpCodes.Call, RequireRuntimeMethod(runtimeType, "CreateReferenceState", 2)));
        il.Append(il.Create(OpCodes.Stsfld, referenceStateField));
        il.Append(il.Create(OpCodes.Ret));
    }

    private static void RewriteRead(TypeDefinition mainType, string name, FieldDefinition tileField, FieldDefinition materializedField, FieldDefinition widthField, TypeDefinition dataType, FieldDefinition dataField, TypeDefinition runtimeType)
    {
        MethodDefinition method = RequireMethod(mainType, name, 2, dataField.FieldType);
        ILProcessor il = ResetBody(method);
        EmitRequireMaterialized(il, materializedField, widthField, runtimeType, 0, 1);
        EmitCellAddress(il, tileField, widthField, dataType);
        il.Append(il.Create(OpCodes.Ldfld, dataField));
        il.Append(il.Create(OpCodes.Ret));
    }

    private static void RewriteWrite(TypeDefinition mainType, string name, FieldDefinition tileField, FieldDefinition materializedField, FieldDefinition widthField, TypeDefinition dataType, FieldDefinition dataField, TypeDefinition runtimeType)
    {
        MethodDefinition method = RequireMethod(mainType, name, 3, mainType.Module.TypeSystem.Void);
        ILProcessor il = ResetBody(method);
        EmitRequireMaterialized(il, materializedField, widthField, runtimeType, 0, 1);
        EmitCellAddress(il, tileField, widthField, dataType);
        il.Append(il.Create(OpCodes.Ldarg_2));
        il.Append(il.Create(OpCodes.Stfld, dataField));
        il.Append(il.Create(OpCodes.Ldsfld, materializedField));
        il.Append(il.Create(OpCodes.Ldarg_0));
        il.Append(il.Create(OpCodes.Ldarg_1));
        il.Append(il.Create(OpCodes.Ldsfld, widthField));
        il.Append(il.Create(OpCodes.Mul));
        il.Append(il.Create(OpCodes.Add));
        il.Append(il.Create(OpCodes.Ldc_I4_1));
        il.Append(il.Create(OpCodes.Stelem_I1));
        il.Append(il.Create(OpCodes.Ret));
    }

    private static void RewriteIsMissing(TypeDefinition mainType, FieldDefinition materializedField, FieldDefinition widthField)
    {
        MethodDefinition method = RequireMethod(mainType, "IsMissing", 2, mainType.Module.TypeSystem.Boolean);
        ILProcessor il = ResetBody(method);
        il.Append(il.Create(OpCodes.Ldsfld, materializedField));
        il.Append(il.Create(OpCodes.Ldarg_0));
        il.Append(il.Create(OpCodes.Ldarg_1));
        il.Append(il.Create(OpCodes.Ldsfld, widthField));
        il.Append(il.Create(OpCodes.Mul));
        il.Append(il.Create(OpCodes.Add));
        il.Append(il.Create(OpCodes.Ldelem_U1));
        il.Append(il.Create(OpCodes.Ldc_I4_0));
        il.Append(il.Create(OpCodes.Ceq));
        il.Append(il.Create(OpCodes.Ret));
    }

    private static void RewriteClear(TypeDefinition mainType, FieldDefinition referenceStateField, FieldDefinition widthField, TypeDefinition runtimeType)
    {
        MethodDefinition method = RequireMethod(mainType, "Clear", 2, mainType.Module.TypeSystem.Void);
        ILProcessor il = ResetBody(method);
        il.Append(il.Create(OpCodes.Ldsfld, referenceStateField));
        EmitCellIndex(il, widthField, 0, 1);
        il.Append(il.Create(OpCodes.Call, RequireRuntimeMethod(runtimeType, "ClearCompactCell", 2)));
        il.Append(il.Create(OpCodes.Ret));
    }

    private static void RewriteEnsureAndWrite(TypeDefinition mainType, FieldDefinition tileField, FieldDefinition materializedField, FieldDefinition widthField, TypeDefinition dataType, FieldDefinition dataTypeField)
    {
        MethodDefinition method = RequireMethod(mainType, "EnsureAndWriteType", 3, mainType.Module.TypeSystem.Void);
        ILProcessor il = ResetBody(method);
        EmitCellAddress(il, tileField, widthField, dataType);
        il.Append(il.Create(OpCodes.Ldarg_2));
        il.Append(il.Create(OpCodes.Stfld, dataTypeField));
        il.Append(il.Create(OpCodes.Ldsfld, materializedField));
        il.Append(il.Create(OpCodes.Ldarg_0));
        il.Append(il.Create(OpCodes.Ldarg_1));
        il.Append(il.Create(OpCodes.Ldsfld, widthField));
        il.Append(il.Create(OpCodes.Mul));
        il.Append(il.Create(OpCodes.Add));
        il.Append(il.Create(OpCodes.Ldc_I4_1));
        il.Append(il.Create(OpCodes.Stelem_I1));
        il.Append(il.Create(OpCodes.Ret));
    }

    private static void RewriteCopy(TypeDefinition mainType, FieldDefinition tileField, FieldDefinition materializedField, FieldDefinition referenceStateField, FieldDefinition widthField, TypeDefinition dataType, TypeDefinition runtimeType)
    {
        MethodDefinition method = RequireMethod(mainType, "CopyCell", 4, mainType.Module.TypeSystem.Void);
        MethodDefinition copy = runtimeType.Methods.SingleOrDefault(candidate =>
            candidate.Name == "CopyCompactCell" &&
            candidate.IsStatic &&
            candidate.ReturnType.MetadataType == MetadataType.Void &&
            candidate.Parameters.Count == 8)
            ?? throw new InvalidOperationException("The fixture compact runtime copy helper was not found.");
        if (copy.Parameters[0].ParameterType.FullName != tileField.FieldType.FullName ||
            copy.Parameters[1].ParameterType.FullName != materializedField.FieldType.FullName ||
            copy.Parameters.Skip(3).Any(parameter => parameter.ParameterType.MetadataType != MetadataType.Int32))
        {
            throw new InvalidOperationException("The fixture compact runtime copy helper has an incompatible signature.");
        }

        ILProcessor il = ResetBody(method);
        il.Append(il.Create(OpCodes.Ldsfld, tileField));
        il.Append(il.Create(OpCodes.Ldsfld, materializedField));
        il.Append(il.Create(OpCodes.Ldsfld, referenceStateField));
        il.Append(il.Create(OpCodes.Ldsfld, widthField));
        il.Append(il.Create(OpCodes.Ldarg_0));
        il.Append(il.Create(OpCodes.Ldarg_1));
        il.Append(il.Create(OpCodes.Ldarg_2));
        il.Append(il.Create(OpCodes.Ldarg_3));
        il.Append(il.Create(OpCodes.Call, copy));
        il.Append(il.Create(OpCodes.Ret));
    }

    private static void RewriteReadActive(TypeDefinition mainType, FieldDefinition tileField, FieldDefinition materializedField, FieldDefinition widthField, TypeDefinition dataType, FieldDefinition dataTypeField, TypeDefinition runtimeType)
    {
        MethodDefinition method = RequireMethod(mainType, "ReadActive", 2, mainType.Module.TypeSystem.Boolean);
        ILProcessor il = ResetBody(method);
        EmitRequireMaterialized(il, materializedField, widthField, runtimeType, 0, 1);
        EmitCellAddress(il, tileField, widthField, dataType);
        il.Append(il.Create(OpCodes.Ldfld, dataTypeField));
        il.Append(il.Create(OpCodes.Ldc_I4_0));
        il.Append(il.Create(OpCodes.Cgt_Un));
        il.Append(il.Create(OpCodes.Ret));
    }

    private static void RewriteGetLength(TypeDefinition mainType, string name, FieldDefinition dimensionField)
    {
        MethodDefinition method = RequireMethod(mainType, name, 0, mainType.Module.TypeSystem.Int32);
        ILProcessor il = ResetBody(method);
        il.Append(il.Create(OpCodes.Ldsfld, dimensionField));
        il.Append(il.Create(OpCodes.Ret));
    }

    private static void RewriteGetCell(TypeDefinition mainType, TypeDefinition tileType, TypeDefinition referenceType, FieldDefinition referenceStateField, FieldDefinition widthField, TypeDefinition runtimeType)
    {
        MethodDefinition method = RequireMethod(mainType, "GetCell", 2, tileType);
        method.ReturnType = referenceType;
        ILProcessor il = ResetBody(method);
        EmitGetCompactReference(il, referenceStateField, widthField, runtimeType, 0, 1);
        il.Append(il.Create(OpCodes.Ret));
    }

    private static void RewriteReadTypeThroughLocal(TypeDefinition mainType, TypeDefinition referenceType, FieldDefinition referenceStateField, FieldDefinition widthField, TypeDefinition runtimeType)
    {
        MethodDefinition method = RequireMethod(mainType, "ReadTypeThroughLocal", 2, mainType.Module.TypeSystem.UInt16);
        ILProcessor il = ResetBody(method);
        method.Body.InitLocals = true;
        var local = new VariableDefinition(referenceType);
        method.Body.Variables.Add(local);
        EmitGetCompactReference(il, referenceStateField, widthField, runtimeType, 0, 1);
        il.Append(il.Create(OpCodes.Stloc, local));
        il.Append(il.Create(OpCodes.Ldloc, local));
        il.Append(il.Create(OpCodes.Call, RequireRuntimeMethod(runtimeType, "GetCompactTypeValue", 1)));
        il.Append(il.Create(OpCodes.Ret));
    }

    private static void RewriteWriteTypeThroughParameter(TypeDefinition mainType, TypeDefinition tileType, TypeDefinition referenceType, TypeDefinition runtimeType)
    {
        MethodDefinition method = RequireMethod(mainType, "WriteTypeThroughParameter", 2, mainType.Module.TypeSystem.Void);
        if (method.Parameters[0].ParameterType.FullName != tileType.FullName)
            throw new InvalidOperationException("The fixture Tile parameter was not found.");

        method.Parameters[0].ParameterType = referenceType;
        ILProcessor il = ResetBody(method);
        il.Append(il.Create(OpCodes.Ldarg_0));
        il.Append(il.Create(OpCodes.Ldarg_1));
        il.Append(il.Create(OpCodes.Call, RequireRuntimeMethod(runtimeType, "SetCompactTypeValue", 2)));
        il.Append(il.Create(OpCodes.Ret));
    }

    private static void RewriteWriteTypeViaParameter(TypeDefinition mainType, TypeDefinition referenceType, FieldDefinition referenceStateField, FieldDefinition widthField, TypeDefinition runtimeType)
    {
        MethodDefinition method = RequireMethod(mainType, "WriteTypeViaParameter", 3, mainType.Module.TypeSystem.Void);
        MethodDefinition target = RequireMethod(mainType, "WriteTypeThroughParameter", 2, mainType.Module.TypeSystem.Void);
        if (target.Parameters[0].ParameterType.FullName != referenceType.FullName)
            throw new InvalidOperationException("The compact fixture Tile parameter was not rewritten.");

        ILProcessor il = ResetBody(method);
        EmitGetCompactReference(il, referenceStateField, widthField, runtimeType, 0, 1);
        il.Append(il.Create(OpCodes.Ldarg_2));
        il.Append(il.Create(OpCodes.Call, target));
        il.Append(il.Create(OpCodes.Ret));
    }

    private static void RewriteWriteTypeThroughReturnedCell(TypeDefinition mainType, TypeDefinition referenceType, FieldDefinition referenceStateField, FieldDefinition widthField, TypeDefinition runtimeType)
    {
        MethodDefinition method = RequireMethod(mainType, "WriteTypeThroughReturnedCell", 3, mainType.Module.TypeSystem.Void);
        MethodDefinition getCell = RequireMethod(mainType, "GetCell", 2, referenceType);
        ILProcessor il = ResetBody(method);
        il.Append(il.Create(OpCodes.Ldarg_0));
        il.Append(il.Create(OpCodes.Ldarg_1));
        il.Append(il.Create(OpCodes.Call, getCell));
        il.Append(il.Create(OpCodes.Ldarg_2));
        il.Append(il.Create(OpCodes.Call, RequireRuntimeMethod(runtimeType, "SetCompactTypeValue", 2)));
        il.Append(il.Create(OpCodes.Ret));
    }

    private static void RewriteReturnedCellIsMissing(TypeDefinition mainType, TypeDefinition referenceType, FieldDefinition referenceStateField, FieldDefinition widthField, TypeDefinition runtimeType)
    {
        MethodDefinition method = RequireMethod(mainType, "ReturnedCellIsMissing", 2, mainType.Module.TypeSystem.Boolean);
        MethodDefinition getCell = RequireMethod(mainType, "GetCell", 2, referenceType);
        ILProcessor il = ResetBody(method);
        il.Append(il.Create(OpCodes.Ldarg_0));
        il.Append(il.Create(OpCodes.Ldarg_1));
        il.Append(il.Create(OpCodes.Call, getCell));
        il.Append(il.Create(OpCodes.Call, RequireRuntimeMethod(runtimeType, "IsNull", 1)));
        il.Append(il.Create(OpCodes.Ret));
    }

    private static void RewriteStoredReferenceMethods(TypeDefinition mainType, TypeDefinition referenceType, FieldDefinition storedTileField, FieldDefinition referenceStateField, FieldDefinition widthField, TypeDefinition runtimeType)
    {
        MethodDefinition store = RequireMethod(mainType, "StoreCell", 2, mainType.Module.TypeSystem.Void);
        ILProcessor il = ResetBody(store);
        EmitGetCompactReference(il, referenceStateField, widthField, runtimeType, 0, 1);
        il.Append(il.Create(OpCodes.Stsfld, storedTileField));
        il.Append(il.Create(OpCodes.Ret));

        MethodDefinition read = RequireMethod(mainType, "ReadStoredType", 0, mainType.Module.TypeSystem.UInt16);
        il = ResetBody(read);
        il.Append(il.Create(OpCodes.Ldsfld, storedTileField));
        il.Append(il.Create(OpCodes.Call, RequireRuntimeMethod(runtimeType, "GetCompactTypeValue", 1)));
        il.Append(il.Create(OpCodes.Ret));

        MethodDefinition write = RequireMethod(mainType, "WriteStoredType", 1, mainType.Module.TypeSystem.Void);
        il = ResetBody(write);
        il.Append(il.Create(OpCodes.Ldsfld, storedTileField));
        il.Append(il.Create(OpCodes.Ldarg_0));
        il.Append(il.Create(OpCodes.Call, RequireRuntimeMethod(runtimeType, "SetCompactTypeValue", 2)));
        il.Append(il.Create(OpCodes.Ret));

        MethodDefinition missing = RequireMethod(mainType, "IsStoredMissing", 0, mainType.Module.TypeSystem.Boolean);
        il = ResetBody(missing);
        il.Append(il.Create(OpCodes.Ldsfld, storedTileField));
        il.Append(il.Create(OpCodes.Call, RequireRuntimeMethod(runtimeType, "IsNull", 1)));
        il.Append(il.Create(OpCodes.Ret));

        MethodDefinition clear = RequireMethod(mainType, "ClearStored", 0, mainType.Module.TypeSystem.Void);
        il = ResetBody(clear);
        il.Append(il.Create(OpCodes.Ldsflda, storedTileField));
        il.Append(il.Create(OpCodes.Initobj, referenceType));
        il.Append(il.Create(OpCodes.Ret));
    }

    private static void RewriteByReferenceMethods(TypeDefinition mainType, TypeDefinition tileType, TypeDefinition referenceType, FieldDefinition referenceStateField, FieldDefinition widthField, TypeDefinition runtimeType)
    {
        MethodDefinition read = RequireMethod(mainType, "ReadTypeByReference", 1, mainType.Module.TypeSystem.UInt16);
        RewriteByReferenceParameter(read, tileType, referenceType, 0);
        ILProcessor il = ResetBody(read);
        il.Append(il.Create(OpCodes.Ldarg_0));
        il.Append(il.Create(OpCodes.Call, RequireRuntimeMethod(runtimeType, "GetCompactTypeValue", 1)));
        il.Append(il.Create(OpCodes.Ret));

        MethodDefinition readCaller = RequireMethod(mainType, "ReadTypeViaByReference", 2, mainType.Module.TypeSystem.UInt16);
        il = ResetBody(readCaller);
        EmitGetCompactReference(il, referenceStateField, widthField, runtimeType, 0, 1);
        il.Append(il.Create(OpCodes.Call, read));
        il.Append(il.Create(OpCodes.Ret));

        MethodDefinition copy = RequireMethod(mainType, "CopyTypeByReference", 2, mainType.Module.TypeSystem.Void);
        RewriteByReferenceParameter(copy, tileType, referenceType, 0);
        RewriteByReferenceParameter(copy, tileType, referenceType, 1);
        il = ResetBody(copy);
        il.Append(il.Create(OpCodes.Ldarg_0));
        il.Append(il.Create(OpCodes.Ldarg_1));
        il.Append(il.Create(OpCodes.Call, RequireRuntimeMethod(runtimeType, "GetCompactTypeValue", 1)));
        il.Append(il.Create(OpCodes.Call, RequireRuntimeMethod(runtimeType, "SetCompactTypeValue", 2)));
        il.Append(il.Create(OpCodes.Ret));

        MethodDefinition copyCaller = RequireMethod(mainType, "CopyTypeViaByReference", 4, mainType.Module.TypeSystem.Void);
        il = ResetBody(copyCaller);
        EmitGetCompactReference(il, referenceStateField, widthField, runtimeType, 2, 3);
        EmitGetCompactReference(il, referenceStateField, widthField, runtimeType, 0, 1);
        il.Append(il.Create(OpCodes.Call, copy));
        il.Append(il.Create(OpCodes.Ret));
    }

    private static void RewriteAddressMethods(TypeDefinition mainType, TypeDefinition tileType, TypeDefinition referenceType, FieldDefinition referenceStateField, FieldDefinition widthField, TypeDefinition runtimeType)
    {
        MethodDefinition address = mainType.Methods.SingleOrDefault(candidate =>
            string.Equals(candidate.Name, "GetCellAddress", StringComparison.Ordinal) &&
            candidate.Parameters.Count == 2 &&
            candidate.ReturnType is ByReferenceType byReference &&
            byReference.ElementType.FullName == tileType.FullName)
            ?? throw new InvalidOperationException("The fixture Tile[,] Address method was not found.");
        address.ReturnType = referenceType;
        ILProcessor il = ResetBody(address);
        EmitGetCompactReference(il, referenceStateField, widthField, runtimeType, 0, 1);
        il.Append(il.Create(OpCodes.Ret));

        MethodDefinition read = RequireMethod(mainType, "ReadTypeThroughAddress", 2, mainType.Module.TypeSystem.UInt16);
        il = ResetBody(read);
        il.Append(il.Create(OpCodes.Ldarg_0));
        il.Append(il.Create(OpCodes.Ldarg_1));
        il.Append(il.Create(OpCodes.Call, address));
        il.Append(il.Create(OpCodes.Call, RequireRuntimeMethod(runtimeType, "GetCompactTypeValue", 1)));
        il.Append(il.Create(OpCodes.Ret));

        MethodDefinition write = RequireMethod(mainType, "WriteTypeThroughAddress", 3, mainType.Module.TypeSystem.Void);
        il = ResetBody(write);
        il.Append(il.Create(OpCodes.Ldarg_0));
        il.Append(il.Create(OpCodes.Ldarg_1));
        il.Append(il.Create(OpCodes.Call, address));
        il.Append(il.Create(OpCodes.Ldarg_2));
        il.Append(il.Create(OpCodes.Call, RequireRuntimeMethod(runtimeType, "SetCompactTypeValue", 2)));
        il.Append(il.Create(OpCodes.Ret));

        MethodDefinition copy = RequireMethod(mainType, "CopyTypeViaAddresses", 4, mainType.Module.TypeSystem.Void);
        MethodDefinition copyByReference = RequireMethod(mainType, "CopyTypeByReference", 2, mainType.Module.TypeSystem.Void);
        if (copyByReference.Parameters.Any(parameter => parameter.ParameterType.FullName != referenceType.FullName))
            throw new InvalidOperationException("The compact fixture Tile by-reference helper was not rewritten before Address lowering.");
        il = ResetBody(copy);
        il.Append(il.Create(OpCodes.Ldarg_2));
        il.Append(il.Create(OpCodes.Ldarg_3));
        il.Append(il.Create(OpCodes.Call, address));
        il.Append(il.Create(OpCodes.Ldarg_0));
        il.Append(il.Create(OpCodes.Ldarg_1));
        il.Append(il.Create(OpCodes.Call, address));
        il.Append(il.Create(OpCodes.Call, copyByReference));
        il.Append(il.Create(OpCodes.Ret));
    }

    private static void RewriteExternalPatterns(TypeDefinition mainType, TypeDefinition tileType, TypeDefinition referenceType, FieldDefinition referenceStateField, FieldDefinition widthField, TypeDefinition runtimeType, TypeDefinition framingType, TypeDefinition playerType, TypeDefinition sittingType, TypeDefinition worldGenType)
    {
        MethodDefinition framing = RewriteTileReturnHelper(framingType, "GetTileSafely", tileType, referenceType, referenceStateField, widthField, runtimeType);
        MethodDefinition floor = RewriteTileReturnHelper(playerType, "GetFloorTile", tileType, referenceType, referenceStateField, widthField, runtimeType);

        MethodDefinition sitting = RequireMethod(sittingType, "TryGetSittingBlock", 3, mainType.Module.TypeSystem.Boolean);
        if (sitting.Parameters[2].ParameterType is not ByReferenceType { ElementType.FullName: var sittingElement } || sittingElement != tileType.FullName)
            throw new InvalidOperationException("The fixture sitting helper does not have the expected out Tile parameter.");
        sitting.Parameters[2].ParameterType = new ByReferenceType(referenceType);
        ILProcessor il = ResetBody(sitting);
        il.Append(il.Create(OpCodes.Ldarg_2));
        EmitGetCompactReference(il, referenceStateField, widthField, runtimeType, 0, 1);
        il.Append(il.Create(OpCodes.Stobj, referenceType));
        il.Append(il.Create(OpCodes.Ldarg_2));
        il.Append(il.Create(OpCodes.Ldobj, referenceType));
        il.Append(il.Create(OpCodes.Call, RequireRuntimeMethod(runtimeType, "IsNull", 1)));
        il.Append(il.Create(OpCodes.Ldc_I4_0));
        il.Append(il.Create(OpCodes.Ceq));
        il.Append(il.Create(OpCodes.Ret));

        MethodDefinition convert = RequireMethod(worldGenType, "Convert_ActuallyConvertTile", 2, mainType.Module.TypeSystem.Void);
        RewriteByReferenceParameter(convert, tileType, referenceType, 0);
        il = ResetBody(convert);
        il.Append(il.Create(OpCodes.Ldarg_0));
        il.Append(il.Create(OpCodes.Ldarg_1));
        il.Append(il.Create(OpCodes.Call, RequireRuntimeMethod(runtimeType, "SetCompactTypeValue", 2)));
        il.Append(il.Create(OpCodes.Ret));

        RewriteExternalMainCallers(mainType, tileType, referenceType, referenceStateField, widthField, runtimeType, framing, floor, sitting, convert);
        RewriteReferenceFields(mainType, tileType, referenceType, referenceStateField, widthField, runtimeType);
    }

    private static MethodDefinition RewriteTileReturnHelper(TypeDefinition owner, string name, TypeDefinition tileType, TypeDefinition referenceType, FieldDefinition referenceStateField, FieldDefinition widthField, TypeDefinition runtimeType)
    {
        MethodDefinition method = RequireMethod(owner, name, 2, tileType);
        method.ReturnType = referenceType;
        ILProcessor il = ResetBody(method);
        EmitGetCompactReference(il, referenceStateField, widthField, runtimeType, 0, 1);
        il.Append(il.Create(OpCodes.Ret));
        return method;
    }

    private static void RewriteExternalMainCallers(TypeDefinition mainType, TypeDefinition tileType, TypeDefinition referenceType, FieldDefinition referenceStateField, FieldDefinition widthField, TypeDefinition runtimeType, MethodDefinition framing, MethodDefinition floor, MethodDefinition sitting, MethodDefinition convert)
    {
        RewriteReadThroughHelper(mainType, "ReadTypeThroughFraming", framing, runtimeType);
        RewriteReadThroughHelper(mainType, "ReadTypeThroughFloorTile", floor, runtimeType);

        MethodDefinition tryGet = RequireMethod(mainType, "TryGetSittingBlock", 3, mainType.Module.TypeSystem.Boolean);
        RewriteOutParameter(tryGet, tileType, referenceType, 2);
        ILProcessor il = ResetBody(tryGet);
        il.Append(il.Create(OpCodes.Ldarg_0));
        il.Append(il.Create(OpCodes.Ldarg_1));
        il.Append(il.Create(OpCodes.Ldarg_2));
        il.Append(il.Create(OpCodes.Call, sitting));
        il.Append(il.Create(OpCodes.Ret));

        MethodDefinition convertCaller = RequireMethod(mainType, "ConvertTile", 3, mainType.Module.TypeSystem.Void);
        il = ResetBody(convertCaller);
        EmitGetCompactReference(il, referenceStateField, widthField, runtimeType, 0, 1);
        il.Append(il.Create(OpCodes.Ldarg_2));
        il.Append(il.Create(OpCodes.Call, convert));
        il.Append(il.Create(OpCodes.Ret));
    }

    private static void RewriteReadThroughHelper(TypeDefinition mainType, string name, MethodDefinition helper, TypeDefinition runtimeType)
    {
        MethodDefinition method = RequireMethod(mainType, name, 2, mainType.Module.TypeSystem.UInt16);
        ILProcessor il = ResetBody(method);
        il.Append(il.Create(OpCodes.Ldarg_0));
        il.Append(il.Create(OpCodes.Ldarg_1));
        il.Append(il.Create(OpCodes.Call, helper));
        il.Append(il.Create(OpCodes.Call, RequireRuntimeMethod(runtimeType, "GetCompactTypeValue", 1)));
        il.Append(il.Create(OpCodes.Ret));
    }

    private static void RewriteReferenceFields(TypeDefinition mainType, TypeDefinition tileType, TypeDefinition referenceType, FieldDefinition referenceStateField, FieldDefinition widthField, TypeDefinition runtimeType)
    {
        (TypeDefinition Owner, FieldDefinition Field)[] fields =
        {
            (RequireType(mainType.Module, FixtureNamespace + ".TileDrawInfo"), null!),
            (RequireType(mainType.Module, FixtureNamespace + ".DartTrapPlacementAttempt"), null!),
            (RequireType(mainType.Module, FixtureNamespace + ".BallCollisionEvent"), null!),
            (RequireType(mainType.Module, FixtureNamespace + ".BallPassThroughEvent"), null!)
        };
        string[] names = { "tileCache", "t", "Tile", "Tile" };
        for (int index = 0; index < fields.Length; index++)
        {
            FieldDefinition field = RequireField(fields[index].Owner, names[index]);
            if (field.FieldType.FullName != tileType.FullName)
                throw new InvalidOperationException($"The fixture Tile reference field {field.FullName} was not found.");
            fields[index] = (fields[index].Owner, field);
        }

        FieldDefinition[] owners = { RequireField(mainType, "drawInfo"), RequireField(mainType, "dartTrap"), RequireField(mainType, "ballCollision"), RequireField(mainType, "ballPassThrough") };
        MethodDefinition store = RequireMethod(mainType, "StoreReferenceFields", 2, mainType.Module.TypeSystem.Void);
        ILProcessor il = ResetBody(store);
        store.Body.InitLocals = true;
        var value = new VariableDefinition(referenceType);
        store.Body.Variables.Add(value);
        EmitGetCompactReference(il, referenceStateField, widthField, runtimeType, 0, 1);
        il.Append(il.Create(OpCodes.Stloc, value));
        for (int index = 0; index < fields.Length; index++)
        {
            il.Append(il.Create(OpCodes.Ldsfld, owners[index]));
            il.Append(il.Create(OpCodes.Ldloc, value));
            il.Append(il.Create(OpCodes.Stfld, fields[index].Field));
        }
        il.Append(il.Create(OpCodes.Ret));

        MethodDefinition read = RequireMethod(mainType, "ReadReferenceFields", 0, mainType.Module.TypeSystem.UInt16);
        il = ResetBody(read);
        for (int index = 0; index < fields.Length; index++)
        {
            il.Append(il.Create(OpCodes.Ldsfld, owners[index]));
            il.Append(il.Create(OpCodes.Ldfld, fields[index].Field));
            il.Append(il.Create(OpCodes.Call, RequireRuntimeMethod(runtimeType, "GetCompactTypeValue", 1)));
            if (index != 0)
                il.Append(il.Create(OpCodes.Add));
        }
        il.Append(il.Create(OpCodes.Conv_U2));
        il.Append(il.Create(OpCodes.Ret));

        FieldDefinition[] referenceFields = fields.Select(entry => entry.Field).ToArray();
        TileReferenceFieldLowerer.RewriteAfterConsumerLowering(tileType, referenceType, referenceFields, new[] { store, read });
        foreach ((TypeDefinition owner, FieldDefinition field) in fields)
            RewriteReferenceFieldOwnerConstructor(owner, field, referenceType);
    }

    private static void RewriteReferenceFieldOwnerConstructor(TypeDefinition owner, FieldDefinition field, TypeDefinition referenceType)
    {
        MethodDefinition constructor = owner.Methods.SingleOrDefault(method => method.IsConstructor && !method.IsStatic && method.Parameters.Count == 0)
            ?? throw new InvalidOperationException($"The fixture Tile reference-field owner {owner.FullName} has no default constructor.");
        for (Instruction? instruction = constructor.Body.Instructions.FirstOrDefault(); instruction is not null; instruction = instruction.Next)
        {
            if (instruction.OpCode != OpCodes.Stfld || !ReferenceEquals(instruction.Operand, field))
                continue;
            Instruction? previous = instruction.Previous;
            if (previous is null || previous.OpCode != OpCodes.Ldnull)
                throw new InvalidOperationException($"The fixture Tile reference-field owner {owner.FullName} has an unexpected field initializer.");
            previous.OpCode = OpCodes.Ldflda;
            previous.Operand = field;
            instruction.OpCode = OpCodes.Initobj;
            instruction.Operand = referenceType;
            return;
        }

        // A nullable-forgiving field declaration may have no emitted initializer.
    }

    private static void RewriteCompactReferenceStaticInitialization(TypeDefinition owner, FieldDefinition field, TypeDefinition referenceType)
    {
        MethodDefinition? constructor = owner.Methods.SingleOrDefault(method => method.IsConstructor && method.IsStatic && method.HasBody);
        if (constructor is null)
            return;
        ILProcessor il = constructor.Body.GetILProcessor();
        for (Instruction? instruction = constructor.Body.Instructions.FirstOrDefault(); instruction is not null; instruction = instruction.Next)
        {
            if (instruction.OpCode != OpCodes.Stsfld || !ReferenceEquals(instruction.Operand, field))
                continue;
            Instruction? previous = instruction.Previous;
            if (previous is null || previous.OpCode != OpCodes.Ldnull)
                throw new InvalidOperationException($"The compact Tile reference field {field.FullName} has an unexpected static initializer.");
            il.Remove(previous);
            instruction.OpCode = OpCodes.Ldsflda;
            instruction.Operand = field;
            il.InsertAfter(instruction, il.Create(OpCodes.Initobj, referenceType));
            return;
        }

        throw new InvalidOperationException($"The compact Tile reference field {field.FullName} has no explicit null initializer.");
    }

    private static void RewriteByReferenceParameter(MethodDefinition method, TypeDefinition tileType, TypeDefinition referenceType, int index)
    {
        TypeReference parameterType = method.Parameters[index].ParameterType;
        if (parameterType is not ByReferenceType byReference || byReference.ElementType.FullName != tileType.FullName)
            throw new InvalidOperationException($"The fixture by-reference Tile parameter {index} was not found in {method.FullName}.");
        method.Parameters[index].ParameterType = referenceType;
    }

    private static void RewriteOutParameter(MethodDefinition method, TypeDefinition tileType, TypeDefinition referenceType, int index)
    {
        TypeReference parameterType = method.Parameters[index].ParameterType;
        if (parameterType is not ByReferenceType byReference || byReference.ElementType.FullName != tileType.FullName || !method.Parameters[index].IsOut)
            throw new InvalidOperationException($"The fixture out Tile parameter {index} was not found in {method.FullName}.");
        method.Parameters[index].ParameterType = new ByReferenceType(referenceType);
    }

    private static void EmitGetCompactReference(ILProcessor il, FieldDefinition referenceStateField, FieldDefinition widthField, TypeDefinition runtimeType, int xArgument, int yArgument)
    {
        il.Append(il.Create(OpCodes.Ldsfld, referenceStateField));
        il.Append(il.Create(OpCodes.Ldsfld, widthField));
        EmitLoadArgument(il, xArgument);
        EmitLoadArgument(il, yArgument);
        il.Append(il.Create(OpCodes.Call, RequireRuntimeMethod(runtimeType, "GetCompactReference", 4)));
    }

    private static void EmitRequireMaterialized(ILProcessor il, FieldDefinition materializedField, FieldDefinition widthField, TypeDefinition runtimeType, int xArgument, int yArgument)
    {
        il.Append(il.Create(OpCodes.Ldsfld, materializedField));
        EmitCellIndex(il, widthField, xArgument, yArgument);
        il.Append(il.Create(OpCodes.Call, RequireRuntimeMethod(runtimeType, "RequireMaterialized", 2)));
    }

    private static MethodDefinition RequireRuntimeMethod(TypeDefinition runtimeType, string name, int parameterCount)
    {
        MethodDefinition method = runtimeType.Methods.SingleOrDefault(candidate =>
            candidate.IsStatic &&
            string.Equals(candidate.Name, name, StringComparison.Ordinal) &&
            candidate.Parameters.Count == parameterCount)
            ?? throw new InvalidOperationException($"The fixture compact runtime method {runtimeType.FullName}::{name} was not found.");
        return method;
    }

    private static void EmitCellAddress(ILProcessor il, FieldDefinition tileField, FieldDefinition widthField, TypeDefinition dataType)
    {
        il.Append(il.Create(OpCodes.Ldsfld, tileField));
        EmitCellIndex(il, widthField, 0, 1);
        il.Append(il.Create(OpCodes.Ldelema, dataType));
    }

    private static void EmitCellIndex(ILProcessor il, FieldDefinition widthField, int xArgument, int yArgument)
    {
        EmitLoadArgument(il, yArgument);
        il.Append(il.Create(OpCodes.Ldsfld, widthField));
        il.Append(il.Create(OpCodes.Mul));
        EmitLoadArgument(il, xArgument);
        il.Append(il.Create(OpCodes.Add));
    }

    private static void EmitCellAddressForArguments(ILProcessor il, FieldDefinition tileField, FieldDefinition widthField, TypeDefinition dataType, int xArgument, int yArgument)
    {
        il.Append(il.Create(OpCodes.Ldsfld, tileField));
        EmitLoadArgument(il, xArgument);
        EmitLoadArgument(il, yArgument);
        il.Append(il.Create(OpCodes.Ldsfld, widthField));
        il.Append(il.Create(OpCodes.Mul));
        il.Append(il.Create(OpCodes.Add));
        il.Append(il.Create(OpCodes.Ldelema, dataType));
    }

    private static void EmitLoadArgument(ILProcessor il, int index)
    {
        switch (index)
        {
            case 0: il.Append(il.Create(OpCodes.Ldarg_0)); break;
            case 1: il.Append(il.Create(OpCodes.Ldarg_1)); break;
            case 2: il.Append(il.Create(OpCodes.Ldarg_2)); break;
            case 3: il.Append(il.Create(OpCodes.Ldarg_3)); break;
            default: throw new ArgumentOutOfRangeException(nameof(index));
        }
    }

    private static ILProcessor ResetBody(MethodDefinition method)
    {
        method.Body = new MethodBody(method) { InitLocals = false, MaxStackSize = 4 };
        return method.Body.GetILProcessor();
    }

    private static void ValidateNoFixtureTileStorageReferences(TypeDefinition mainType, TypeDefinition tileType)
    {
        foreach (FieldDefinition field in mainType.Fields)
        {
            if (ContainsType(field.FieldType, tileType.FullName))
                throw new InvalidOperationException($"Lowered fixture field {field.FullName} still references {tileType.FullName}.");
        }

        foreach (MethodDefinition method in mainType.Methods.Where(method => method.HasBody))
        {
            if (ContainsType(method.ReturnType, tileType.FullName) ||
                method.Parameters.Any(parameter => ContainsType(parameter.ParameterType, tileType.FullName)) ||
                method.Body.Variables.Any(variable => ContainsType(variable.VariableType, tileType.FullName)))
            {
                throw new InvalidOperationException($"Lowered fixture method {method.FullName} still has a {tileType.FullName} signature or local.");
            }

            foreach (Instruction instruction in method.Body.Instructions)
            {
                if (instruction.Operand is MethodReference methodReference &&
                    methodReference.DeclaringType.FullName == tileType.FullName)
                {
                    throw new InvalidOperationException($"Lowered fixture method {method.FullName} still calls {methodReference.FullName}.");
                }
                if (instruction.Operand is FieldReference fieldReference &&
                    fieldReference.DeclaringType.FullName == tileType.FullName)
                {
                    throw new InvalidOperationException($"Lowered fixture method {method.FullName} still references {fieldReference.FullName}.");
                }
            }
        }
    }

    private static bool ContainsType(TypeReference type, string fullName)
    {
        if (type.FullName == fullName)
            return true;
        if (type is GenericInstanceType generic && generic.GenericArguments.Any(argument => ContainsType(argument, fullName)))
            return true;
        return type is TypeSpecification specification && ContainsType(specification.ElementType, fullName);
    }

    private static TypeDefinition RequireType(ModuleDefinition module, string fullName)
    {
        return module.GetType(fullName) ?? throw new InvalidOperationException($"The fixture type {fullName} was not found.");
    }

    private static FieldDefinition RequireField(TypeDefinition type, string name)
    {
        return type.Fields.SingleOrDefault(field => string.Equals(field.Name, name, StringComparison.Ordinal))
            ?? throw new InvalidOperationException($"The fixture field {type.FullName}::{name} was not found.");
    }

    private static void RequireTileField(TypeDefinition tileType, string name, TypeReference type)
    {
        FieldDefinition field = RequireField(tileType, name);
        if (field.FieldType.MetadataType != type.MetadataType)
            throw new InvalidOperationException($"The fixture field {tileType.FullName}::{name} has an unexpected type.");
    }

    private static MethodDefinition RequireMethod(TypeDefinition type, string name, int parameterCount, TypeReference returnType)
    {
        MethodDefinition method = type.Methods.SingleOrDefault(candidate =>
            string.Equals(candidate.Name, name, StringComparison.Ordinal) &&
            candidate.Parameters.Count == parameterCount &&
            candidate.ReturnType.MetadataType == returnType.MetadataType)
            ?? throw new InvalidOperationException($"The fixture method {type.FullName}::{name} has an unexpected signature.");
        if (!method.IsStatic)
            throw new InvalidOperationException($"The fixture method {method.FullName} must be static.");
        return method;
    }

    private static bool IsTwoDimensionalTileArray(TypeReference type, TypeDefinition tileType)
    {
        return type is ArrayType array && array.Rank == 2 && array.ElementType.FullName == tileType.FullName;
    }

    private static bool IsInt32(TypeReference type)
    {
        return type.MetadataType == MetadataType.Int32;
    }
}

public sealed record FixtureTileLoweringResult(string CompactDataType, int RewrittenMethods);
