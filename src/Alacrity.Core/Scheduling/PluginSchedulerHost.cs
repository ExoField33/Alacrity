using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Alacrity.PluginSdk;

namespace Alacrity.Core;

/// <summary>
/// Host-owned timing and bounded background-work layer. Scheduled callbacks use the existing
/// dispatcher; background work is cancellation-owned by the same activation scope and never grants
/// access to live Terraria state.
/// </summary>
public sealed class PluginSchedulerHost
{
    private readonly object gate = new object();
    private readonly List<ScheduledWork> scheduled = new List<ScheduledWork>();
    private readonly List<BackgroundRegistration> background = new List<BackgroundRegistration>();
    private readonly Dictionary<PluginId, int> backgroundCounts = new Dictionary<PluginId, int>();
    private readonly IMonotonicClock clock;
    private readonly int maximumScheduledWorkPerPlugin;
    private readonly int maximumBackgroundWorkPerPlugin;
    private uint latestUpdateVersion;
    private int stopping;

    /// <summary>Creates the production scheduler with bounded scheduled and background work.</summary>
    public PluginSchedulerHost(int maximumScheduledWorkPerPlugin = 64, int maximumBackgroundWorkPerPlugin = 8)
        : this(StopwatchMonotonicClock.Instance, maximumScheduledWorkPerPlugin, maximumBackgroundWorkPerPlugin)
    {
    }

    /// <summary>Test-only constructor for deterministic non-Stopwatch clock frequencies.</summary>
    internal PluginSchedulerHost(IMonotonicClock clock, int maximumScheduledWorkPerPlugin = 64, int maximumBackgroundWorkPerPlugin = 8)
    {
        this.clock = clock ?? throw new ArgumentNullException(nameof(clock));
        if (clock.Frequency <= 0) throw new ArgumentOutOfRangeException(nameof(clock));
        if (maximumScheduledWorkPerPlugin <= 0) throw new ArgumentOutOfRangeException(nameof(maximumScheduledWorkPerPlugin));
        if (maximumBackgroundWorkPerPlugin <= 0) throw new ArgumentOutOfRangeException(nameof(maximumBackgroundWorkPerPlugin));
        this.maximumScheduledWorkPerPlugin = maximumScheduledWorkPerPlugin;
        this.maximumBackgroundWorkPerPlugin = maximumBackgroundWorkPerPlugin;
    }

    public IPluginScheduler CreateService(PluginManifest manifest, IPluginResourceScope resources, IPluginDispatcher dispatcher, IPluginLogger logger)
    {
        if (manifest == null) throw new ArgumentNullException(nameof(manifest));
        if (resources == null) throw new ArgumentNullException(nameof(resources));
        if (dispatcher == null) throw new ArgumentNullException(nameof(dispatcher));
        if (logger == null) throw new ArgumentNullException(nameof(logger));
        ThrowIfStopping();
        var guard = new ScopeGuard();
        try { resources.Own("scheduler", PluginResourceKind.BackgroundTask, guard); }
        catch { guard.Dispose(); throw; }
        return new ScopedScheduler(this, manifest.Id, resources, dispatcher, logger, guard);
    }

    /// <summary>Stops future scheduling and background admission during host shutdown.</summary>
    public void StopAcceptingWork() => Interlocked.Exchange(ref stopping, 1);

    /// <summary>
    /// Cancels currently tracked background work and observes cooperative completion within the
    /// supplied bound. It is intended for shutdown coordination, never a normal update/render path.
    /// </summary>
    public async Task<bool> CancelAndDrainBackgroundWorkAsync(TimeSpan timeout)
    {
        if (timeout < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(timeout));
        StopAcceptingWork();
        BackgroundRegistration[] active;
        lock (gate) active = background.ToArray();
        for (int index = 0; index < active.Length; index++) active[index].RequestCancellation();
        if (active.Length == 0) return true;

        var completions = new Task[active.Length];
        for (int index = 0; index < active.Length; index++) completions[index] = active[index].Completion;
        Task all = Task.WhenAll(completions);
        using var timeoutCancellation = new CancellationTokenSource();
        Task timeoutTask = Task.Delay(timeout, timeoutCancellation.Token);
        if (await Task.WhenAny(all, timeoutTask).ConfigureAwait(false) != all)
            return false;
        timeoutCancellation.Cancel();
        await all.ConfigureAwait(false);
        return true;
    }

    /// <summary>Called once from the verified host update boundary after input state is current.</summary>
    public void Tick(uint updateVersion)
    {
        if (Volatile.Read(ref stopping) != 0) return;
        ScheduledWork[] due;
        long now = clock.GetTimestamp();
        lock (gate)
        {
            latestUpdateVersion = updateVersion;
            if (scheduled.Count == 0) return;
            var list = new List<ScheduledWork>();
            for (int index = 0; index < scheduled.Count; index++)
            {
                ScheduledWork work = scheduled[index];
                if (work.IsReleased || work.Guard.IsReleased) { scheduled.RemoveAt(index--); continue; }
                if (work.InFlight || !work.IsDue(updateVersion, now)) continue;
                work.InFlight = true;
                list.Add(work);
            }
            due = list.ToArray();
        }

        for (int index = 0; index < due.Length; index++)
        {
            ScheduledWork work = due[index];
            try { work.Dispatcher.Post(() => Execute(work, updateVersion)); }
            catch (Exception exception)
            {
                work.Logger.Error("Scheduled work '" + work.Name + "' could not be queued for plugin '" + work.Owner.Value + "'.", exception);
                Complete(work, updateVersion, now);
            }
        }
    }

    private void Execute(ScheduledWork work, uint updateVersion)
    {
        bool entered = false;
        try
        {
            entered = !work.IsReleased && work.Guard.TryEnterCallback();
            if (entered) work.Callback();
        }
        catch (Exception exception)
        {
            work.Logger.Error("Scheduled work '" + work.Name + "' failed for plugin '" + work.Owner.Value + "'.", exception);
        }
        finally
        {
            if (entered) work.Guard.ExitCallback();
            Complete(work, updateVersion, clock.GetTimestamp());
        }
    }

    private void Complete(ScheduledWork work, uint updateVersion, long now)
    {
        lock (gate)
        {
            work.InFlight = false;
            if (work.IsReleased || work.Guard.IsReleased || !work.Repeat || Volatile.Read(ref stopping) != 0)
            {
                scheduled.Remove(work);
                work.Dispose();
                return;
            }
            work.ScheduleNext(updateVersion, now);
        }
    }

    private IPluginRegistration Schedule(PluginId owner, IPluginResourceScope resources, IPluginDispatcher dispatcher, IPluginLogger logger, ScopeGuard guard, string name, uint? updateDelay, TimeSpan? elapsedDelay, bool repeat, Action callback)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("A diagnostic name is required.", nameof(name));
        if (callback == null) throw new ArgumentNullException(nameof(callback));
        ThrowIfUnavailable(guard);
        long elapsedDelayTicks = elapsedDelay.HasValue ? MonotonicClockMath.ToClockTicks(elapsedDelay.Value, clock.Frequency) : 0;
        var work = new ScheduledWork(this, owner, name, dispatcher, logger, guard, updateDelay, elapsedDelayTicks, repeat, callback, clock.GetTimestamp(), 0);
        lock (gate)
        {
            ThrowIfUnavailable(guard);
            int owned = 0;
            for (int index = 0; index < scheduled.Count; index++) if (scheduled[index].Owner == owner && !scheduled[index].IsReleased) owned++;
            if (owned >= maximumScheduledWorkPerPlugin) throw new InvalidOperationException("The scheduled-work limit was reached for plugin '" + owner.Value + "'.");
            work.SetInitialUpdateDue(latestUpdateVersion);
            scheduled.Add(work);
        }
        try { resources.Own("scheduled:" + name, PluginResourceKind.BackgroundTask, work); }
        catch { work.Dispose(); throw; }
        return work;
    }

    private IPluginRegistration StartBackground(PluginId owner, IPluginResourceScope resources, IPluginLogger logger, ScopeGuard guard, string name, Func<CancellationToken, Task> callback)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("A diagnostic name is required.", nameof(name));
        if (callback == null) throw new ArgumentNullException(nameof(callback));
        ThrowIfUnavailable(guard);
        BackgroundRegistration registration;
        lock (gate)
        {
            ThrowIfUnavailable(guard);
            backgroundCounts.TryGetValue(owner, out int activeCount);
            if (activeCount >= maximumBackgroundWorkPerPlugin)
                throw new InvalidOperationException("The background-work limit was reached for plugin '" + owner.Value + "'.");
            registration = new BackgroundRegistration(this, owner, name, logger, guard, callback);
            background.Add(registration);
            backgroundCounts[owner] = activeCount + 1;
        }
        try { resources.Own("background:" + name, PluginResourceKind.BackgroundTask, registration); }
        catch { registration.Dispose(); throw; }
        registration.Start();
        return registration;
    }

    private void Remove(ScheduledWork work)
    {
        lock (gate) scheduled.Remove(work);
    }

    private void CompleteBackground(BackgroundRegistration registration)
    {
        lock (gate)
        {
            if (!background.Remove(registration)) return;
            backgroundCounts.TryGetValue(registration.Owner, out int activeCount);
            if (activeCount <= 1) backgroundCounts.Remove(registration.Owner);
            else backgroundCounts[registration.Owner] = activeCount - 1;
        }
    }

    /// <summary>Test diagnostic for physical in-flight background ownership.</summary>
    internal int GetBackgroundWorkCount(PluginId owner)
    {
        lock (gate)
            return backgroundCounts.TryGetValue(owner, out int count) ? count : 0;
    }

    private void ThrowIfUnavailable(ScopeGuard guard)
    {
        if (guard.IsReleased) throw new ObjectDisposedException("IPluginScheduler", "The owning plugin activation has ended.");
        ThrowIfStopping();
    }

    private void ThrowIfStopping()
    {
        if (Volatile.Read(ref stopping) != 0) throw new ObjectDisposedException("PluginSchedulerHost", "The scheduler is shutting down.");
    }

    private sealed class ScopedScheduler : IPluginScheduler
    {
        private readonly PluginSchedulerHost host; private readonly PluginId owner; private readonly IPluginResourceScope resources; private readonly IPluginDispatcher dispatcher; private readonly IPluginLogger logger; private readonly ScopeGuard guard;
        public ScopedScheduler(PluginSchedulerHost host, PluginId owner, IPluginResourceScope resources, IPluginDispatcher dispatcher, IPluginLogger logger, ScopeGuard guard) { this.host = host; this.owner = owner; this.resources = resources; this.dispatcher = dispatcher; this.logger = logger; this.guard = guard; }
        public IPluginRegistration NextUpdate(string name, Action callback) => host.Schedule(owner, resources, dispatcher, logger, guard, name, 1, null, false, callback);
        public IPluginRegistration AfterUpdates(string name, uint updateCount, Action callback) { if (updateCount == 0) throw new ArgumentOutOfRangeException(nameof(updateCount)); return host.Schedule(owner, resources, dispatcher, logger, guard, name, updateCount, null, false, callback); }
        public IPluginRegistration EveryUpdates(string name, uint updateInterval, Action callback) { if (updateInterval == 0) throw new ArgumentOutOfRangeException(nameof(updateInterval)); return host.Schedule(owner, resources, dispatcher, logger, guard, name, updateInterval, null, true, callback); }
        public IPluginRegistration After(string name, TimeSpan delay, Action callback) { if (delay <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(delay)); return host.Schedule(owner, resources, dispatcher, logger, guard, name, null, delay, false, callback); }
        public IPluginRegistration Every(string name, TimeSpan interval, Action callback) { if (interval <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(interval)); return host.Schedule(owner, resources, dispatcher, logger, guard, name, null, interval, true, callback); }
        public IPluginRegistration RunBackground(string name, Func<CancellationToken, Task> callback) => host.StartBackground(owner, resources, logger, guard, name, callback);
    }

    private sealed class ScopeGuard : IDisposable
    {
        private readonly object gate = new object();
        private bool released;
        private int callbackLeases;
        public bool IsReleased { get { lock (gate) return released; } }
        public bool TryEnterCallback() { lock (gate) { if (released) return false; callbackLeases++; return true; } }
        public void ExitCallback() { lock (gate) { if (callbackLeases > 0) callbackLeases--; } }
        public void Dispose() { lock (gate) released = true; }
    }

    private sealed class ScheduledWork : IPluginRegistration
    {
        private readonly PluginSchedulerHost host;
        private int released;
        private long dueElapsedTicks;
        private uint dueUpdate;
        public ScheduledWork(PluginSchedulerHost host, PluginId owner, string name, IPluginDispatcher dispatcher, IPluginLogger logger, ScopeGuard guard, uint? updateDelay, long elapsedDelayTicks, bool repeat, Action callback, long now, uint initialUpdateVersion)
        { this.host = host; Owner = owner; Name = name; Dispatcher = dispatcher; Logger = logger; Guard = guard; UpdateDelay = updateDelay; ElapsedDelayTicks = elapsedDelayTicks; Repeat = repeat; Callback = callback; dueUpdate = unchecked(initialUpdateVersion + updateDelay.GetValueOrDefault()); dueElapsedTicks = updateDelay.HasValue ? long.MaxValue : MonotonicClockMath.SaturatingAdd(now, elapsedDelayTicks); }
        public PluginId Owner { get; } public string Name { get; } public IPluginDispatcher Dispatcher { get; } public IPluginLogger Logger { get; } public ScopeGuard Guard { get; } public uint? UpdateDelay { get; } public long ElapsedDelayTicks { get; } public bool Repeat { get; } public Action Callback { get; } public bool InFlight { get; set; }
        public bool IsReleased => Volatile.Read(ref released) != 0;
        public bool IsDue(uint updateVersion, long elapsedTicks) => UpdateDelay.HasValue ? updateVersion >= dueUpdate : elapsedTicks >= dueElapsedTicks;
        public void SetInitialUpdateDue(uint updateVersion) { if (UpdateDelay.HasValue) dueUpdate = unchecked(updateVersion + UpdateDelay.Value); }
        public void ScheduleNext(uint updateVersion, long elapsedTicks) { if (UpdateDelay.HasValue) dueUpdate = unchecked(updateVersion + UpdateDelay.Value); else dueElapsedTicks = MonotonicClockMath.SaturatingAdd(elapsedTicks, ElapsedDelayTicks); }
        public void Dispose() { if (Interlocked.Exchange(ref released, 1) == 0) host.Remove(this); }
    }

    private sealed class BackgroundRegistration : IPluginRegistration
    {
        private readonly PluginSchedulerHost host; private readonly IPluginLogger logger; private readonly ScopeGuard guard; private readonly Func<CancellationToken, Task> callback; private readonly CancellationTokenSource cancellation = new CancellationTokenSource(); private readonly object startGate = new object();
        private Task? task; private int released; private int completed;
        internal BackgroundRegistration(PluginSchedulerHost host, PluginId owner, string name, IPluginLogger logger, ScopeGuard guard, Func<CancellationToken, Task> callback) { this.host = host; Owner = owner; Name = name; this.logger = logger; this.guard = guard; this.callback = callback; }
        internal PluginId Owner { get; }
        public string Name { get; }
        public bool IsReleased => Volatile.Read(ref released) != 0;
        internal Task Completion { get { lock (startGate) return task ?? Task.CompletedTask; } }

        internal void Start()
        {
            bool completeWithoutStarting = false;
            lock (startGate)
            {
                if (task != null) return;
                if (IsReleased || guard.IsReleased) completeWithoutStarting = true;
                else task = Task.Run(RunAsync);
            }
            if (completeWithoutStarting) Complete();
        }

        internal void RequestCancellation()
        {
            try { cancellation.Cancel(); } catch (ObjectDisposedException) { }
        }

        private async Task RunAsync()
        {
            bool entered = guard.TryEnterCallback();
            try
            {
                if (!entered || IsReleased || cancellation.IsCancellationRequested) return;
                await callback(cancellation.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested || guard.IsReleased)
            {
                // Activation cancellation is an expected completion path.
            }
            catch (Exception exception)
            {
                logger.Error("Background work '" + Name + "' failed for plugin '" + Owner.Value + "'.", exception);
            }
            finally
            {
                if (entered) guard.ExitCallback();
                Complete();
            }
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref released, 1) != 0) return;
            RequestCancellation();
            bool completeWithoutStarting;
            lock (startGate) completeWithoutStarting = task == null;
            if (completeWithoutStarting) Complete();
        }

        private void Complete()
        {
            if (Interlocked.Exchange(ref completed, 1) != 0) return;
            host.CompleteBackground(this);
            cancellation.Dispose();
        }
    }
}
