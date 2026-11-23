// <copyright file="EventPanelNames.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns.Vorthania.EventPanel;

using MUnique.OpenMU.GameLogic.PlugIns.InvasionEvents;

/// <summary>
/// VORTHANIA: The names under which the periodic events appear in the event panel.
/// </summary>
/// <remarks>
/// Only the events listed here are shown, so that technical periodic tasks (saving, shield
/// recovery, ...) stay out of the panel. The exception are invasions: every plugin based on
/// <see cref="BaseInvasionPlugIn{TConfiguration}"/> is shown, also new ones which aren't listed
/// yet, under the name of its class without the "InvasionPlugIn" suffix.
/// </remarks>
public static class EventPanelNames
{
    private static readonly Dictionary<string, (string Name, EventPanelEntryKind Kind)> KnownEvents = new()
    {
        ["GoldenInvasionPlugIn"] = ("Golden Invasion", EventPanelEntryKind.Invasion),
        ["RedDragonInvasionPlugIn"] = ("Red Dragon", EventPanelEntryKind.Invasion),
        ["WhiteWizardInvasionPlugIn"] = ("White Wizard", EventPanelEntryKind.Invasion),
        ["SkeletonKingInvasionPlugIn"] = ("Skeleton King", EventPanelEntryKind.Invasion),
        ["BloodCastleStartPlugIn"] = ("Blood Castle", EventPanelEntryKind.MiniGame),
        ["ChaosCastleStartPlugIn"] = ("Chaos Castle", EventPanelEntryKind.MiniGame),
        ["DevilSquareStartPlugIn"] = ("Devil Square", EventPanelEntryKind.MiniGame),
        ["KanturuStartPlugIn"] = ("Kanturu", EventPanelEntryKind.MiniGame),
        ["HappyHourPlugIn"] = ("Happy Hour", EventPanelEntryKind.HappyHour),
    };

    /// <summary>
    /// Gets the name and kind of the event of the given plugin type.
    /// </summary>
    /// <param name="plugInType">The type of the plugin.</param>
    /// <returns>The name and kind, or <c>null</c> if the event isn't shown in the panel.</returns>
    public static (string Name, EventPanelEntryKind Kind)? Get(Type plugInType)
    {
        if (KnownEvents.TryGetValue(plugInType.Name, out var known))
        {
            return known;
        }

        if (IsInvasion(plugInType))
        {
            var name = plugInType.Name.Replace("InvasionPlugIn", string.Empty, StringComparison.Ordinal)
                .Replace("PlugIn", string.Empty, StringComparison.Ordinal);
            return (name, EventPanelEntryKind.Invasion);
        }

        return null;
    }

    private static bool IsInvasion(Type type)
    {
        for (var current = type.BaseType; current is not null; current = current.BaseType)
        {
            if (current.IsGenericType && current.GetGenericTypeDefinition() == typeof(BaseInvasionPlugIn<>))
            {
                return true;
            }
        }

        return false;
    }
}
