// <copyright file="ResetConfiguration.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.Resets;

using MUnique.OpenMU.DataModel.Composition;
using MUnique.OpenMU.DataModel.Configuration.Items;

/// <summary>
/// Configuration of the Reset System.
/// </summary>
public class ResetConfiguration
{
    /// <summary>
    /// Gets or sets the reset limit, which is the maximum amount of possible resets.
    /// </summary>
    [Display(ResourceType = typeof(PlugInResources), Name = nameof(PlugInResources.ResetConfiguration_ResetLimit_Name))]
    public int? ResetLimit { get; set; }

    /// <summary>
    /// Gets or sets the date at which the season started. It's the reference point for the <see cref="ResetLimitSchedule"/>.
    /// </summary>
    /// <remarks>
    /// VORTHANIA: la fecha de apertura del server. La semana 1 son los siete días que empiezan acá.
    /// Sin fecha, el calendario se ignora y manda el <see cref="ResetLimit"/> de siempre.
    /// </remarks>
    [Display(Name = "Season start date")]
    public DateOnly SeasonStart { get; set; }

    /// <summary>
    /// Gets or sets the reset limit per week of the season. It overrides the <see cref="ResetLimit"/>.
    /// </summary>
    /// <remarks>
    /// VORTHANIA: la progresión de la temporada. Cada fila dice "a partir de la semana N, el tope es X".
    /// Se evalúa en cada reset, así que no hay que tocar nada a mano los lunes. Ver docs/12 y docs/14.
    /// </remarks>
    [Display(Name = "Reset limit schedule")]
    [MemberOfAggregate]
    [ScaffoldColumn(true)]
    public ICollection<ResetLimitWeek> ResetLimitSchedule { get; set; } = [];

    /// <summary>
    /// Gets or sets the required level for a reset.
    /// </summary>
    [Display(ResourceType = typeof(PlugInResources), Name = nameof(PlugInResources.ResetConfiguration_RequiredLevel_Name))]
    public int RequiredLevel { get; set; } = 400;

    /// <summary>
    /// Gets or sets the character level after a reset.
    /// </summary>
    [Display(ResourceType = typeof(PlugInResources), Name = nameof(PlugInResources.ResetConfiguration_LevelAfterReset_Name))]
    public int LevelAfterReset { get; set; } = 10;

    /// <summary>
    /// Gets or sets the required money for a reset.
    /// </summary>
    [Display(ResourceType = typeof(PlugInResources), Name = nameof(PlugInResources.ResetConfiguration_RequiredMoney_Name))]
    public int RequiredMoney { get; set; } = 1;

    /// <summary>
    /// Gets or sets a value indicating whether the required money should
    /// be multiplied with the current reset count.
    /// </summary>
    [Display(ResourceType = typeof(PlugInResources), Name = nameof(PlugInResources.ResetConfiguration_MultiplyRequiredMoneyByResetCount_Name))]
    public bool MultiplyRequiredMoneyByResetCount { get; set; } = true;

    /// <summary>
    /// Gets or sets the required item for a reset.
    /// </summary>
    [Display(Name = nameof(MUnique.OpenMU.GameLogic.Properties.PlugInResources.ResetConfiguration_RequiredResetItem_Name), ResourceType = typeof(MUnique.OpenMU.GameLogic.Properties.PlugInResources))]
    public ItemDefinition? RequiredResetItem { get; set; }

    /// <summary>
    /// Gets or sets the item costs per reset range.
    /// </summary>
    [Display(Name = nameof(MUnique.OpenMU.GameLogic.Properties.PlugInResources.ResetConfiguration_ItemCostTiers_Name), ResourceType = typeof(MUnique.OpenMU.GameLogic.Properties.PlugInResources))]
    [MemberOfAggregate]
    [ScaffoldColumn(true)]
    public ICollection<ResetItemCostTier> ItemCostTiers { get; set; } = [];

    /// <summary>
    /// Gets or sets a value indicating whether a reset sets the stat points back to the initial values.
    /// </summary>
    [Display(ResourceType = typeof(PlugInResources), Name = nameof(PlugInResources.ResetConfiguration_ResetStats_Name))]
    public bool ResetStats { get; set; } = true;

    /// <summary>
    /// Gets or sets the legacy amount of points which will be set at the <see cref="Character.LevelUpPoints"/> when doing a reset.
    /// Use <see cref="PointsTiers"/> instead.
    /// </summary>
    [Display(ResourceType = typeof(PlugInResources), Name = nameof(PlugInResources.ResetConfiguration_PointsPerReset_Name))]
    [ScaffoldColumn(false)]
    public int PointsPerReset { get; set; } = 1500;

    /// <summary>
    /// Gets or sets a value indicating whether the legacy <see cref="PointsPerReset"/> should be multiplied with the current reset count.
    /// Use <see cref="PointsTiers"/> instead.
    /// </summary>
    [Display(ResourceType = typeof(PlugInResources), Name = nameof(PlugInResources.ResetConfiguration_MultiplyPointsByResetCount_Name))]
    [ScaffoldColumn(false)]
    public bool MultiplyPointsByResetCount { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether a reset will replace (true) or add (false) the <see cref="Character.LevelUpPoints"/>.
    /// </summary>
    [Display(ResourceType = typeof(PlugInResources), Name = nameof(PlugInResources.ResetConfiguration_ReplacePointsPerReset_Name))]
    public bool ReplacePointsPerReset { get; set; } = true;

    /// <summary>
    /// Gets or sets the points granted per reset range.
    /// </summary>
    [Display(Name = nameof(MUnique.OpenMU.GameLogic.Properties.PlugInResources.ResetConfiguration_PointsTiers_Name), ResourceType = typeof(MUnique.OpenMU.GameLogic.Properties.PlugInResources))]
    [MemberOfAggregate]
    [ScaffoldColumn(true)]
    public ICollection<ResetPointTier> PointsTiers { get; set; } = [];

    /// <summary>
    /// Gets or sets a value indicating whether a reset moves the player home.
    /// </summary>
    [Display(ResourceType = typeof(PlugInResources), Name = nameof(PlugInResources.ResetConfiguration_MoveHome_Name))]
    public bool MoveHome { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether a reset logs the player out back to character selection.
    /// </summary>
    [Display(ResourceType = typeof(PlugInResources), Name = nameof(PlugInResources.ResetConfiguration_LogOut_Name))]
    public bool LogOut { get; set; } = true;

    /// <summary>
    /// Tier definition for the points which are granted when resetting.
    /// </summary>
    public class ResetPointTier
    {
        /// <summary>
        /// Gets or sets the minimum reset count at which this tier applies.
        /// </summary>
        [Display(Name = nameof(MUnique.OpenMU.GameLogic.Properties.PlugInResources.ResetPointTier_MinimumResetCount_Name), ResourceType = typeof(MUnique.OpenMU.GameLogic.Properties.PlugInResources))]
        public int MinimumResetCount { get; set; }

        /// <summary>
        /// Gets or sets the points granted for this tier.
        /// </summary>
        [Display(Name = nameof(MUnique.OpenMU.GameLogic.Properties.PlugInResources.ResetPointTier_PointsGranted_Name), ResourceType = typeof(MUnique.OpenMU.GameLogic.Properties.PlugInResources))]
        public int PointsGranted { get; set; }
    }

    /// <summary>
    /// Tier definition for the reset limit which applies from a week of the season on.
    /// </summary>
    public class ResetLimitWeek
    {
        /// <summary>
        /// Gets or sets the week of the season from which this limit applies. The first week is the week 1.
        /// </summary>
        [Display(Name = "From week")]
        public int FromWeek { get; set; } = 1;

        /// <summary>
        /// Gets or sets the maximum amount of resets from this week on. Zero means no limit.
        /// </summary>
        [Display(Name = "Reset limit")]
        public int ResetLimit { get; set; }
    }

    /// <summary>
    /// Tier definition for required reset item amounts.
    /// </summary>
    public class ResetItemCostTier
    {
        /// <summary>
        /// Gets or sets the minimum reset count at which this tier applies.
        /// </summary>
        [Display(Name = nameof(MUnique.OpenMU.GameLogic.Properties.PlugInResources.ResetItemCostTier_MinimumResetCount_Name), ResourceType = typeof(MUnique.OpenMU.GameLogic.Properties.PlugInResources))]
        public int MinimumResetCount { get; set; }

        /// <summary>
        /// Gets or sets the required item amount for this tier.
        /// </summary>
        [Display(Name = nameof(MUnique.OpenMU.GameLogic.Properties.PlugInResources.ResetItemCostTier_RequiredItemAmount_Name), ResourceType = typeof(MUnique.OpenMU.GameLogic.Properties.PlugInResources))]
        public int RequiredItemAmount { get; set; }
    }
}
