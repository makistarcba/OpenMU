// <copyright file="EventPanelModel.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns.Vorthania.EventPanel;

/// <summary>
/// VORTHANIA: What kind of event a line of the event panel (key H) is about.
/// </summary>
public enum EventPanelEntryKind : byte
{
    /// <summary>A monster invasion (Golden, Red Dragon, White Wizard, ...).</summary>
    Invasion = 0,

    /// <summary>A mini game with an entrance (Blood Castle, Chaos Castle, Devil Square).</summary>
    MiniGame = 1,

    /// <summary>The happy hour.</summary>
    HappyHour = 2,

    /// <summary>Anything else.</summary>
    Other = 3,
}

/// <summary>
/// VORTHANIA: The state of an event, as the panel shows it.
/// </summary>
public enum EventPanelEntryState : byte
{
    /// <summary>Waits for its next start.</summary>
    Scheduled = 0,

    /// <summary>Was announced and starts within seconds.</summary>
    Starting = 1,

    /// <summary>Is running (for mini games: the entrance is open).</summary>
    Running = 2,

    /// <summary>Has no start time configured, so it never starts on its own.</summary>
    NoSchedule = 3,
}

/// <summary>
/// VORTHANIA: How many monsters of one kind an invasion spawned on one map and how many are still alive.
/// </summary>
/// <param name="MonsterName">The name of the monster.</param>
/// <param name="MapName">The name of the map.</param>
/// <param name="Alive">The monsters which are still alive.</param>
/// <param name="Total">The monsters which were spawned.</param>
public sealed record EventPanelMonsterGroup(string MonsterName, string MapName, int Alive, int Total);

/// <summary>
/// VORTHANIA: One line of the event panel.
/// </summary>
/// <param name="Name">The name of the event.</param>
/// <param name="Kind">The kind of the event.</param>
/// <param name="State">The state of the event.</param>
/// <param name="StartUtc">The (next or current) start; <c>null</c> without schedule.</param>
/// <param name="EndUtc">The end of the current run; only set while it's running.</param>
/// <param name="Monsters">The monster groups of a running invasion; empty otherwise.</param>
public sealed record EventPanelEntry(
    string Name,
    EventPanelEntryKind Kind,
    EventPanelEntryState State,
    DateTime? StartUtc,
    DateTime? EndUtc,
    IReadOnlyList<EventPanelMonsterGroup> Monsters);

/// <summary>
/// VORTHANIA: A periodic event which can describe itself for the event panel.
/// </summary>
public interface IEventPanelSource
{
    /// <summary>
    /// Gets the line of the event panel for this event, or <c>null</c> if it's not shown.
    /// </summary>
    /// <param name="gameContext">The game context.</param>
    /// <returns>The line of the event panel, or <c>null</c>.</returns>
    EventPanelEntry? GetEventPanelEntry(IGameContext gameContext);
}
