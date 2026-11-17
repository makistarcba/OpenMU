// <copyright file="QuestDefinition.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.DataModel.Configuration.Quests;

using MUnique.OpenMU.Annotations;

/// <summary>
/// The definition of a quest.
/// </summary>
[Cloneable]
public partial class QuestDefinition
{
    /// <summary>
    /// Gets or sets the NPC which gives the quest.
    /// </summary>
    public virtual MonsterDefinition? QuestGiver { get; set; }

    /// <summary>
    /// Gets or sets the name of the quest.
    /// </summary>
    public LocalizedString Name { get; set; }

    /// <summary>
    /// Gets or sets the group identifier of the quest.
    /// </summary>
    public short Group { get; set; }

    /// <summary>
    /// Gets or sets the number of the quest which should be unique within the group.
    /// </summary>
    public short Number { get; set; }

    /// <summary>
    /// Gets or sets the starting number of the quest which should be unique within the group. It's used as an identifier before a quest is started.
    /// It's an identifier which is required on the client side to show the correct starting text.
    /// </summary>
    public short StartingNumber { get; set; }

    /// <summary>
    /// Gets or sets the refuse number of the quest which should be unique within the group. It's used as an identifier after a quest has been refused by the player.
    /// It's an identifier which is required on the client side to show the correct follow-up text.
    /// </summary>
    public short RefuseNumber { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether this <see cref="QuestDefinition"/> is repeatable
    /// which means if the character can start this quest multiple times even after it already
    /// completed it once.
    /// </summary>
    /// <value>
    ///   <c>true</c> if repeatable; otherwise, <c>false</c>.
    /// </value>
    public bool Repeatable { get; set; }

    /// <summary>
    /// VORTHANIA: Gets or sets how often a completed quest becomes available again.
    /// </summary>
    /// <remarks>
    /// Cuando no es <see cref="QuestPeriod.None"/>, el período MANDA por encima de
    /// <see cref="Repeatable"/>: la quest se puede volver a hacer, pero recién después del corte.
    /// El corte se calcula contra <c>CharacterQuestState.LastFinishedUtc</c> usando la zona horaria
    /// del servidor.
    ///
    /// Ojo con los grupos: <c>CharacterQuestState</c> es uno por grupo y guarda una sola fecha, así
    /// que **cada quest con período necesita su propio grupo**. Dos diarias en el mismo grupo
    /// comparten el reloj y se pisan.
    /// </remarks>
    public QuestPeriod Period { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether this quest requires a client action.
    /// </summary>
    public bool RequiresClientAction { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether this quest requires a certain amount of money in the characters
    /// inventory before it can be started.
    /// </summary>
    public int RequiredStartMoney { get; set; }

    /// <summary>
    /// Gets or sets the minimum character level.
    /// </summary>
    public int MinimumCharacterLevel { get; set; }

    /// <summary>
    /// Gets or sets the maximum character level.
    /// </summary>
    public int MaximumCharacterLevel { get; set; }

    /// <summary>
    /// VORTHANIA: Gets or sets the minimum reset count of the character.
    /// </summary>
    /// <remarks>
    /// Upstream solo tiene <see cref="MinimumCharacterLevel"/>, y como un reset devuelve el
    /// personaje al nivel 1, el nivel NO puede expresar "esto se desbloquea en el reset 60".
    /// Este es el gate que necesitan las cadenas por tramo (ver docs/10).
    ///
    /// 0 significa sin mínimo.
    /// </remarks>
    public int MinimumResets { get; set; }

    /// <summary>
    /// VORTHANIA: Gets or sets the maximum reset count of the character. 0 means no maximum.
    /// </summary>
    public int MaximumResets { get; set; }

    /// <summary>
    /// Gets or sets the qualified character class. If <c>null</c>, it's valid for all character classes.
    /// </summary>
    public virtual CharacterClass? QualifiedCharacter { get; set; }

    /// <summary>
    /// Gets or sets the required monster kills to be able to complete this quest.
    /// </summary>
    [MemberOfAggregate]
    public virtual ICollection<QuestMonsterKillRequirement> RequiredMonsterKills { get; protected set; } = null!;

    /// <summary>
    /// Gets or sets the required items which should be in the characters inventory when the
    /// player requests to complete the quest.
    /// </summary>
    [MemberOfAggregate]
    public virtual ICollection<QuestItemRequirement> RequiredItems { get; protected set; } = null!;

    /// <summary>
    /// Gets or sets the rewards when completing the quest successfully.
    /// </summary>
    [MemberOfAggregate]
    public virtual ICollection<QuestReward> Rewards { get; protected set; } = null!;

    /// <inheritdoc />
    public override string? ToString()
    {
        return this.Name.ToString();
    }
}