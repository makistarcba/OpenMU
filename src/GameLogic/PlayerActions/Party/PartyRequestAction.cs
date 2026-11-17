// <copyright file="PartyRequestAction.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlayerActions.Party;

using System.ComponentModel;
using MUnique.OpenMU.GameLogic.Attributes;
using MUnique.OpenMU.GameLogic.MuHelper;
using MUnique.OpenMU.GameLogic.PlugIns;
using MUnique.OpenMU.GameLogic.Views.Party;

/// <summary>
/// The party request action.
/// </summary>
public class PartyRequestAction
{
    /// <summary>
    /// VORTHANIA: The maximum level difference allowed between two party members.
    /// </summary>
    /// <remarks>
    /// Upstream OpenMU does not restrict this at all - a level 400 can party a level 1. The original
    /// game does restrict it, and so does Vorthania: the experience distribution already punishes a
    /// mismatch hard (the base experience is calculated against the party's *average* level, and the
    /// share each member gets is proportional to their level), so an unrestricted party mostly
    /// produces two disappointed players rather than an exploit. This puts a visible, explainable
    /// limit on it instead of letting people discover it as a mystery.
    /// </remarks>
    private const int MaximumPartyLevelDifference = 120;

    /// <summary>
    /// Handles the party request from the <paramref name="player"/> to <paramref name="toRequest"/>.
    /// </summary>
    /// <param name="player">The player.</param>
    /// <param name="toRequest">The player which receives the request.</param>
    public async ValueTask HandlePartyRequestAsync(Player player, Player toRequest)
    {
        var isPartyMember = player.Party != null && !Equals(player.Party.PartyMaster, player);
        if (player.CurrentMiniGame?.Definition.AllowParty is false)
        {
            await player.ShowLocalizedBlueMessageAsync(nameof(PlayerMessage.PartyNotPossibleDuringEvent)).ConfigureAwait(false);
            return;
        }

        if (await IsPartyDeniedAsync(player, toRequest).ConfigureAwait(false))
        {
            return;
        }

        if (toRequest.Party != null || toRequest.LastPartyRequester != null)
        {
            // A server-side bot is asked as well when it is a plain member of its (bot) party: a living
            // player takes precedence over the bot's own company, so it leaves that party and joins the
            // inviter (see BotPartyHandler). Everyone else keeps the original rule - only the master of a
            // party can answer an invitation.
            var isBot = toRequest.Account?.IsBot == true;
            if (toRequest.Party != null && (isBot || Equals(toRequest.Party.PartyMaster, toRequest)))
            {
                if (await PartyRequestHandler.TryAutoAcceptPartyRequestAsync(toRequest, player).ConfigureAwait(false))
                {
                    return;
                }
            }

            await player.ShowLocalizedBlueMessageAsync(nameof(PlayerMessage.PlayerIsAlreadyInParty), toRequest.Name).ConfigureAwait(false);
            return;
        }

        if (isPartyMember)
        {
            await player.ShowLocalizedBlueMessageAsync(nameof(PlayerMessage.YouAreNotPartyMaster)).ConfigureAwait(false);
            return;
        }

        // VORTHANIA: reject the invitation when the levels are too far apart.
        if (GetTotalLevel(player) is { } requesterLevel
            && GetTotalLevel(toRequest) is { } targetLevel
            && Math.Abs(requesterLevel - targetLevel) > MaximumPartyLevelDifference)
        {
            await player.ShowBlueMessageAsync($"The level difference to {toRequest.Name} is too big for a party (maximum {MaximumPartyLevelDifference}).").ConfigureAwait(false);
            return;
        }

        if (toRequest is Offline.OfflinePlayer)
        {
            if (await PartyRequestHandler.TryAutoAcceptPartyRequestAsync(toRequest, player).ConfigureAwait(false))
            {
                return;
            }

            await player.ShowLocalizedBlueMessageAsync(nameof(PlayerMessage.PlayerIsAlreadyInParty), toRequest.Name).ConfigureAwait(false);
            return;
        }

        if (await toRequest.PlayerState.TryAdvanceToAsync(PlayerState.PartyRequest).ConfigureAwait(false))
        {
            await this.SendPartyRequestAsync(toRequest, player).ConfigureAwait(false);
            await player.ShowLocalizedBlueMessageAsync(nameof(PlayerMessage.RequestedPlayerForParty), toRequest.Name).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Determines whether a <see cref="IPartyRequestingPlugIn"/> denies the party between the players.
    /// </summary>
    /// <param name="requester">The player which requested the party.</param>
    /// <param name="target">The requested player.</param>
    /// <returns><c>true</c>, if the party is denied; otherwise, <c>false</c>.</returns>
    internal static async ValueTask<bool> IsPartyDeniedAsync(Player requester, Player target)
    {
        if (requester.GameContext.PlugInManager.GetPlugInPoint<IPartyRequestingPlugIn>() is not { } plugInPoint)
        {
            return false;
        }

        var eventArgs = new CancelEventArgs();
        await plugInPoint.PartyRequestingAsync(requester, target, eventArgs).ConfigureAwait(false);
        return eventArgs.Cancel;
    }

    /// <summary>
    /// VORTHANIA: Gets the total level of a player, or <c>null</c> when it cannot be determined.
    /// </summary>
    /// <param name="player">The player.</param>
    /// <returns>The total level, or <c>null</c>.</returns>
    /// <remarks>
    /// Returning <c>null</c> when the attributes are not available means the check is skipped rather
    /// than blocking the invitation: a missing attribute set is not the player's fault.
    /// </remarks>
    private static int? GetTotalLevel(Player player)
    {
        if (player.Attributes is not { } attributes)
        {
            return null;
        }

        return (int)attributes[Stats.TotalLevel];
    }

    private async ValueTask SendPartyRequestAsync(IPartyMember toRequest, IPartyMember requester)
    {
        if (Equals(requester, toRequest))
        {
            return;
        }

        toRequest.LastPartyRequester = requester;

        if (toRequest is Player receiver && requester is Player requesterPlayer)
        {
            if (await PartyRequestHandler.TryAutoAcceptPartyRequestAsync(receiver, requesterPlayer).ConfigureAwait(false))
            {
                return;
            }
        }

        await toRequest.InvokeViewPlugInAsync<IShowPartyRequestPlugIn>(p => p.ShowPartyRequestAsync(requester)).ConfigureAwait(false);
    }
}