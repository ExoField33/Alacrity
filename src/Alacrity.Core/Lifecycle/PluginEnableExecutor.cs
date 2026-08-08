using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Alacrity.PluginSdk;

namespace Alacrity.Core;

/// <summary>Applies a dependency-first enable plan atomically across registered plugin controllers.</summary>
public sealed class PluginEnableExecutor
{
    private readonly PluginNotificationCenter? notifications;
    /// <summary>Creates an executor that optionally publishes transient completion notices.</summary>
    public PluginEnableExecutor(PluginNotificationCenter? notifications = null) { this.notifications = notifications; }
    /// <summary>Enables every planned package or disables newly enabled packages after a failure.</summary>
    public PluginEnableResult Execute(PluginEnablePlan plan, IReadOnlyDictionary<PluginId, PluginLifecycleController> controllers)
    {
        if (plan == null) throw new ArgumentNullException(nameof(plan));
        if (controllers == null) throw new ArgumentNullException(nameof(controllers));
        var newlyEnabled = new List<PluginLifecycleController>();
        var notifications = new List<PluginEnableNotification>();
        try
        {
            foreach (var id in plan.OrderedPlugins)
            {
                if (!controllers.TryGetValue(id, out var controller)) throw new InvalidOperationException("No lifecycle controller is registered for " + id + ".");
                if (controller.State == PluginLifecycleState.Enabled) continue;
                if (controller.State == PluginLifecycleState.Discovered) controller.Validate();
                if (controller.State != PluginLifecycleState.Disabled) throw new InvalidOperationException("Plugin cannot be enabled from state " + controller.State + ": " + id + ".");
                controller.Initialize();
                controller.Enable();
                newlyEnabled.Add(controller);
                foreach (var notification in plan.Notifications)
                    if (notification.Dependency == id) notifications.Add(notification);
            }
            foreach (var notification in notifications) this.notifications?.Publish(notification.Message, TimeSpan.FromSeconds(6));
            return new PluginEnableResult(true, null, notifications, Array.Empty<Exception>());
        }
        catch (Exception exception)
        {
            var rollbackFailures = new List<Exception>();
            for (var index = newlyEnabled.Count - 1; index >= 0; index--)
            {
                try { newlyEnabled[index].Disable(); }
                catch (Exception rollbackFailure) { rollbackFailures.Add(rollbackFailure); }
            }
            return new PluginEnableResult(false, exception, Array.Empty<PluginEnableNotification>(), rollbackFailures);
        }
    }

    /// <summary>Asynchronously enables a dependency plan without scheduling synchronous callbacks onto worker threads.</summary>
    public async Task<PluginEnableResult> ExecuteAsync(PluginEnablePlan plan, IReadOnlyDictionary<PluginId, PluginLifecycleController> controllers, CancellationToken cancellationToken)
    {
        if (plan == null) throw new ArgumentNullException(nameof(plan));
        if (controllers == null) throw new ArgumentNullException(nameof(controllers));
        var newlyEnabled = new List<PluginLifecycleController>();
        var notifications = new List<PluginEnableNotification>();
        try
        {
            foreach (var id in plan.OrderedPlugins)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!controllers.TryGetValue(id, out var controller)) throw new InvalidOperationException("No lifecycle controller is registered for " + id + ".");
                if (controller.State == PluginLifecycleState.Enabled) continue;
                if (controller.State == PluginLifecycleState.Discovered) controller.Validate();
                if (controller.State != PluginLifecycleState.Disabled) throw new InvalidOperationException("Plugin cannot be enabled from state " + controller.State + ": " + id + ".");
                await controller.InitializeAsync(cancellationToken).ConfigureAwait(false);
                await controller.EnableAsync(cancellationToken).ConfigureAwait(false);
                newlyEnabled.Add(controller);
                foreach (var notification in plan.Notifications)
                    if (notification.Dependency == id) notifications.Add(notification);
            }
            foreach (var notification in notifications) this.notifications?.Publish(notification.Message, TimeSpan.FromSeconds(6));
            return new PluginEnableResult(true, null, notifications, Array.Empty<Exception>());
        }
        catch (Exception exception)
        {
            var rollbackFailures = new List<Exception>();
            for (var index = newlyEnabled.Count - 1; index >= 0; index--)
            {
                try { await newlyEnabled[index].DisableAsync(CancellationToken.None).ConfigureAwait(false); }
                catch (Exception rollbackFailure) { rollbackFailures.Add(rollbackFailure); }
            }
            return new PluginEnableResult(false, exception, Array.Empty<PluginEnableNotification>(), rollbackFailures);
        }
    }
}

/// <summary>Outcome of applying one dependency-first plugin enable operation.</summary>
public sealed class PluginEnableResult
{
    internal PluginEnableResult(bool succeeded, Exception? failure, IReadOnlyList<PluginEnableNotification> notifications, IReadOnlyList<Exception> rollbackFailures) { Succeeded = succeeded; Failure = failure; Notifications = notifications; RollbackFailures = rollbackFailures; }
    public bool Succeeded { get; }
    public Exception? Failure { get; }
    public IReadOnlyList<PluginEnableNotification> Notifications { get; }
    public IReadOnlyList<Exception> RollbackFailures { get; }
}
