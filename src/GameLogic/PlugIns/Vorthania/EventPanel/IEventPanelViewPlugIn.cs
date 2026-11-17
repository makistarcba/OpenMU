// <copyright file="IEventPanelViewPlugIn.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns.Vorthania.EventPanel;

using MUnique.OpenMU.GameLogic.Views;

/// <summary>
/// VORTHANIA: A view whose client can show the event panel (key H of the Vorthania client).
/// </summary>
public interface IEventPanelViewPlugIn : IViewPlugIn
{
    /// <summary>
    /// Shows the events with their next start and the monsters which are left of running invasions.
    /// </summary>
    /// <param name="entries">The lines of the panel.</param>
    /// <param name="serverNowUtc">The current time of the server, in UTC.</param>
    /// <param name="serverTimeZone">The time zone of the server, in which the times are shown.</param>
    ValueTask ShowEventPanelAsync(IReadOnlyList<EventPanelEntry> entries, DateTime serverNowUtc, TimeZoneInfo serverTimeZone);
}
