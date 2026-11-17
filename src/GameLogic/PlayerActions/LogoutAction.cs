// <copyright file="LogoutAction.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlayerActions;

using System.Collections.Concurrent;
using MUnique.OpenMU.GameLogic.Views.Login;

/// <summary>
/// Action to log the player out of the game.
/// </summary>
public class LogoutAction
{
    /// <summary>
    /// VORTHANIA: Seconds a player has to wait in the world after requesting a logout.
    /// </summary>
    private const int CountdownSeconds = 5;

    /// <summary>
    /// VORTHANIA: The players with a running logout countdown.
    /// </summary>
    private static readonly ConcurrentDictionary<Player, LogoutType> PendingLogouts = new();

    /// <summary>
    /// VORTHANIA: Handles a logout which was requested by the player through the game menu.
    /// It's refused while the player has attacked another player and that self-defense is
    /// still active. Otherwise, the player stays in the world during a countdown of
    /// <see cref="CountdownSeconds"/> seconds and can still be hit in the meantime.
    /// Monsters don't prevent the logout.
    /// </summary>
    /// <param name="player">The player.</param>
    /// <param name="logoutType">Type of the logout.</param>
    public async ValueTask RequestLogoutAsync(Player player, LogoutType logoutType)
    {
        if (logoutType > LogoutType.BackToServerSelection)
        {
            // Not a type which a client may request.
            return;
        }

        if (!IsInWorld(player))
        {
            // e.g. at the character selection screen: nothing to protect.
            await this.LogoutAsync(player, logoutType).ConfigureAwait(false);
            return;
        }

        if (PendingLogouts.ContainsKey(player))
        {
            return;
        }

        if (player.IsAnySelfDefenseActive())
        {
            await RefuseAsync(player).ConfigureAwait(false);
            return;
        }

        if (!PendingLogouts.TryAdd(player, logoutType))
        {
            return;
        }

        // Not awaited: the packet handler must not be blocked during the countdown.
        _ = Task.Run(() => this.RunCountdownAsync(player, logoutType));
    }

    /// <summary>
    /// Logs out the specified player.
    /// </summary>
    /// <param name="player">The player.</param>
    /// <param name="logoutType">Type of the logout.</param>
    public async ValueTask LogoutAsync(Player player, LogoutType logoutType)
    {
        await player.RemoveFromGameAsync().ConfigureAwait(false);

        if (logoutType == LogoutType.CloseGame)
        {
            await player.InvokeViewPlugInAsync<ILogoutPlugIn>(p => p.LogoutAsync(logoutType)).ConfigureAwait(false);
            await player.DisconnectAsync().ConfigureAwait(false);
        }
        else
        {
            if (logoutType == LogoutType.BackToCharacterSelection)
            {
                await player.PlayerState.TryAdvanceToAsync(PlayerState.Authenticated).ConfigureAwait(false);
            }

            await player.InvokeViewPlugInAsync<ILogoutPlugIn>(p => p.LogoutAsync(logoutType)).ConfigureAwait(false);
        }
    }

    private static bool IsInWorld(Player player)
    {
        return player.IsConnected && player.SelectedCharacter is not null && player.CurrentMap is not null;
    }

    private static async ValueTask RefuseAsync(Player player)
    {
        await player.ShowBlueMessageAsync("No podes salir: atacaste a otro jugador. Espera a que termine la autodefensa.").ConfigureAwait(false);
        await player.InvokeViewPlugInAsync<ILogoutPlugIn>(p => p.LogoutAsync(LogoutType.Refused)).ConfigureAwait(false);
    }

    private async Task RunCountdownAsync(Player player, LogoutType logoutType)
    {
        try
        {
            for (var remaining = CountdownSeconds; remaining > 0; remaining--)
            {
                if (!IsInWorld(player))
                {
                    return;
                }

                if (player.IsAnySelfDefenseActive())
                {
                    // The player attacked someone during the countdown.
                    await RefuseAsync(player).ConfigureAwait(false);
                    return;
                }

                await player.ShowBlueMessageAsync($"Saldras en {remaining}...").ConfigureAwait(false);
                await Task.Delay(TimeSpan.FromSeconds(1)).ConfigureAwait(false);
            }

            if (!IsInWorld(player))
            {
                return;
            }

            if (player.IsAnySelfDefenseActive())
            {
                await RefuseAsync(player).ConfigureAwait(false);
                return;
            }

            await this.LogoutAsync(player, logoutType).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            player.Logger.LogError(ex, "Logout countdown of player {Player} failed.", player);
        }
        finally
        {
            PendingLogouts.TryRemove(player, out _);
        }
    }
}