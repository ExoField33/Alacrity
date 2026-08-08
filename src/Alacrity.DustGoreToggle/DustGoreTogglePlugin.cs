using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Alacrity.PluginSdk;

namespace Alacrity.DustGoreToggle;

/// <summary>Owns local Dust/Gore presentation settings and publishes immutable policy snapshots.</summary>
public sealed class DustGoreTogglePlugin : IAlacrityPlugin
{
    private const int MaximumDustId = 999;
    private IPluginContext? context;
    private IPluginSetting<bool>? dustEffectsSetting;
    private IPluginSetting<bool>? goreEffectsSetting;
    private IPluginSetting<int[]>? dustExceptionsSetting;
    private bool dustEffectsEnabled = true;
    private bool goreEffectsEnabled = true;
    private HashSet<int> dustExceptions = new HashSet<int>();
    private int[] dustExceptionSnapshot = Array.Empty<int>();
    private IPluginRegistration? policyRegistration;

    public void Initialize(IPluginContext context)
    {
        this.context = context ?? throw new ArgumentNullException(nameof(context));
        dustEffectsSetting = context.Settings.Register(new PluginSettingDefinition<bool>("dustEffectsEnabled", true));
        goreEffectsSetting = context.Settings.Register(new PluginSettingDefinition<bool>("goreEffectsEnabled", true));
        dustExceptionsSetting = context.Settings.Register(new PluginSettingDefinition<int[]>("dustExceptions", Array.Empty<int>()));
        dustEffectsEnabled = dustEffectsSetting.Value;
        goreEffectsEnabled = goreEffectsSetting.Value;
        dustEffectsSetting.Subscribe(ApplyDustEffectsSetting);
        goreEffectsSetting.Subscribe(ApplyGoreEffectsSetting);
        dustExceptionsSetting.Subscribe(value => { dustExceptions = LoadExceptions(value); RebuildExceptionSnapshot(true); });
        dustExceptions = LoadExceptions(dustExceptionsSetting.Value);
        RebuildExceptionSnapshot(false);

        context.Ui.RegisterSettingsPage(new PluginUiContribution("dust-gore-toggle", "Dust & Gore Toggle"));
        context.Ui.RegisterSettingsControl(PluginSettingControl.Toggle("dust-effects", "Dust Effects", dustEffectsSetting).InPage("dust-gore-toggle"));
        context.Ui.RegisterSettingsControl(PluginSettingControl.Toggle("gore-effects", "Gore Effects", goreEffectsSetting).InPage("dust-gore-toggle"));
        context.Commands.Register(new PluginCommandDescriptor("de", "Manage Dust ID exceptions: /de <id>, /de list, /de clear."), HandleDustExceptionCommand);
        RegisterPolicy();
    }

    public void Enable() { }
    public void Disable() { }
    public void Shutdown() { policyRegistration = null; dustEffectsSetting = null; goreEffectsSetting = null; dustExceptionsSetting = null; context = null; }

    private void ApplyDustEffectsSetting(bool value)
    {
        if (dustEffectsEnabled == value) return;
        dustEffectsEnabled = value;
        RegisterPolicy();
    }

    private void ApplyGoreEffectsSetting(bool value)
    {
        if (goreEffectsEnabled == value) return;
        goreEffectsEnabled = value;
        RegisterPolicy();
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
        int[] snapshot = CreateExceptionSnapshot();
        if (dustExceptionsSetting == null)
        {
            dustExceptionSnapshot = snapshot;
            RegisterPolicy();
            return;
        }
        if (dustExceptionSnapshot.SequenceEqual(snapshot)) return;
        dustExceptionSnapshot = snapshot;
        dustExceptionsSetting.Value = snapshot;
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

    private void RebuildExceptionSnapshot(bool registerPolicy)
    {
        dustExceptionSnapshot = CreateExceptionSnapshot();
        if (registerPolicy) RegisterPolicy();
    }

    private int[] CreateExceptionSnapshot() => dustExceptions.OrderBy(value => value).ToArray();

    private void RegisterPolicy()
    {
        policyRegistration?.Dispose();
        policyRegistration = context?.Terraria.VisualEffects.RegisterPolicy(new PluginVisualEffectsPolicy(dustEffectsEnabled, goreEffectsEnabled, dustExceptionSnapshot));
    }
}
