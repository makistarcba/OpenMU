// <copyright file="CharacterQuestState.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.DataModel.Entities;

using MUnique.OpenMU.DataModel.Configuration.Quests;

/// <summary>
/// Keeps the progress state of a started quest.
/// It's only possible to have one quest of the same group active.
/// </summary>
public class CharacterQuestState
{
    /// <summary>
    /// Gets or sets the group.
    /// </summary>
    public short Group { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the game client action was performed.
    /// </summary>
    public bool ClientActionPerformed { get; set; }

    /// <summary>
    /// Gets or sets the last finished quest of the <see cref="Group"/>.
    /// </summary>
    public virtual QuestDefinition? LastFinishedQuest { get; set; }

    /// <summary>
    /// VORTHANIA: Gets or sets when the last quest of this group was finished, in UTC.
    /// </summary>
    /// <remarks>
    /// Es lo único que hacía falta para que existan las diarias y las semanales: upstream no guarda
    /// en ningún lado CUÁNDO se terminó una quest, así que no había forma de saber si la de hoy ya
    /// estaba hecha.
    ///
    /// Se guarda en UTC y se convierte con la zona horaria del servidor al comparar, por la misma
    /// razón que el calendario del Happy Hour: la base no tiene que saber de husos.
    /// </remarks>
    public DateTime? LastFinishedUtc { get; set; }

    /// <summary>
    /// Gets or sets the active quest.
    /// </summary>
    public virtual QuestDefinition? ActiveQuest { get; set; }

    /// <summary>
    /// Gets or sets the requirement states for the current <see cref="ActiveQuest"/>.
    /// </summary>
    [MemberOfAggregate]
    public virtual ICollection<QuestMonsterKillRequirementState> RequirementStates { get; protected set; } = null!;

    /// <inheritdoc />
    public override string ToString()
    {
        return $"#{this.Group}, Last finished: '{this.LastFinishedQuest?.Name}', Active: '{this.ActiveQuest?.Name}'";
    }
}