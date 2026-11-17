// <copyright file="EventPanelService.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns.Vorthania.EventPanel;

using System.Collections.Concurrent;

/// <summary>
/// VORTHANIA: Builds the event panel and keeps the players who have it open up to date.
/// </summary>
/// <remarks>
/// The client opens the panel by sending "/eventos abrir" and closes it with "/eventos cerrar".
/// While it's open, the client repeats "/eventos abrir" every minute, so a subscription which isn't
/// renewed (crash, lost connection) runs out by itself after <see cref="SubscriptionLifetime"/>.
///
/// Changes (an invasion monster died, an event started or ended) are pushed to the subscribers.
/// They are collected for half a second, so that an area attack which kills several golden monsters
/// at once results in one message instead of one per monster.
/// </remarks>
public static class EventPanelService
{
    /// <summary>
    /// How long a subscription lasts without being renewed by the client.
    /// </summary>
    public static readonly TimeSpan SubscriptionLifetime = TimeSpan.FromMinutes(3);

    private static readonly TimeSpan NotificationDelay = TimeSpan.FromMilliseconds(500);

    private static readonly ConcurrentDictionary<Player, DateTime> Subscriptions = new();

    private static readonly ConcurrentDictionary<IGameContext, byte> PendingNotifications = new();

    /// <summary>
    /// Builds the lines of the panel: running events first, then by their next start.
    /// </summary>
    /// <param name="gameContext">The game context.</param>
    /// <returns>The lines of the panel.</returns>
    public static IReadOnlyList<EventPanelEntry> BuildEntries(IGameContext gameContext)
    {
        var entries = new List<EventPanelEntry>();
        foreach (var source in gameContext.PlugInManager.GetActivePlugInsOf<IPeriodicTaskPlugIn>().OfType<IEventPanelSource>())
        {
            try
            {
                if (source.GetEventPanelEntry(gameContext) is { } entry)
                {
                    entries.Add(entry);
                }
            }
            catch (Exception ex)
            {
                gameContext.LoggerFactory.CreateLogger(typeof(EventPanelService).FullName!)
                    .LogWarning(ex, "Event panel: couldn't describe {Source}.", source.GetType().Name);
            }
        }

        return entries
            .OrderBy(e => e.State switch
            {
                EventPanelEntryState.Running => 0,
                EventPanelEntryState.Starting => 1,
                EventPanelEntryState.Scheduled => 2,
                _ => 3,
            })
            .ThenBy(e => e.StartUtc ?? DateTime.MaxValue)
            .ThenBy(e => e.Name, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// Subscribes the player to updates and sends the panel right away.
    /// </summary>
    /// <param name="player">The player.</param>
    public static async ValueTask OpenAsync(Player player)
    {
        Subscriptions[player] = DateTime.UtcNow.Add(SubscriptionLifetime);
        await SendAsync(player, BuildEntries(player.GameContext)).ConfigureAwait(false);
    }

    /// <summary>
    /// Stops sending updates to the player.
    /// </summary>
    /// <param name="player">The player.</param>
    public static void Close(Player player)
    {
        Subscriptions.TryRemove(player, out _);
    }

    /// <summary>
    /// Tells the subscribers of the game context that something changed. Cheap to call often.
    /// </summary>
    /// <param name="gameContext">The game context.</param>
    public static void NotifyChanged(IGameContext gameContext)
    {
        if (Subscriptions.IsEmpty || !PendingNotifications.TryAdd(gameContext, 0))
        {
            return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(NotificationDelay).ConfigureAwait(false);
                PendingNotifications.TryRemove(gameContext, out _);
                await SendToSubscribersAsync(gameContext).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                PendingNotifications.TryRemove(gameContext, out _);
                gameContext.LoggerFactory.CreateLogger(typeof(EventPanelService).FullName!)
                    .LogWarning(ex, "Event panel: couldn't send the update.");
            }
        });
    }

    private static async ValueTask SendToSubscribersAsync(IGameContext gameContext)
    {
        IReadOnlyList<EventPanelEntry>? entries = null;
        var now = DateTime.UtcNow;
        foreach (var (player, expiresUtc) in Subscriptions)
        {
            if (expiresUtc < now || !IsInGame(player))
            {
                Subscriptions.TryRemove(player, out _);
                continue;
            }

            if (!ReferenceEquals(player.GameContext, gameContext))
            {
                continue;
            }

            entries ??= BuildEntries(gameContext);
            await SendAsync(player, entries).ConfigureAwait(false);
        }
    }

    private static bool IsInGame(Player player)
    {
        var state = player.PlayerState.CurrentState;
        return player.SelectedCharacter is not null
               && state != PlayerState.Disconnected
               && state != PlayerState.Finished;
    }

    private static ValueTask SendAsync(Player player, IReadOnlyList<EventPanelEntry> entries)
    {
        var gameContext = player.GameContext;
        return player.InvokeViewPlugInAsync<IEventPanelViewPlugIn>(
            p => p.ShowEventPanelAsync(entries, DateTime.UtcNow, gameContext.ServerTimeZone));
    }
}
