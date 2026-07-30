using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Alacrity.PluginSdk;

namespace Alacrity.DustGoreToggle;

/// <summary>Owns local Dust/Gore presentation settings and publishes immutable policy snapshots.</summary>
public sealed class DustGoreTogglePlugin : IAlacrityPlugin, IVisualEffectsPolicyService
{
    private const int MaximumDustId = 999;
    private IPluginContext? context;
    private bool dustEffectsEnabled = true;
    private bool goreEffectsEnabled = true;
    private HashSet<int> dustExceptions = new HashSet<int>();
    private int[] dustExceptionSnapshot = Array.Empty<int>();
    private VisualEffectsPolicySnapshot policySnapshot = new VisualEffectsPolicySnapshot(true, true, Array.Empty<int>());

    public void Initialize(IPluginContext context)
    {
        this.context = context ?? throw new ArgumentNullException(nameof(context));
        dustEffectsEnabled = context.Settings.Get("dustEffectsEnabled", true);
        goreEffectsEnabled = context.Settings.Get("goreEffectsEnabled", true);
        dustExceptions = LoadExceptions(context.Settings.Get("dustExceptions", Array.Empty<int>()));
        RebuildExceptionSnapshot();

        context.Ui.RegisterSettingsPage(new PluginUiContribution("dust-gore-toggle", "Dust & Gore Toggle"));
        context.Ui.RegisterSettingsControl(PluginSettingControl.Toggle("dust-effects", "Dust Effects", () => dustEffectsEnabled, SetDustEffectsEnabled));
        context.Ui.RegisterSettingsControl(PluginSettingControl.Toggle("gore-effects", "Gore Effects", () => goreEffectsEnabled, SetGoreEffectsEnabled));
        context.Commands.Register(new PluginCommandDescriptor("de", "Manage Dust ID exceptions: /de <id>, /de list, /de clear."), HandleDustExceptionCommand);
        context.Services.Publish<IVisualEffectsPolicyService>(this);
    }

    public void Enable() { }
    public void Disable() { }
    public void Shutdown() => context = null;

    public VisualEffectsPolicySnapshot GetVisualEffectsPolicy() => policySnapshot;

    private void SetDustEffectsEnabled(bool value)
    {
        if (dustEffectsEnabled == value) return;
        dustEffectsEnabled = value;
        context?.Settings.Set("dustEffectsEnabled", value);
        RebuildPolicySnapshot();
    }

    private void SetGoreEffectsEnabled(bool value)
    {
        if (goreEffectsEnabled == value) return;
        goreEffectsEnabled = value;
        context?.Settings.Set("goreEffectsEnabled", value);
        RebuildPolicySnapshot();
    }

    private void HandleDustExceptionCommand(PluginCommandInvocation invocation)
    {
        if (invocation.Arguments.Count == 0)
        {
            invocation.Reply("Usage: /de <dust id>, /de list, /de clear");
            return;
        }

        string argument = invocation.Arguments[0];
        if (string.Equals(argument, "list", StringComparison.OrdinalIgnoreCase))
        {
            invocation.Reply(dustExceptionSnapshot.Length == 0 ? "Dust exceptions: none" : "Dust exceptions: " + string.Join(", ", dustExceptionSnapshot));
            return;
        }
        if (string.Equals(argument, "clear", StringComparison.OrdinalIgnoreCase))
        {
            if (dustExceptions.Count == 0) { invocation.Reply("Dust exceptions are already clear."); return; }
            dustExceptions.Clear();
            SaveExceptions();
            invocation.Reply("Dust exceptions cleared.");
            return;
        }
        if (!int.TryParse(argument, NumberStyles.Integer, CultureInfo.InvariantCulture, out int dustId) || dustId < 0 || dustId > MaximumDustId)
        {
            invocation.Reply("Dust ID must be between 0 and " + MaximumDustId + ".");
            return;
        }

        bool added = dustExceptions.Add(dustId);
        if (!added) dustExceptions.Remove(dustId);
        SaveExceptions();
        invocation.Reply("Dust exception " + dustId + (added ? " added." : " removed."));
    }

    private void SaveExceptions()
    {
        RebuildExceptionSnapshot();
        context?.Settings.Set("dustExceptions", dustExceptionSnapshot);
    }

    private static HashSet<int> LoadExceptions(IReadOnlyList<int> values)
    {
        var result = new HashSet<int>();
        if (values == null) return result;
        for (int index = 0; index < values.Count; index++)
            if (values[index] >= 0 && values[index] <= MaximumDustId)
                result.Add(values[index]);
        return result;
    }

    private void RebuildExceptionSnapshot()
    {
        dustExceptionSnapshot = dustExceptions.OrderBy(value => value).ToArray();
        RebuildPolicySnapshot();
    }

    private void RebuildPolicySnapshot()
    {
        policySnapshot = new VisualEffectsPolicySnapshot(dustEffectsEnabled, goreEffectsEnabled, dustExceptionSnapshot);
    }
}
