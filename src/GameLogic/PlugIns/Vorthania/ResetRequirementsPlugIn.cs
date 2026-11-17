// <copyright file="ResetRequirementsPlugIn.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns.Vorthania;

using System.Runtime.InteropServices;
using MUnique.OpenMU.DataModel.Composition;
using MUnique.OpenMU.GameLogic.Attributes;
using MUnique.OpenMU.PlugIns;

/// <summary>
/// Restricts maps and the personal store to a range of reset counts.
/// </summary>
/// <remarks>
/// VORTHANIA: OpenMU puede pedir un nivel mínimo para entrar a un mapa
/// (<c>EnterGate.LevelRequirement</c>, <c>WarpInfo.LevelRequirement</c>) y hasta un atributo mínimo
/// (<c>GameMapDefinition.MapRequirements</c>), pero nunca un máximo, y en un server con resets el
/// nivel no dice nada: un jugador de 200 resets pasa la mitad de su vida en nivel 1.
///
/// Este plugin agrega lo que falta: un rango de resets por mapa (la Arena es de 1 a 10) y un mínimo
/// de resets para abrir tienda personal, que es lo que corta a los personajes creados solo para
/// vender. Todo es configuración del panel, sin migración: vive en el JSON del plugin.
///
/// El enganche está en <c>PlayerMapTransitions.WarpToAsync</c>, que es el único punto por el que
/// pasan todas las formas de entrar a un mapa (lista de warp, /move, portal, NPC de puerta, invocar
/// party, scroll, entrada a evento), y en <c>OpenStoreAction.OpenStoreAsync</c>.
/// </remarks>
[PlugIn]
[Display(Name = "Vorthania: Reset requirements", Description = "Reset range per map and minimum resets to open a personal store.")]
[Guid("C4E9B1A7-2F85-4D63-9A0E-7B31C5D8F206")]
public class ResetRequirementsPlugIn
    : IFeaturePlugIn,
      ISupportCustomConfiguration<ResetRequirementsPlugIn.ResetRequirementsConfiguration>,
      ISupportDefaultCustomConfiguration
{
    /// <summary>
    /// Gets or sets the configuration.
    /// </summary>
    public ResetRequirementsConfiguration? Configuration { get; set; }

    /// <summary>
    /// Gets the reset count of the player.
    /// </summary>
    /// <param name="player">The player.</param>
    /// <returns>The reset count, or zero when it's unknown.</returns>
    public static int GetResets(Player player)
    {
        return (int)(player.Attributes?[Stats.Resets] ?? 0f);
    }

    /// <summary>
    /// Determines whether the player is allowed to enter the specified map.
    /// </summary>
    /// <param name="player">The player.</param>
    /// <param name="mapNumber">The number of the map.</param>
    /// <param name="message">The message which should be shown to the player when he's not allowed.</param>
    /// <returns><c>true</c> when the player may enter the map.</returns>
    public bool MayEnterMap(Player player, short mapNumber, out string? message)
    {
        message = null;
        if (this.Configuration is not { } configuration)
        {
            return true;
        }

        if (player.SelectedCharacter?.CharacterStatus >= CharacterStatus.GameMaster)
        {
            return true;
        }

        var requirement = configuration.MapRequirements.FirstOrDefault(r => r.MapNumber == mapNumber);
        if (requirement is null)
        {
            return true;
        }

        var resets = GetResets(player);
        if (resets < requirement.MinimumResets)
        {
            message = string.Format(configuration.MinimumResetsMessage, requirement.MinimumResets);
            return false;
        }

        if (requirement.MaximumResets > 0 && resets > requirement.MaximumResets)
        {
            message = string.Format(configuration.MaximumResetsMessage, requirement.MaximumResets);
            return false;
        }

        return true;
    }

    /// <summary>
    /// Determines whether the player is allowed to open a personal store.
    /// </summary>
    /// <param name="player">The player.</param>
    /// <param name="message">The message which should be shown to the player when he's not allowed.</param>
    /// <returns><c>true</c> when the player may open a store.</returns>
    public bool MayOpenStore(Player player, out string? message)
    {
        message = null;
        if (this.Configuration is not { MinimumResetsForStore: > 0 } configuration)
        {
            return true;
        }

        if (player.SelectedCharacter?.CharacterStatus >= CharacterStatus.GameMaster)
        {
            return true;
        }

        if (GetResets(player) >= configuration.MinimumResetsForStore)
        {
            return true;
        }

        message = string.Format(configuration.StoreMessage, configuration.MinimumResetsForStore);
        return false;
    }

    /// <inheritdoc />
    public object CreateDefaultConfig() => ResetRequirementsConfiguration.CreateDefault();

    /// <summary>
    /// The configuration of the <see cref="ResetRequirementsPlugIn"/>.
    /// </summary>
    public class ResetRequirementsConfiguration
    {
        /// <summary>
        /// Gets or sets the minimum amount of resets which is required to open a personal store.
        /// </summary>
        [Display(Name = "Minimum resets to open a store")]
        public int MinimumResetsForStore { get; set; }

        /// <summary>
        /// Gets or sets the reset requirements per map.
        /// </summary>
        [Display(Name = "Map requirements")]
        [MemberOfAggregate]
        [ScaffoldColumn(true)]
        public ICollection<MapResetRequirement> MapRequirements { get; set; } = [];

        /// <summary>
        /// Gets or sets the message which is shown when the player has too few resets for a map. {0} is the minimum.
        /// </summary>
        [Display(Name = "Message: too few resets for the map")]
        public string MinimumResetsMessage { get; set; } = "Necesitás al menos {0} reset para entrar acá.";

        /// <summary>
        /// Gets or sets the message which is shown when the player has too many resets for a map. {0} is the maximum.
        /// </summary>
        [Display(Name = "Message: too many resets for the map")]
        public string MaximumResetsMessage { get; set; } = "Este mapa es hasta {0} resets. Ya lo pasaste.";

        /// <summary>
        /// Gets or sets the message which is shown when the player may not open a store. {0} is the minimum.
        /// </summary>
        [Display(Name = "Message: too few resets for a store")]
        public string StoreMessage { get; set; } = "Para abrir tienda necesitás {0} resets.";

        /// <summary>
        /// Creates the default configuration of Vorthania.
        /// </summary>
        /// <returns>The default configuration.</returns>
        public static ResetRequirementsConfiguration CreateDefault()
        {
            return new ResetRequirementsConfiguration
            {
                MinimumResetsForStore = 3,
                MapRequirements =
                [
                    new MapResetRequirement { MapNumber = 6, MinimumResets = 1, MaximumResets = 10 },
                ],
            };
        }
    }
}
