using Mono.Cecil;
using Mono.Cecil.Cil;

internal static class TileStackFlowAnalyzer
{
    private const int MaximumStates = 4096;
    private const int MaximumTrackedStackDepth = 32;

    internal static TileStackFlowAudit Analyze(MethodDefinition method, Instruction getCall, string tileType, ISet<string> mutatingTileMethods)
    {
        return AnalyzeAfter(method, getCall, NextNonNop(getCall), tileType, mutatingTileMethods);
    }

    internal static TileStackFlowAudit AnalyzeFromLoad(MethodDefinition method, Instruction load, string tileType, ISet<string> mutatingTileMethods)
    {
        return AnalyzeAfter(method, load, NextNonNop(load), tileType, mutatingTileMethods);
    }

    private static TileStackFlowAudit AnalyzeAfter(
        MethodDefinition method,
        Instruction source,
        Instruction? first,
        string tileType,
        ISet<string> mutatingTileMethods)
    {
        var outcomes = new HashSet<string>(StringComparer.Ordinal);
        var work = new Queue<StackState>();
        var visited = new HashSet<string>(StringComparer.Ordinal);
        if (first is null)
            return CreateResult(method, source, outcomes, stateLimitReached: false);

        work.Enqueue(new StackState(first, new[] { StackValue.Tile }));
        while (work.Count != 0)
        {
            if (visited.Count >= MaximumStates)
            {
                outcomes.Add("StateLimitReached");
                break;
            }

            StackState state = work.Dequeue();
            string stateKey = state.Instruction.Offset.ToString("X4", System.Globalization.CultureInfo.InvariantCulture) + ":" + string.Join(',', state.Stack);
            if (!visited.Add(stateKey))
                continue;

            Execute(method, state, tileType, mutatingTileMethods, outcomes, work);
        }

        return CreateResult(method, source, outcomes, outcomes.Contains("StateLimitReached", StringComparer.Ordinal));
    }

    private static TileStackFlowAudit CreateResult(MethodDefinition method, Instruction getCall, ISet<string> outcomes, bool stateLimitReached)
    {
        if (outcomes.Count == 0)
            outcomes.Add("NoObservedUse");

        return new TileStackFlowAudit
        {
            Location = $"{method.DeclaringType.FullName}::{method.Name}@IL_{getCall.Offset:X4}",
            Outcomes = outcomes.OrderBy(value => value, StringComparer.Ordinal).ToArray(),
            StateLimitReached = stateLimitReached
        };
    }

    private static void Execute(
        MethodDefinition method,
        StackState state,
        string tileType,
        ISet<string> mutatingTileMethods,
        ISet<string> outcomes,
        Queue<StackState> work)
    {
        Instruction instruction = state.Instruction;
        List<StackValue> stack = new(state.Stack);
        Code code = instruction.OpCode.Code;

        if (code is Code.Br or Code.Br_S or Code.Leave or Code.Leave_S)
        {
            EnqueueBranch(work, (Instruction)instruction.Operand, stack, outcomes);
            return;
        }

        if (code == Code.Switch)
        {
            foreach (Instruction target in (Instruction[])instruction.Operand)
                EnqueueBranch(work, target, Pop(stack, 1, outcomes), outcomes);
            EnqueueBranch(work, NextNonNop(instruction), Pop(stack, 1, outcomes), outcomes);
            return;
        }

        if (IsConditionalBranch(code))
        {
            int popCount = code is Code.Brtrue or Code.Brtrue_S or Code.Brfalse or Code.Brfalse_S ? 1 : 2;
            List<StackValue> afterBranch = Pop(stack, popCount, outcomes, "NullOrIdentityBranch");
            EnqueueBranch(work, (Instruction)instruction.Operand, afterBranch, outcomes);
            EnqueueBranch(work, NextNonNop(instruction), afterBranch, outcomes);
            return;
        }

        switch (code)
        {
            case Code.Nop:
            case Code.Break:
            case Code.Volatile:
            case Code.Tail:
            case Code.Constrained:
            case Code.Unaligned:
            case Code.Readonly:
                EnqueueBranch(work, NextNonNop(instruction), stack, outcomes);
                return;

            case Code.Pop:
                EnqueueBranch(work, NextNonNop(instruction), Pop(stack, 1, outcomes, "DiscardedTileValue"), outcomes);
                return;

            case Code.Dup:
                if (stack.Count == 0)
                {
                    outcomes.Add("UnsupportedStackUnderflow:Dup");
                    return;
                }
                stack.Add(stack[^1]);
                EnqueueBranch(work, NextNonNop(instruction), stack, outcomes);
                return;

            case Code.Ret:
                Pop(stack, method.ReturnType.MetadataType == MetadataType.Void ? 0 : 1, outcomes, "ReturnEscape");
                return;

            case Code.Throw:
            case Code.Rethrow:
                Pop(stack, code == Code.Throw ? 1 : 0, outcomes, "ThrownTileValue");
                return;

            case Code.Stloc_0:
            case Code.Stloc_1:
            case Code.Stloc_2:
            case Code.Stloc_3:
            case Code.Stloc:
            case Code.Stloc_S:
                EnqueueBranch(work, NextNonNop(instruction), Pop(stack, 1, outcomes, "LocalAliasEscape"), outcomes);
                return;

            case Code.Starg:
            case Code.Starg_S:
                EnqueueBranch(work, NextNonNop(instruction), Pop(stack, 1, outcomes, "ArgumentEscape"), outcomes);
                return;

            case Code.Stsfld:
                EnqueueBranch(work, NextNonNop(instruction), Pop(stack, 1, outcomes, "StaticFieldEscape"), outcomes);
                return;

            case Code.Stfld:
                HandleFieldStore(instruction, stack, outcomes);
                EnqueueBranch(work, NextNonNop(instruction), stack, outcomes);
                return;

            case Code.Ldfld:
                HandleFieldLoad(instruction, stack, outcomes);
                EnqueueBranch(work, NextNonNop(instruction), stack, outcomes);
                return;

            case Code.Ldflda:
                HandleFieldLoad(instruction, stack, outcomes, "FieldAddressRead");
                EnqueueBranch(work, NextNonNop(instruction), stack, outcomes);
                return;

            case Code.Call:
            case Code.Callvirt:
                HandleCall((MethodReference)instruction.Operand, stack, tileType, mutatingTileMethods, outcomes);
                EnqueueBranch(work, NextNonNop(instruction), stack, outcomes);
                return;

            case Code.Newobj:
                HandleNewObject((MethodReference)instruction.Operand, stack, tileType, outcomes);
                EnqueueBranch(work, NextNonNop(instruction), stack, outcomes);
                return;

            case Code.Ceq:
            case Code.Cgt_Un:
            case Code.Clt_Un:
                EnqueueBranch(work, NextNonNop(instruction), PopAndPush(stack, 2, outcomes, "NullOrIdentityComparison"), outcomes);
                return;

            case Code.Ldind_Ref:
            case Code.Ldind_I1:
            case Code.Ldind_U1:
            case Code.Ldind_I2:
            case Code.Ldind_U2:
            case Code.Ldind_I4:
            case Code.Ldind_U4:
            case Code.Ldind_I8:
            case Code.Ldind_I:
            case Code.Ldind_R4:
            case Code.Ldind_R8:
                EnqueueBranch(work, NextNonNop(instruction), PopAndPush(stack, 1, outcomes, "IndirectRead"), outcomes);
                return;

            case Code.Ldlen:
                EnqueueBranch(work, NextNonNop(instruction), PopAndPush(stack, 1, outcomes, "UnexpectedTileArrayOperation"), outcomes);
                return;

            case Code.Ldelem_I1:
            case Code.Ldelem_U1:
            case Code.Ldelem_I2:
            case Code.Ldelem_U2:
            case Code.Ldelem_I4:
            case Code.Ldelem_U4:
            case Code.Ldelem_I8:
            case Code.Ldelem_I:
            case Code.Ldelem_R4:
            case Code.Ldelem_R8:
            case Code.Ldelem_Ref:
            case Code.Ldelem_Any:
                EnqueueBranch(work, NextNonNop(instruction), PopAndPush(stack, 2, outcomes, "UnexpectedTileArrayOperation"), outcomes);
                return;

            case Code.Ldelema:
                EnqueueBranch(work, NextNonNop(instruction), PopAndPush(stack, 2, outcomes, "UnexpectedTileArrayAddress"), outcomes);
                return;

            case Code.Newarr:
                EnqueueBranch(work, NextNonNop(instruction), PopAndPush(stack, 1, outcomes, "UnexpectedTileArrayOperation"), outcomes);
                return;

            case Code.Initobj:
                EnqueueBranch(work, NextNonNop(instruction), Pop(stack, 1, outcomes, "UnexpectedTileInitialization"), outcomes);
                return;

            case Code.Stind_Ref:
            case Code.Stind_I1:
            case Code.Stind_I2:
            case Code.Stind_I4:
            case Code.Stind_I8:
            case Code.Stind_R4:
            case Code.Stind_R8:
            case Code.Stind_I:
                EnqueueBranch(work, NextNonNop(instruction), Pop(stack, 2, outcomes, "IndirectEscape"), outcomes);
                return;

            case Code.Stelem_I:
            case Code.Stelem_I1:
            case Code.Stelem_I2:
            case Code.Stelem_I4:
            case Code.Stelem_I8:
            case Code.Stelem_R4:
            case Code.Stelem_R8:
            case Code.Stelem_Ref:
            case Code.Stelem_Any:
                EnqueueBranch(work, NextNonNop(instruction), Pop(stack, 3, outcomes, "ArrayElementEscape"), outcomes);
                return;

            case Code.Stobj:
                EnqueueBranch(work, NextNonNop(instruction), Pop(stack, 2, outcomes, "IndirectEscape"), outcomes);
                return;

            case Code.Box:
                EnqueueBranch(work, NextNonNop(instruction), PopAndPush(stack, 1, outcomes, "BoxedTileValue"), outcomes);
                return;
        }

        if (IsSimplePush(code))
        {
            stack.Add(StackValue.Other);
            EnqueueBranch(work, NextNonNop(instruction), stack, outcomes);
            return;
        }

        if (IsBinaryOperator(code))
        {
            EnqueueBranch(work, NextNonNop(instruction), PopAndPush(stack, 2, outcomes, "UnexpectedTileArithmetic"), outcomes);
            return;
        }

        if (IsUnaryOperator(code))
        {
            EnqueueBranch(work, NextNonNop(instruction), PopAndPush(stack, 1, outcomes, "UnexpectedTileUnaryOperation"), outcomes);
            return;
        }

        if (stack.Contains(StackValue.Tile))
            outcomes.Add("UnsupportedInstruction:" + code);
    }

    private static void HandleFieldLoad(Instruction instruction, List<StackValue> stack, ISet<string> outcomes, string outcome = "FieldRead")
    {
        StackValue objectValue = PopTop(stack, outcomes, outcome);
        if (objectValue == StackValue.Tile)
            outcomes.Add(outcome);
        stack.Add(StackValue.Other);
    }

    private static void HandleFieldStore(Instruction instruction, List<StackValue> stack, ISet<string> outcomes)
    {
        StackValue value = PopTop(stack, outcomes, "FieldEscape");
        StackValue target = PopTop(stack, outcomes, "FieldWrite");
        if (target == StackValue.Tile)
            outcomes.Add("FieldWrite");
        if (value == StackValue.Tile)
            outcomes.Add("FieldEscape");
    }

    private static void HandleCall(MethodReference called, List<StackValue> stack, string tileType, ISet<string> mutatingTileMethods, ISet<string> outcomes)
    {
        int count = called.Parameters.Count + (called.HasThis ? 1 : 0);
        StackValue[] inputs = PopInputs(stack, count, outcomes, "UnexpectedTileCall");
        for (int index = 0; index < inputs.Length; index++)
        {
            if (inputs[index] != StackValue.Tile)
                continue;

            if (called.DeclaringType.FullName == tileType && called.HasThis && index == 0)
            {
                outcomes.Add(mutatingTileMethods.Contains(GetMethodKey(called)) ? "TileMethodMutation" : "TileMethodRead");
                continue;
            }

            int parameterIndex = index - (called.HasThis ? 1 : 0);
            if (parameterIndex >= 0 && parameterIndex < called.Parameters.Count && IsTileType(called.Parameters[parameterIndex].ParameterType, tileType))
            {
                outcomes.Add("TileParameterEscape");
                continue;
            }

            outcomes.Add("UnexpectedTileCall:" + called.FullName);
        }

        if (called.ReturnType.MetadataType != MetadataType.Void)
            stack.Add(StackValue.Other);
    }

    private static void HandleNewObject(MethodReference constructor, List<StackValue> stack, string tileType, ISet<string> outcomes)
    {
        StackValue[] inputs = PopInputs(stack, constructor.Parameters.Count, outcomes, "UnexpectedTileConstruction");
        if (inputs.Any(value => value == StackValue.Tile))
            outcomes.Add(constructor.DeclaringType.FullName == tileType ? "TileSnapshotConstruction" : "UnexpectedTileConstruction");
        stack.Add(StackValue.Other);
    }

    private static List<StackValue> PopAndPush(List<StackValue> stack, int count, ISet<string> outcomes, string outcome)
    {
        Pop(stack, count, outcomes, outcome);
        stack.Add(StackValue.Other);
        return stack;
    }

    private static List<StackValue> Pop(List<StackValue> stack, int count, ISet<string> outcomes, string? tileOutcome = null)
    {
        for (int index = 0; index < count; index++)
        {
            StackValue value = PopTop(stack, outcomes, tileOutcome);
            if (value == StackValue.Tile && tileOutcome is not null)
                outcomes.Add(tileOutcome);
        }

        return stack;
    }

    private static StackValue[] PopInputs(List<StackValue> stack, int count, ISet<string> outcomes, string underflowOutcome)
    {
        StackValue[] inputs = new StackValue[count];
        for (int index = count - 1; index >= 0; index--)
            inputs[index] = PopTop(stack, outcomes, tileOutcome: null);
        return inputs;
    }

    private static StackValue PopTop(List<StackValue> stack, ISet<string> outcomes, string? tileOutcome)
    {
        if (stack.Count == 0)
            return StackValue.Other;

        int index = stack.Count - 1;
        StackValue value = stack[index];
        stack.RemoveAt(index);
        if (value == StackValue.Tile && tileOutcome is not null)
            outcomes.Add(tileOutcome);
        return value;
    }

    private static void EnqueueBranch(Queue<StackState> work, Instruction? target, List<StackValue> stack, ISet<string> outcomes)
    {
        if (target is null)
            return;
        if (stack.Count > MaximumTrackedStackDepth)
        {
            outcomes.Add("StackDepthLimitReached");
            return;
        }

        work.Enqueue(new StackState(target, stack.ToArray()));
    }

    private static Instruction? NextNonNop(Instruction instruction)
    {
        for (Instruction? cursor = instruction.Next; cursor is not null; cursor = cursor.Next)
        {
            if (cursor.OpCode != OpCodes.Nop)
                return cursor;
        }

        return null;
    }

    private static bool IsTileType(TypeReference type, string tileType)
    {
        return type.FullName == tileType || type is ByReferenceType byReference && byReference.ElementType.FullName == tileType;
    }

    private static string GetMethodKey(MethodReference method)
    {
        return method.Name + "/" + method.Parameters.Count;
    }

    private static bool IsConditionalBranch(Code code)
    {
        return code is Code.Brfalse or Code.Brfalse_S or Code.Brtrue or Code.Brtrue_S or
            Code.Beq or Code.Beq_S or Code.Bge or Code.Bge_S or Code.Bge_Un or Code.Bge_Un_S or
            Code.Bgt or Code.Bgt_S or Code.Bgt_Un or Code.Bgt_Un_S or Code.Ble or Code.Ble_S or
            Code.Ble_Un or Code.Ble_Un_S or Code.Blt or Code.Blt_S or Code.Blt_Un or Code.Blt_Un_S or
            Code.Bne_Un or Code.Bne_Un_S;
    }

    private static bool IsSimplePush(Code code)
    {
        return code is Code.Ldarg_0 or Code.Ldarg_1 or Code.Ldarg_2 or Code.Ldarg_3 or Code.Ldarg or Code.Ldarg_S or
            Code.Ldarga or Code.Ldarga_S or Code.Ldloc_0 or Code.Ldloc_1 or Code.Ldloc_2 or Code.Ldloc_3 or Code.Ldloc or Code.Ldloc_S or
            Code.Ldloca or Code.Ldloca_S or Code.Ldc_I4_M1 or Code.Ldc_I4_0 or Code.Ldc_I4_1 or Code.Ldc_I4_2 or Code.Ldc_I4_3 or
            Code.Ldc_I4_4 or Code.Ldc_I4_5 or Code.Ldc_I4_6 or Code.Ldc_I4_7 or Code.Ldc_I4_8 or Code.Ldc_I4 or Code.Ldc_I4_S or
            Code.Ldc_I8 or Code.Ldc_R4 or Code.Ldc_R8 or Code.Ldnull or Code.Ldstr or Code.Ldsfld or Code.Ldftn or Code.Ldvirtftn or
            Code.Ldtoken or Code.Arglist or Code.Sizeof;
    }

    private static bool IsBinaryOperator(Code code)
    {
        return code is Code.Add or Code.Add_Ovf or Code.Add_Ovf_Un or Code.Sub or Code.Sub_Ovf or Code.Sub_Ovf_Un or Code.Mul or
            Code.Mul_Ovf or Code.Mul_Ovf_Un or Code.Div or Code.Div_Un or Code.Rem or Code.Rem_Un or Code.And or Code.Or or Code.Xor or
            Code.Shl or Code.Shr or Code.Shr_Un or Code.Cgt or Code.Clt or Code.Ceq;
    }

    private static bool IsUnaryOperator(Code code)
    {
        return code is Code.Neg or Code.Not or Code.Conv_I or Code.Conv_I1 or Code.Conv_I2 or Code.Conv_I4 or Code.Conv_I8 or
            Code.Conv_R4 or Code.Conv_R8 or Code.Conv_U4 or Code.Conv_U8 or Code.Conv_U2 or Code.Conv_U1 or Code.Conv_U or
            Code.Conv_Ovf_I or Code.Conv_Ovf_I_Un or Code.Conv_Ovf_I1 or Code.Conv_Ovf_I1_Un or Code.Conv_Ovf_I2 or Code.Conv_Ovf_I2_Un or
            Code.Conv_Ovf_I4 or Code.Conv_Ovf_I4_Un or Code.Conv_Ovf_I8 or Code.Conv_Ovf_I8_Un or Code.Conv_Ovf_U or Code.Conv_Ovf_U_Un or
            Code.Conv_Ovf_U1 or Code.Conv_Ovf_U1_Un or Code.Conv_Ovf_U2 or Code.Conv_Ovf_U2_Un or Code.Conv_Ovf_U4 or Code.Conv_Ovf_U4_Un or
            Code.Conv_Ovf_U8 or Code.Conv_Ovf_U8_Un or Code.Conv_R_Un;
    }

    private enum StackValue
    {
        Other,
        Tile
    }

    private sealed class StackState
    {
        internal StackState(Instruction instruction, IReadOnlyList<StackValue> stack)
        {
            Instruction = instruction;
            Stack = stack;
        }

        internal Instruction Instruction { get; }
        internal IReadOnlyList<StackValue> Stack { get; }
    }
}

internal sealed class TileStackFlowAudit
{
    public string Location { get; init; } = string.Empty;
    public IReadOnlyList<string> Outcomes { get; init; } = Array.Empty<string>();
    public bool StateLimitReached { get; init; }
}
