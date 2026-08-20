using System;
using System.Threading;
using AlacrityTerraria.Rendering.Optimization;
using Xunit;

public sealed class LightingParallelExecutorTests
{
    [Fact]
    public void For_CoversEachValueExactlyOnceAcrossBalancedRanges()
    {
        var visits = new int[257];
        RangeCallback callback = (start, end, state) =>
        {
            var values = (int[])state;
            for (int index = start; index < end; index++)
            {
                Interlocked.Increment(ref values[index]);
            }
        };

        TerrariaLightingParallelExecutor.For(0, visits.Length, callback, visits);

        for (int index = 0; index < visits.Length; index++)
        {
            Assert.Equal(1, Volatile.Read(ref visits[index]));
        }
    }

    [Fact]
    public void For_PreservesHalfOpenOffsets()
    {
        var visits = new int[64];
        RangeCallback callback = (start, end, state) =>
        {
            var values = (int[])state;
            for (int index = start; index < end; index++)
            {
                Interlocked.Increment(ref values[index]);
            }
        };

        TerrariaLightingParallelExecutor.For(11, 43, callback, visits);

        for (int index = 0; index < visits.Length; index++)
        {
            int expected = index >= 11 && index < 43 ? 1 : 0;
            Assert.Equal(expected, Volatile.Read(ref visits[index]));
        }
    }

    [Fact]
    public void For_ObservesCallbackExceptionsAfterAllWorkersFinish()
    {
        RangeCallback callback = (_, __, ___) =>
        {
            throw new InvalidOperationException("range failure");
        };

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => TerrariaLightingParallelExecutor.For(0, Math.Max(2, Environment.ProcessorCount * 2), callback, null));

        Assert.Equal("range failure", exception.Message);
    }

    [Fact]
    public void For_EmptyRangeDoesNotInvokeCallback()
    {
        int calls = 0;
        RangeCallback callback = (_, __, ___) => Interlocked.Increment(ref calls);

        TerrariaLightingParallelExecutor.For(5, 5, callback, null);

        Assert.Equal(0, Volatile.Read(ref calls));
    }

    private delegate void RangeCallback(int start, int end, object state);
}
