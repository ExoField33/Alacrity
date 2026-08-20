using System;
using System.Collections.Concurrent;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.ExceptionServices;
using System.Threading;

namespace AlacrityTerraria.Rendering.Optimization;

/// <summary>
/// Executes the independent ranges used by Terraria's LightMap blur and TileLightScanner export
/// paths. The caller always processes one balanced range, so it never waits idle while workers
/// run. Work groups and worker state are retained for reuse after their first use.
/// </summary>
internal static class TerrariaLightingParallelExecutor
{
    private static readonly ConcurrentBag<WorkGroup> idleGroups = new ConcurrentBag<WorkGroup>();
    private static readonly WaitCallback workerCallback = ExecuteWorker;
    // Match Terraria's FastParallel degree: reserve one logical processor while the caller
    // performs one range. The gain comes from removing its transient tasks/events and spin wait,
    // not from oversubscribing an extra worker.
    private static readonly int maximumWorkerCount = Math.Max(1, Environment.ProcessorCount - 1);

    /// <summary>
    /// Runs <paramref name="callback"/> once for every value in the half-open range. The callback
    /// receives non-overlapping contiguous ranges with the same bounds convention as FastParallel.
    /// </summary>
    internal static void For(int fromInclusive, int toExclusive, Delegate callback, object context)
    {
        if (callback == null)
        {
            throw new ArgumentNullException(nameof(callback));
        }

        int rangeLength = toExclusive - fromInclusive;
        if (rangeLength <= 0)
        {
            return;
        }

        int workerCount = Math.Min(maximumWorkerCount, rangeLength);
        if (workerCount == 1)
        {
            TerrariaLightingRangeInvoker.Invoke(callback, fromInclusive, toExclusive, context);
            return;
        }

        int queuedWorkerCount = workerCount - 1;
        WorkGroup group = Rent(queuedWorkerCount);
        group.Prepare(
            TerrariaLightingRangeInvoker.Get(callback),
            callback,
            context,
            fromInclusive,
            toExclusive,
            workerCount,
            queuedWorkerCount);

        for (int workerIndex = 0; workerIndex < queuedWorkerCount; workerIndex++)
        {
            RangeWorker worker = group.GetWorker(workerIndex);
            if (!ThreadPool.UnsafeQueueUserWorkItem(workerCallback, worker))
            {
                worker.Execute();
            }
        }

        Exception callerFailure = null;
        try
        {
            int callerEnd = GetRangeEnd(fromInclusive, rangeLength, workerCount, 0);
            group.InvokeCaller(fromInclusive, callerEnd);
        }
        catch (Exception exception)
        {
            callerFailure = exception;
        }
        finally
        {
            group.WaitForWorkers();
        }

        Exception workerFailure = group.TakeFailure();
        Return(group);

        if (callerFailure != null)
        {
            ExceptionDispatchInfo.Capture(callerFailure).Throw();
        }

        if (workerFailure != null)
        {
            ExceptionDispatchInfo.Capture(workerFailure).Throw();
        }
    }

    private static WorkGroup Rent(int queuedWorkerCount)
    {
        if (!idleGroups.TryTake(out WorkGroup group))
        {
            group = new WorkGroup();
        }

        group.EnsureCapacity(queuedWorkerCount);
        return group;
    }

    private static void Return(WorkGroup group)
    {
        group.Reset();
        idleGroups.Add(group);
    }

    private static void ExecuteWorker(object state)
    {
        ((RangeWorker)state).Execute();
    }

    private static int GetRangeStart(int fromInclusive, int rangeLength, int workerCount, int rangeIndex)
    {
        int baseLength = rangeLength / workerCount;
        int remainder = rangeLength % workerCount;
        return fromInclusive + (baseLength * rangeIndex) + Math.Min(rangeIndex, remainder);
    }

    private static int GetRangeEnd(int fromInclusive, int rangeLength, int workerCount, int rangeIndex)
    {
        int start = GetRangeStart(fromInclusive, rangeLength, workerCount, rangeIndex);
        int baseLength = rangeLength / workerCount;
        int remainder = rangeLength % workerCount;
        return start + baseLength + (rangeIndex < remainder ? 1 : 0);
    }

    private sealed class WorkGroup
    {
        private readonly ManualResetEventSlim completed = new ManualResetEventSlim(false);
        private RangeWorker[] workers = Array.Empty<RangeWorker>();
        private LightingRangeInvoker invoker;
        private Delegate callback;
        private object context;
        private int pendingWorkers;
        private Exception failure;

        internal void EnsureCapacity(int requiredCapacity)
        {
            if (workers.Length >= requiredCapacity)
            {
                return;
            }

            var expanded = new RangeWorker[requiredCapacity];
            for (int workerIndex = 0; workerIndex < expanded.Length; workerIndex++)
            {
                expanded[workerIndex] = new RangeWorker(this);
            }

            workers = expanded;
        }

        internal void Prepare(
            LightingRangeInvoker rangeInvoker,
            Delegate nativeCallback,
            object rangeContext,
            int fromInclusive,
            int toExclusive,
            int workerCount,
            int queuedWorkerCount)
        {
            invoker = rangeInvoker;
            callback = nativeCallback;
            context = rangeContext;
            failure = null;
            pendingWorkers = queuedWorkerCount;
            completed.Reset();

            int rangeLength = toExclusive - fromInclusive;
            for (int workerIndex = 0; workerIndex < queuedWorkerCount; workerIndex++)
            {
                int nativeRangeIndex = workerCount - workerIndex - 1;
                int start = GetRangeStart(fromInclusive, rangeLength, workerCount, nativeRangeIndex);
                int end = GetRangeEnd(fromInclusive, rangeLength, workerCount, nativeRangeIndex);
                workers[workerIndex].SetRange(start, end);
            }
        }

        internal RangeWorker GetWorker(int workerIndex)
        {
            return workers[workerIndex];
        }

        internal void Execute(int fromInclusive, int toExclusive)
        {
            try
            {
                invoker(callback, fromInclusive, toExclusive, context);
            }
            catch (Exception exception)
            {
                Interlocked.CompareExchange(ref failure, exception, null);
            }
            finally
            {
                if (Interlocked.Decrement(ref pendingWorkers) == 0)
                {
                    completed.Set();
                }
            }
        }

        internal void WaitForWorkers()
        {
            completed.Wait();
        }

        internal Exception TakeFailure()
        {
            return failure;
        }

        internal void Reset()
        {
            invoker = null;
            callback = null;
            context = null;
            failure = null;
        }

        internal void InvokeCaller(int fromInclusive, int toExclusive)
        {
            invoker(callback, fromInclusive, toExclusive, context);
        }
    }

    private sealed class RangeWorker
    {
        private readonly WorkGroup owner;
        private int fromInclusive;
        private int toExclusive;

        internal RangeWorker(WorkGroup owner)
        {
            this.owner = owner;
        }

        internal void SetRange(int start, int end)
        {
            fromInclusive = start;
            toExclusive = end;
        }

        internal void Execute()
        {
            owner.Execute(fromInclusive, toExclusive);
        }
    }
}

internal delegate void LightingRangeInvoker(Delegate callback, int fromInclusive, int toExclusive, object context);

/// <summary>
/// Terraria's ParallelForAction lives in an implementation assembly that is intentionally not a
/// build reference. Create one exact typed call site for its runtime type, then reuse it.
/// </summary>
internal static class TerrariaLightingRangeInvoker
{
    private static readonly ConcurrentDictionary<Type, LightingRangeInvoker> invokers =
        new ConcurrentDictionary<Type, LightingRangeInvoker>();

    internal static LightingRangeInvoker Get(Delegate callback)
    {
        Type callbackType = callback.GetType();
        if (invokers.TryGetValue(callbackType, out LightingRangeInvoker invoker))
        {
            return invoker;
        }

        LightingRangeInvoker created = Create(callbackType);
        return invokers.GetOrAdd(callbackType, created);
    }

    internal static void Invoke(Delegate callback, int fromInclusive, int toExclusive, object context)
    {
        Get(callback)(callback, fromInclusive, toExclusive, context);
    }

    private static LightingRangeInvoker Create(Type callbackType)
    {
        MethodInfo invoke = callbackType.GetMethod("Invoke", BindingFlags.Instance | BindingFlags.Public);
        if (invoke == null || invoke.ReturnType != typeof(void) || invoke.GetParameters().Length != 3)
        {
            throw new InvalidOperationException("Terraria lighting callback does not match the verified range delegate shape.");
        }

        var method = new DynamicMethod(
            "AlacrityInvokeLightingRange",
            typeof(void),
            new[] { typeof(Delegate), typeof(int), typeof(int), typeof(object) },
            typeof(TerrariaLightingRangeInvoker).Module,
            true);
        ILGenerator il = method.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, callbackType);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Ldarg_3);
        il.EmitCall(OpCodes.Callvirt, invoke, null);
        il.Emit(OpCodes.Ret);
        return (LightingRangeInvoker)method.CreateDelegate(typeof(LightingRangeInvoker));
    }
}
