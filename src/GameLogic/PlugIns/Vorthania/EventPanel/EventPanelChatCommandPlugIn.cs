// <copyright file="EventPanelChatCommandPlugIn.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns.Vorthania.EventPanel;

using System.Runtime.InteropServices;
using MUnique.OpenMU.GameLogic.PlugIns.ChatCommands;
using MUnique.OpenMU.PlugIns;

/// <summary>
/// VORTHANIA: The command behind the event panel (key H of the Vorthania client).
/// </summary>
/// <remarks>
/// <c>/eventos</c> writes the next events into the chat, so it's useful with any client.
/// <c>/eventos abrir</c> and <c>/eventos cerrar</c> are sent by the Vorthania client when the panel
/// is opened and closed; they subscribe to the updates which feed the panel.
/// </remarks>
[Guid("7D3C2E91-5A64-4B0F-9E2D-31A8C6F4B705")]
[PlugIn]
[Display(Name = "Vorthania: /eventos", Description = "Next events and monsters left of running invasions. Feeds the event panel (key H) of the Vorthania client.")]
[ChatCommandHelp(Command, typeof(EventPanelChatCommandArgs), CharacterStatus.Normal)]
public class EventPanelChatCommandPlugIn : ChatCommandPlugInBase<EventPanelChatCommandArgs>, IChatCommandPlugIn
{
    private const string Command = "/eventos";

    /// <inheritdoc/>
    public override string Key => Command;

    /// <inheritdoc/>
    public override CharacterStatus MinCharacterStatusRequirement => CharacterStatus.Normal;

    /// <inheritdoc/>
    protected override async ValueTask DoHandleCommandAsync(Player player, EventPanelChatCommandArgs arguments)
    {
        switch (arguments.Mode?.Trim().ToLowerInvariant())
        {
            case "abrir":
                await EventPanelService.OpenAsync(player).ConfigureAwait(false);
                return;
            case "cerrar":
                EventPanelService.Close(player);
                return;
            default:
                await this.ShowInChatAsync(player).ConfigureAwait(false);
                return;
        }
    }

    private static string FormatDuration(TimeSpan duration)
    {
        if (duration < TimeSpan.Zero)
        {
            duration = TimeSpan.Zero;
        }

        if (duration.TotalMinutes < 1)
        {
            return $"{(int)duration.TotalSeconds} s";
        }

        return duration.TotalHours >= 1
            ? $"{(int)duration.TotalHours} h {duration.Minutes} min"
            : $"{(int)duration.TotalMinutes} min";
    }

    private async ValueTask ShowInChatAsync(Player player)
    {
        var timeZone = player.GameContext.ServerTimeZone;
        var nowUtc = DateTime.UtcNow;
        var entries = EventPanelService.BuildEntries(player.GameContext);
        if (entries.Count == 0)
        {
            await player.ShowBlueMessageAsync("No hay eventos programados.").ConfigureAwait(false);
            return;
        }

        foreach (var entry in entries)
        {
            string line;
            switch (entry.State)
            {
                case EventPanelEntryState.Running:
                    var remaining = entry.EndUtc is { } end ? $", quedan {FormatDuration(end - nowUtc)}" : string.Empty;
                    var label = entry.Kind == EventPanelEntryKind.MiniGame ? "entrada abierta" : "en curso";
                    line = $"{entry.Name}: {label}{remaining}";
                    if (entry.Monsters.Count > 0)
                    {
                        line += $" ({entry.Monsters.Sum(m => m.Alive)}/{entry.Monsters.Sum(m => m.Total)} vivos)";
                    }

                    break;
                case EventPanelEntryState.Starting:
                    line = $"{entry.Name}: empieza ya";
                    break;
                case EventPanelEntryState.Scheduled when entry.StartUtc is { } start:
                    var local = TimeZoneInfo.ConvertTimeFromUtc(start, timeZone);
                    line = $"{entry.Name}: {local:HH:mm} (en {FormatDuration(start - nowUtc)})";
                    break;
                default:
                    line = $"{entry.Name}: sin horario";
                    break;
            }

            await player.ShowBlueMessageAsync(line).ConfigureAwait(false);

            if (entry.State == EventPanelEntryState.Running)
            {
                foreach (var group in entry.Monsters.Where(m => m.Alive > 0))
                {
                    await player.ShowBlueMessageAsync($"  {group.MonsterName} ({group.MapName}): {group.Alive}/{group.Total}").ConfigureAwait(false);
                }
            }
        }
    }
}

/// <summary>
/// VORTHANIA: Arguments of <see cref="EventPanelChatCommandPlugIn"/>.
/// </summary>
public class EventPanelChatCommandArgs : ArgumentsBase
{
    /// <summary>
    /// Gets or sets the mode: empty to write the events into the chat, "abrir" or "cerrar" for the panel.
    /// </summary>
    [Argument("modo", false)]
    public string? Mode { get; set; }
}
