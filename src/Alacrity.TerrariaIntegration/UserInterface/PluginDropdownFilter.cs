using System;
using System.Collections.Generic;
using Alacrity.PluginSdk;

namespace AlacrityTerraria;

/// <summary>
/// Allocation-free matching for the small, host-owned dropdown collections used by Terraria UI.
/// A linear ordinal-ignore-case scan is faster and simpler than building an index for a chooser
/// containing a few hundred dynamic options at most.
/// </summary>
internal static class PluginDropdownFilter
{
    internal static bool Matches(string label, string value, string searchText)
    {
        if (string.IsNullOrWhiteSpace(searchText))
        {
            return true;
        }

        return (!string.IsNullOrEmpty(label) && label.IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0)
            || (!string.IsNullOrEmpty(value) && value.IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0);
    }

    internal static void Filter(IReadOnlyList<PluginSettingOption> source, string searchText, List<PluginSettingOption> destination)
    {
        destination.Clear();
        if (source == null)
        {
            return;
        }

        for (int index = 0; index < source.Count; index++)
        {
            PluginSettingOption option = source[index];
            if (Matches(option.DisplayName, option.Value, searchText))
            {
                destination.Add(option);
            }
        }
    }
}
