using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Alacrity.PluginSdk;

namespace Alacrity.Core;

/// <summary>
/// Host-owned timing layer. It uses the existing dispatcher for plugin callbacks so timed work
/// shares the same main-thread budget and ownership semantics as ordinary posted work.
/// </summary>
public sealed class PluginSchedulerHost
{
    private readonly object gate = new object();
    private readonly List<ScheduledWork> scheduled = new List<ScheduledWork>();
    private readonly Stopwatch clock = Stopwatch.StartNew();
    private readonly int maximumScheduledWorkPerPlugin;
    private uint latestUpdateVersion;

    public PluginSchedulerHost(int maximumScheduledWorkPerPlugin = 64)
    {
        if (maximumScheduledWorkPerPlugin <= 0) throw new ArgumentOutOfRangeException(nameof(maximumScheduledWorkPerPlugin));
        this.maximumScheduledWorkPerPlugin = maximumScheduledWorkPerPlugin;
    }

    public IPluginScheduler CreateService(PluginManifest manifest, IPluginResourceScope resources, IPluginDispatcher dispatcher, IPluginLogger logger)
    {
        if (manifest == null) throw new ArgumentNullException(nameof(manifest));
        if (resources == null) throw new ArgumentNullException(nameof(resources));
        if (dispatcher == null) throw new ArgumentNullException(nameof(dispatcher));
        if (logger == null) throw new ArgumentNullException(nameof(logger));
        var guard = new ScopeGuard(this);
        try { resources.Own("scheduler", PluginResourceKind.BackgroundTask, guard); }
        catch { guard.Dispose(); throw; }
        return new ScopedScheduler(this, manifest.Id, resources, dispatcher, logger, guard);
    }

    /// <summary>Called once from the verified host update boundary after input state is current.</summary>
    public void Tick(uint updateVersion)
    {
        ScheduledWork[] due;
        long now = clock.ElapsedTicks;
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
            try
            {
                work.Dispatcher.Post(() => Execute(work, updateVersion));
            }
            catch (Exception exception)
            {
                work.Logger.Error("Scheduled work '" + work.Name + "' could not be queued for plugin '" + work.Owner.Value + "'.", exception);
                Complete(work, updateVersion, now, false);
            }
        }
    }

    private void Execute(ScheduledWork work, uint updateVersion)
    {
        bool succeeded = false;
        try
        {
            if (!work.IsReleased && !work.Guard.IsReleased)
            {
                work.Callback();
                succeeded = true;
            }
        }
        catch (Exception exception)
        {
            work.Logger.Error("Scheduled work '" + work.Name + "' failed for plugin '" + work.Owner.Value + "'.", exception);
        }
        finally { Complete(work, updateVersion, clock.ElapsedTicks, succeeded); }
    }

    private void Complete(ScheduledWork work, uint updateVersion, long now, bool succeeded)
    {
        lock (gate)
        {
            work.InFlight = false;
            if (work.IsReleased || work.Guard.IsReleased || !work.Repeat)
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
        if (guard.IsReleased) throw new ObjectDisposedException("IPluginScheduler", "The owning plugin activation has ended.");
        var work = new ScheduledWork(owner, name, dispatcher, logger, guard, updateDelay, elapsedDelay, repeat, callback, clock.ElapsedTicks, 0);
        lock (gate)
        {
            if (guard.IsReleased) throw new ObjectDisposedException("IPluginScheduler", "The owning plugin activation has ended.");
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

    private void Remove(ScheduledWork work)
    {
        lock (gate) scheduled.Remove(work);
    }

    private sealed class ScopedScheduler : IPluginScheduler
    {
        private readonly PluginSchedulerHost host;
        private readonly PluginId owner;
        private readonly IPluginResourceScope resources;
        private readonly IPluginDispatcher dispatcher;
        private readonly IPluginLogger logger;
        private readonly ScopeGuard guard;
        public ScopedScheduler(PluginSchedulerHost host, PluginId owner, IPluginResourceScope resources, IPluginDispatcher dispatcher, IPluginLogger logger, ScopeGuard guard) { this.host = host; this.owner = owner; this.resources = resources; this.dispatcher = dispatcher; this.logger = logger; this.guard = guard; }
        public IPluginRegistration NextUpdate(string name, Action callback) => host.Schedule(owner, resources, dispatcher, logger, guard, name, 1, null, false, callback);
        public IPluginRegistration AfterUpdates(string name, uint updateCount, Action callback) { if (updateCount == 0) throw new ArgumentOutOfRangeException(nameof(updateCount)); return host.Schedule(owner, resources, dispatcher, logger, guard, name, updateCount, null, false, callback); }
        public IPluginRegistration EveryUpdates(string name, uint updateInterval, Action callback) { if (updateInterval == 0) throw new ArgumentOutOfRangeException(nameof(updateInterval)); return host.Schedule(owner, resources, dispatcher, logger, guard, name, updateInterval, null, true, callback); }
        public IPluginRegistration After(string name, TimeSpan delay, Action callback) { if (delay <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(delay)); return host.Schedule(owner, resources, dispatcher, logger, guard, name, null, delay, false, callback); }
        public IPluginRegistration Every(string name, TimeSpan interval, Action callback) { if (interval <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(interval)); return host.Schedule(owner, resources, dispatcher, logger, guard, name, null, interval, true, callback); }
        public IPluginRegistration RunBackground(string name, Func<CancellationToken, Task> callback)
        {
            if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("A diagnostic name is required.", nameof(name));
            if (callback == null) throw new ArgumentNullException(nameof(callback));
            if (guard.IsReleased) throw new ObjectDisposedException("IPluginScheduler", "The owning plugin activation has ended.");
            var registration = new BackgroundRegistration(owner, name, logger, guard, callback);
            try { resources.Own("background:" + name, PluginResourceKind.BackgroundTask, registration); }
            catch { registration.Dispose(); throw; }
            registration.Start();
            return registration;
        }
    }

    private sealed class ScopeGuard : IDisposable
    {
        internal PluginSchedulerHost Host { get; }
        private int released;
        public ScopeGuard(PluginSchedulerHost host) { Host = host; }
        public bool IsReleased => Volatile.Read(ref released) != 0;
        public void Dispose() { if (Interlocked.Exchange(ref released, 1) == 0) { } }
    }

    private sealed class ScheduledWork : IPluginRegistration
    {
        private readonly PluginSchedulerHost host;
        private int released;
        private long dueElapsedTicks;
        private uint dueUpdate;
        public ScheduledWork(PluginId owner, string name, IPluginDispatcher dispatcher, IPluginLogger logger, ScopeGuard guard, uint? updateDelay, TimeSpan? elapsedDelay, bool repeat, Action callback, long now, uint initialUpdateVersion) { this.host = guard.Host; Owner = owner; Name = name; Dispatcher = dispatcher; Logger = logger; Guard = guard; UpdateDelay = updateDelay; ElapsedDelay = elapsedDelay; Repeat = repeat; Callback = callback; dueUpdate = unchecked(initialUpdateVersion + updateDelay.GetValueOrDefault()); dueElapsedTicks = elapsedDelay.HasValue ? now + elapsedDelay.Value.Ticks : long.MaxValue; }
        public PluginId Owner { get; } public string Name { get; } public IPluginDispatcher Dispatcher { get; } public IPluginLogger Logger { get; } public ScopeGuard Guard { get; } public uint? UpdateDelay { get; } public TimeSpan? ElapsedDelay { get; } public bool Repeat { get; } public Action Callback { get; } public bool InFlight { get; set; }
        public bool IsReleased => Volatile.Read(ref released) != 0;
        public bool IsDue(uint updateVersion, long elapsedTicks) => UpdateDelay.HasValue ? updateVersion >= dueUpdate : elapsedTicks >= dueElapsedTicks;
        public void SetInitialUpdateDue(uint updateVersion) { if (UpdateDelay.HasValue) dueUpdate = unchecked(updateVersion + UpdateDelay.Value); }
        public void ScheduleNext(uint updateVersion, long elapsedTicks) { if (UpdateDelay.HasValue) dueUpdate = unchecked(updateVersion + UpdateDelay.Value); else dueElapsedTicks = elapsedTicks + ElapsedDelay!.Value.Ticks; }
        public void Dispose() { if (Interlocked.Exchange(ref released, 1) == 0) host.Remove(this); }
    }

    private sealed class BackgroundRegistration : IPluginRegistration
    {
        private readonly PluginId owner; private readonly IPluginLogger logger; private readonly ScopeGuard guard; private readonly Func<CancellationToken, Task> callback; private readonly CancellationTokenSource cancellation = new CancellationTokenSource(); private int released;
        public BackgroundRegistration(PluginId owner, string name, IPluginLogger logger, ScopeGuard guard, Func<CancellationToken, Task> callback) { this.owner = owner; Name = name; this.logger = logger; this.guard = guard; this.callback = callback; }
        public string Name { get; } public bool IsReleased => Volatile.Read(ref released) != 0;
        public void Start() { Task.Run(async () => { try { await callback(cancellation.Token).ConfigureAwait(false); } catch (OperationCanceledException) when (cancellation.IsCancellationRequested || guard.IsReleased) { } catch (Exception exception) { logger.Error("Background work '" + Name + "' failed for plugin '" + owner.Value + "'.", exception); } }); }
        public void Dispose() { if (Interlocked.Exchange(ref released, 1) == 0) { cancellation.Cancel(); cancellation.Dispose(); } }
    }
}
