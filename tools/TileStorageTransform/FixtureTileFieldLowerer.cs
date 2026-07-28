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
        FieldDefinition originalTileField = RequireField(mainType, "tile");
        FieldDefinition widthField = RequireField(mainType, "width");
        RequireTileField(tileType, "type", module.TypeSystem.UInt16);
        RequireTileField(tileType, "frameX", module.TypeSystem.Int16);
        if (!IsTwoDimensionalTileArray(originalTileField.FieldType, tileType))
            throw new InvalidOperationException("The fixture tile field is not the expected Tile[,] storage shape.");
        if (!IsInt32(widthField.FieldType))
            throw new InvalidOperationException("The fixture width field is not an Int32.");

        TypeDefinition dataType = RequireType(module, FixtureNamespace + "." + DataTypeName);
        FieldDefinition dataTypeField = RequireField(dataType, "Type");
        FieldDefinition dataFrameXField = RequireField(dataType, "FrameX");
        string originalTileFieldName = originalTileField.Name;
        string originalTileFieldDeclaringType = originalTileField.DeclaringType.FullName;
        string originalTileFieldType = originalTileField.FieldType.FullName;
        FieldDefinition tileField = ReplaceTileField(mainType, originalTileField, dataType);
        NormalizeFieldReferences(module, originalTileFieldName, originalTileFieldDeclaringType, originalTileFieldType, tileField);
        FieldDefinition materializedField = AddMaterializationField(mainType, module);

        RewriteInitialize(mainType, tileField, materializedField, widthField, dataType);
        RewriteRead(mainType, "ReadType", tileField, widthField, dataType, dataTypeField);
        RewriteWrite(mainType, "WriteType", tileField, materializedField, widthField, dataType, dataTypeField);
        RewriteRead(mainType, "ReadFrameX", tileField, widthField, dataType, dataFrameXField);
        RewriteWrite(mainType, "WriteFrameX", tileField, materializedField, widthField, dataType, dataFrameXField);
        RewriteIsMissing(mainType, materializedField, widthField);
        RewriteClear(mainType, tileField, materializedField, widthField, dataType);
        RewriteEnsureAndWrite(mainType, tileField, materializedField, widthField, dataType, dataTypeField);
        RewriteCopy(mainType, tileField, materializedField, widthField, dataType, runtimeType);
        RewriteReadActive(mainType, tileField, widthField, dataType, dataTypeField);

        ValidateNoFixtureTileStorageReferences(mainType, tileType);
        string? outputDirectory = Path.GetDirectoryName(Path.GetFullPath(outputAssemblyPath));
        if (string.IsNullOrEmpty(outputDirectory))
            throw new InvalidOperationException("The transformed fixture output must have a parent directory.");
        Directory.CreateDirectory(outputDirectory);
        assembly.Write(outputAssemblyPath);
        return new FixtureTileLoweringResult(dataType.FullName, 10);
    }

    private static FieldDefinition AddMaterializationField(TypeDefinition mainType, ModuleDefinition module)
    {
        if (mainType.Fields.Any(field => string.Equals(field.Name, "__alacrityMaterialized", StringComparison.Ordinal)))
            throw new InvalidOperationException("The fixture has already been lowered.");

        var field = new FieldDefinition("__alacrityMaterialized", FieldAttributes.Private | FieldAttributes.Static, new ArrayType(module.TypeSystem.Boolean));
        mainType.Fields.Add(field);
        return field;
    }

    private static FieldDefinition ReplaceTileField(TypeDefinition mainType, FieldDefinition original, TypeDefinition dataType)
    {
        int index = mainType.Fields.IndexOf(original);
        if (index < 0)
            throw new InvalidOperationException("The fixture tile field is not owned by the expected type.");

        var replacement = new FieldDefinition(original.Name, original.Attributes, new ArrayType(dataType));
        mainType.Fields.RemoveAt(index);
        mainType.Fields.Insert(index, replacement);
        return replacement;
    }

    private static void NormalizeFieldReferences(ModuleDefinition module, string originalName, string originalDeclaringType, string originalFieldType, FieldDefinition replacement)
    {
        foreach (TypeDefinition type in EnumerateTypes(module.Types))
        {
            foreach (MethodDefinition method in type.Methods.Where(candidate => candidate.HasBody))
            {
                foreach (Instruction instruction in method.Body.Instructions)
                {
                    if (instruction.Operand is FieldReference reference &&
                        reference.Name == originalName &&
                        reference.DeclaringType?.FullName == originalDeclaringType &&
                        reference.FieldType.FullName == originalFieldType)
                    {
                        instruction.Operand = replacement;
                    }
                }
            }
        }
    }

    private static IEnumerable<TypeDefinition> EnumerateTypes(IEnumerable<TypeDefinition> types)
    {
        foreach (TypeDefinition type in types)
        {
            yield return type;
            foreach (TypeDefinition nested in EnumerateTypes(type.NestedTypes))
                yield return nested;
        }
    }

    private static void RewriteInitialize(TypeDefinition mainType, FieldDefinition tileField, FieldDefinition materializedField, FieldDefinition widthField, TypeDefinition dataType)
    {
        MethodDefinition method = RequireMethod(mainType, "Initialize", 2, mainType.Module.TypeSystem.Void);
        ILProcessor il = ResetBody(method);
        il.Append(il.Create(OpCodes.Ldarg_0));
        il.Append(il.Create(OpCodes.Stsfld, widthField));
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
        il.Append(il.Create(OpCodes.Ret));
    }

    private static void RewriteRead(TypeDefinition mainType, string name, FieldDefinition tileField, FieldDefinition widthField, TypeDefinition dataType, FieldDefinition dataField)
    {
        MethodDefinition method = RequireMethod(mainType, name, 2, dataField.FieldType);
        ILProcessor il = ResetBody(method);
        EmitCellAddress(il, tileField, widthField, dataType);
        il.Append(il.Create(OpCodes.Ldfld, dataField));
        il.Append(il.Create(OpCodes.Ret));
    }

    private static void RewriteWrite(TypeDefinition mainType, string name, FieldDefinition tileField, FieldDefinition materializedField, FieldDefinition widthField, TypeDefinition dataType, FieldDefinition dataField)
    {
        MethodDefinition method = RequireMethod(mainType, name, 3, mainType.Module.TypeSystem.Void);
        ILProcessor il = ResetBody(method);
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

    private static void RewriteClear(TypeDefinition mainType, FieldDefinition tileField, FieldDefinition materializedField, FieldDefinition widthField, TypeDefinition dataType)
    {
        MethodDefinition method = RequireMethod(mainType, "Clear", 2, mainType.Module.TypeSystem.Void);
        ILProcessor il = ResetBody(method);
        EmitCellAddress(il, tileField, widthField, dataType);
        il.Append(il.Create(OpCodes.Initobj, dataType));
        il.Append(il.Create(OpCodes.Ldsfld, materializedField));
        il.Append(il.Create(OpCodes.Ldarg_0));
        il.Append(il.Create(OpCodes.Ldarg_1));
        il.Append(il.Create(OpCodes.Ldsfld, widthField));
        il.Append(il.Create(OpCodes.Mul));
        il.Append(il.Create(OpCodes.Add));
        il.Append(il.Create(OpCodes.Ldc_I4_0));
        il.Append(il.Create(OpCodes.Stelem_I1));
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

    private static void RewriteCopy(TypeDefinition mainType, FieldDefinition tileField, FieldDefinition materializedField, FieldDefinition widthField, TypeDefinition dataType, TypeDefinition runtimeType)
    {
        MethodDefinition method = RequireMethod(mainType, "CopyCell", 4, mainType.Module.TypeSystem.Void);
        MethodDefinition copy = runtimeType.Methods.SingleOrDefault(candidate =>
            candidate.Name == "CopyCompactCell" &&
            candidate.IsStatic &&
            candidate.ReturnType.MetadataType == MetadataType.Void &&
            candidate.Parameters.Count == 7)
            ?? throw new InvalidOperationException("The fixture compact runtime copy helper was not found.");
        if (copy.Parameters[0].ParameterType.FullName != tileField.FieldType.FullName ||
            copy.Parameters[1].ParameterType.FullName != materializedField.FieldType.FullName ||
            copy.Parameters.Skip(2).Any(parameter => parameter.ParameterType.MetadataType != MetadataType.Int32))
        {
            throw new InvalidOperationException("The fixture compact runtime copy helper has an incompatible signature.");
        }

        ILProcessor il = ResetBody(method);
        il.Append(il.Create(OpCodes.Ldsfld, tileField));
        il.Append(il.Create(OpCodes.Ldsfld, materializedField));
        il.Append(il.Create(OpCodes.Ldsfld, widthField));
        il.Append(il.Create(OpCodes.Ldarg_0));
        il.Append(il.Create(OpCodes.Ldarg_1));
        il.Append(il.Create(OpCodes.Ldarg_2));
        il.Append(il.Create(OpCodes.Ldarg_3));
        il.Append(il.Create(OpCodes.Call, copy));
        il.Append(il.Create(OpCodes.Ret));
    }

    private static void RewriteReadActive(TypeDefinition mainType, FieldDefinition tileField, FieldDefinition widthField, TypeDefinition dataType, FieldDefinition dataTypeField)
    {
        MethodDefinition method = RequireMethod(mainType, "ReadActive", 2, mainType.Module.TypeSystem.Boolean);
        ILProcessor il = ResetBody(method);
        EmitCellAddress(il, tileField, widthField, dataType);
        il.Append(il.Create(OpCodes.Ldfld, dataTypeField));
        il.Append(il.Create(OpCodes.Ldc_I4_0));
        il.Append(il.Create(OpCodes.Cgt_Un));
        il.Append(il.Create(OpCodes.Ret));
    }

    private static void EmitCellAddress(ILProcessor il, FieldDefinition tileField, FieldDefinition widthField, TypeDefinition dataType)
    {
        il.Append(il.Create(OpCodes.Ldsfld, tileField));
        il.Append(il.Create(OpCodes.Ldarg_1));
        il.Append(il.Create(OpCodes.Ldsfld, widthField));
        il.Append(il.Create(OpCodes.Mul));
        il.Append(il.Create(OpCodes.Ldarg_0));
        il.Append(il.Create(OpCodes.Add));
        il.Append(il.Create(OpCodes.Ldelema, dataType));
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
        foreach (MethodDefinition method in mainType.Methods.Where(method => method.HasBody))
        {
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
