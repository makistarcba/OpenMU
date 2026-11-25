// <copyright file="GoblinPointsForEventsPlugIn.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns.Vorthania;

using System.Runtime.InteropServices;
using MUnique.OpenMU.GameLogic.PlugIns.Vorthania.LaCleon;
using MUnique.OpenMU.PlugIns;

/// <summary>
/// VORTHANIA: Goblin Points (la moneda del Cash Shop) que se ganan jugando eventos, sin plata.
/// </summary>
/// <remarks>
/// Tarjetas #13 y #9 de Trello (24/11/2026): el Cash Shop vende cosas como inventario extra o baul
/// extendido, que se compran con puntos ganados en el juego. Se engancha en los mismos momentos en que
/// LaCleon da tiempo (<see cref="LaCleonEventPlugIn.RewardAsync"/>): ultimo golpe a un jefe de evento,
/// entregar el arma en Blood Castle, sobrevivir Devil Square, ganar Chaos Castle.
/// Los puntos de cada cosa se configuran en el panel; en 0 no da nada. Viene apagado.
/// </remarks>
[PlugIn]
[Display(Name = "Vorthania: Goblin Points for events", Description = "Gives Goblin Points (cash shop coin) for event bosses, Blood Castle, Devil Square and Chaos Castle.")]
[Guid("8C2E5B71-4D9A-4F36-A8E0-1B7F3C6D92A4")]
public class GoblinPointsForEventsPlugIn : IFeaturePlugIn, ISupportCustomConfiguration<GoblinPointsForEventsPlugIn.GoblinPointsForEventsConfiguration>, ISupportDefaultCustomConfiguration, IDisabledByDefault
{
    /// <summary>
    /// Gets or sets the configuration.
    /// </summary>
    public GoblinPointsForEventsConfiguration? Configuration { get; set; }

    /// <summary>
    /// Gives the Goblin Points of the specified source to the player, if the plugin is active. Never throws.
    /// </summary>
    /// <param name="player">The player.</param>
    /// <param name="source">The source.</param>
    public static async ValueTask RewardAsync(Player player, LaCleonRewardSource source)
    {
        try
        {
            if (player.GameContext.FeaturePlugIns.GetPlugIn<GoblinPointsForEventsPlugIn>() is not { } plugIn
                || player.Account is not { } account)
            {
                return;
            }

            var configuration = plugIn.Configuration ?? new GoblinPointsForEventsConfiguration();
            var points = source switch
            {
                LaCleonRewardSource.Boss => configuration.PointsPerBoss,
                LaCleonRewardSource.BloodCastle => configuration.PointsPerBloodCastle,
                LaCleonRewardSource.DevilSquare => configuration.PointsPerDevilSquare,
                LaCleonRewardSource.ChaosCastle => configuration.PointsPerChaosCastle,
                _ => 0,
            };
            if (points <= 0)
            {
                return;
            }

            await player.RunPersistenceExclusiveAsync(() =>
            {
                account.GoblinPoints = (int)Math.Min((long)account.GoblinPoints + points, int.MaxValue);
                return ValueTask.CompletedTask;
            }).ConfigureAwait(false);
            await player.ShowBlueMessageAsync($"Ganaste {points} Goblin Points (tenes {account.GoblinPoints}).").ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            player.Logger.LogWarning(ex, "Vorthania: no se pudieron dar los Goblin Points ({Source}).", source);
        }
    }

    /// <inheritdoc />
    public object CreateDefaultConfig() => new GoblinPointsForEventsConfiguration();

    /// <summary>
    /// The configuration of the <see cref="GoblinPointsForEventsPlugIn"/>.
    /// </summary>
    public class GoblinPointsForEventsConfiguration
    {
        /// <summary>
        /// Gets or sets the points for the last hit on an event boss (the bosses configured in LaCleon).
        /// </summary>
        [Display(Name = "Points per event boss", Description = "Last hit on an event boss (the boss list of the LaCleon plugin).")]
        public int PointsPerBoss { get; set; }

        /// <summary>
        /// Gets or sets the points for delivering the weapon in Blood Castle.
        /// </summary>
        [Display(Name = "Points per Blood Castle", Description = "The player who delivers the weapon to the archangel.")]
        public int PointsPerBloodCastle { get; set; }

        /// <summary>
        /// Gets or sets the points for surviving Devil Square.
        /// </summary>
        [Display(Name = "Points per Devil Square", Description = "Every player alive at the end.")]
        public int PointsPerDevilSquare { get; set; }

        /// <summary>
        /// Gets or sets the points for winning Chaos Castle.
        /// </summary>
        [Display(Name = "Points per Chaos Castle", Description = "The winner.")]
        public int PointsPerChaosCastle { get; set; }
    }
}
