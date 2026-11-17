// <copyright file="MapResetRequirement.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns.Vorthania;

/// <summary>
/// The reset range which is required to enter a map.
/// </summary>
public class MapResetRequirement
{
    /// <summary>
    /// Gets or sets the number of the map, as it's shown in the game configuration. The Arena is 6.
    /// </summary>
    [Display(Name = "Map number")]
    public short MapNumber { get; set; }

    /// <summary>
    /// Gets or sets the minimum amount of resets which is required to enter. Zero means no minimum.
    /// </summary>
    [Display(Name = "Minimum resets")]
    public int MinimumResets { get; set; }

    /// <summary>
    /// Gets or sets the maximum amount of resets which is allowed to enter. Zero means no maximum.
    /// </summary>
    [Display(Name = "Maximum resets")]
    public int MaximumResets { get; set; }
}
