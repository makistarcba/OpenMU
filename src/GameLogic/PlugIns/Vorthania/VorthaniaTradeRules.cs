// <copyright file="VorthaniaTradeRules.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns.Vorthania;

using MUnique.OpenMU.DataModel.Configuration.Items;

/// <summary>
/// VORTHANIA: reglas de comercio propias de Vorthania.
/// </summary>
/// <remarks>
/// Decisión de Nicolás (24/11/2026, tarjeta #16 de Trello): un ítem con opción de Jewel of Harmony
/// no se puede comerciar (trade ni tienda personal). Para venderlo hay que sacarle la opción antes,
/// así el Harmony gana valor.
/// </remarks>
public static class VorthaniaTradeRules
{
    /// <summary>
    /// El mensaje que ve el jugador cuando intenta comerciar un ítem con Harmony.
    /// </summary>
    public const string HarmonyItemMessage = "Este item tiene opcion de Harmony: no se puede comerciar. Sacale la opcion para venderlo.";

    /// <summary>
    /// Determines whether the item has a Jewel of Harmony option.
    /// </summary>
    /// <param name="item">The item.</param>
    /// <returns><c>True</c>, if the item has a Jewel of Harmony option; Otherwise, <c>false</c>.</returns>
    public static bool HasHarmonyOption(Item item)
    {
        return item.ItemOptions.Any(link => object.Equals(link.ItemOption?.OptionType, ItemOptionTypes.HarmonyOption));
    }
}
