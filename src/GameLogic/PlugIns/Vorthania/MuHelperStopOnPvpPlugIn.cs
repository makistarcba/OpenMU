// <copyright file="MuHelperStopOnPvpPlugIn.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns.Vorthania;

using System.Runtime.InteropServices;
using MUnique.OpenMU.GameLogic.Attributes;
using MUnique.OpenMU.GameLogic.NPC;
using MUnique.OpenMU.PlugIns;

/// <summary>
/// VORTHANIA: si un jugador con el MU Helper prendido le pega a otro jugador, el helper se apaga.
/// </summary>
/// <remarks>
/// Tarjeta #7 de Trello (24/11/2026): para que nadie haga PvP con el helper.
/// Aplica siempre que el golpe le hace daño a otro jugador (o a su invocación), también en duelo y
/// en eventos. El helper se vuelve a prender a mano desde el cliente.
/// </remarks>
[PlugIn]
[Display(Name = "Vorthania: MU Helper off on PvP", Description = "Stops the MU Helper of a player when he damages another player.")]
[Guid("7E3D9A15-2C84-4B6F-A0D1-58F2C6B9E347")]
public class MuHelperStopOnPvpPlugIn : IAttackableGotHitPlugIn
{
    /// <summary>
    /// El mensaje que ve el jugador cuando se le apaga el helper.
    /// </summary>
    public const string StoppedMessage = "MU Helper apagado: no se puede usar para pelear contra jugadores.";

    /// <inheritdoc />
    public void AttackableGotHit(IAttackable attackable, IAttacker attacker, HitInfo hitInfo)
    {
        var defender = attackable as Player ?? (attackable as Monster)?.SummonedBy;
        var attackerPlayer = attacker as Player ?? (attacker as Monster)?.SummonedBy;
        if (defender is null || attackerPlayer is null || defender == attackerPlayer)
        {
            return;
        }

        if (hitInfo is { HealthDamage: 0, ShieldDamage: 0 } || hitInfo.Attributes.HasFlag(DamageAttributes.Reflected))
        {
            return;
        }

        if ((attackerPlayer.Attributes?[Stats.IsMuHelperActive] ?? 0) <= 0)
        {
            return;
        }

        _ = StopAsync(attackerPlayer);
    }

    private static async Task StopAsync(Player player)
    {
        try
        {
            await player.MuHelper.StopAsync().ConfigureAwait(false);
            await player.ShowBlueMessageAsync(StoppedMessage).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            player.Logger.LogWarning(ex, "Vorthania: no pude apagar el MU Helper de {Player}.", player.Name);
        }
    }
}
