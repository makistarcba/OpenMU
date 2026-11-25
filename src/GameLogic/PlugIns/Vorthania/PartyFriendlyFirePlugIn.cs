// <copyright file="PartyFriendlyFirePlugIn.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns.Vorthania;

using System.ComponentModel;
using System.Runtime.InteropServices;
using MUnique.OpenMU.PlugIns;

/// <summary>
/// VORTHANIA: los miembros de una party no se pueden pegar entre ellos (sin "friendly fire").
/// </summary>
/// <remarks>
/// Tarjeta #1 de Trello (24/11/2026). Vale para golpes, skills de area y mascotas/invocaciones
/// (el punto de plugin recibe al dueño). Un duelo entre dos de la misma party sí se pelea.
/// </remarks>
[PlugIn]
[Display(Name = "Vorthania: no party friendly fire", Description = "Members of the same party can't damage each other (except in a duel).")]
[Guid("3A9F6C21-8B47-4E5D-9C13-F27A0B4D6E85")]
public class PartyFriendlyFirePlugIn : IPlayerAttackRestrictionPlugIn
{
    /// <inheritdoc />
    public void CheckAttack(Player attacker, Player defender, CancelEventArgs eventArgs)
    {
        if (attacker.Party is not { } party || !ReferenceEquals(party, defender.Party))
        {
            return;
        }

        if (attacker.DuelRoom is { State: DuelState.DuelStarted } duelRoom && duelRoom.IsDuelist(defender))
        {
            return;
        }

        eventArgs.Cancel = true;
    }
}
