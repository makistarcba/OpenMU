// <copyright file="QuestStartAction.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlayerActions.Quests;

using MUnique.OpenMU.DataModel.Configuration.Quests;
using MUnique.OpenMU.GameLogic.Views.Inventory;
using MUnique.OpenMU.GameLogic.Views.Quest;

/// <summary>
/// A player action which implements the starting of a quest.
/// </summary>
public class QuestStartAction
{
    /// <summary>
    /// Tries to start the quest of the given group and number for the specified player.
    /// </summary>
    /// <param name="player">The player.</param>
    /// <param name="group">The group.</param>
    /// <param name="number">The number.</param>
    public async ValueTask StartQuestAsync(Player player, short group, short number)
    {
        using var loggerScope = player.Logger.BeginScope(this.GetType());
        var quest = player.GetQuest(group, number);
        if (quest is null)
        {
            player.Logger.LogWarning("Failed, quest not found");
            return;
        }

        if (quest.MinimumCharacterLevel > player.Level || (quest.MaximumCharacterLevel > 0 && quest.MaximumCharacterLevel < player.Level))
        {
            player.Logger.LogDebug("Failed, character level {0} not in allowed range {1} to {2}.", player.Level, quest.MinimumCharacterLevel, quest.MaximumCharacterLevel);
            return;
        }

        // VORTHANIA: el gate por resets. El nivel no sirve para esto porque un reset devuelve el
        // personaje al nivel 1 (ver docs/10).
        if (!quest.IsResetRangeFulfilled(player))
        {
            player.Logger.LogDebug("Failed, reset count not in allowed range {0} to {1}.", quest.MinimumResets, quest.MaximumResets);
            await player.ShowBlueMessageAsync($"You need at least {quest.MinimumResets} resets for this quest.").ConfigureAwait(false);
            return;
        }

        var questState = player.SelectedCharacter!.QuestStates.FirstOrDefault(q => q.Group == group);
        if (questState is null)
        {
            questState = player.PersistenceContext.CreateNew<CharacterQuestState>();
            questState.Group = group;
            player.SelectedCharacter.QuestStates.Add(questState);
        }

        if (questState.ActiveQuest != null)
        {
            player.Logger.LogDebug("There is already an active quest of this group.");
            await player.InvokeViewPlugInAsync<IQuestProgressPlugIn>(p => p.ShowQuestProgressAsync(questState.ActiveQuest, false)).ConfigureAwait(false);
            return;
        }

        // VORTHANIA: el período manda por encima de Repeatable. Una quest con período se puede
        // rehacer, pero recién después del corte de calendario; una sin período se comporta como
        // en upstream.
        if (quest.Period != QuestPeriod.None)
        {
            if (!quest.IsPeriodElapsed(questState, player.GameContext.ServerTimeZone))
            {
                player.Logger.LogDebug("The quest was already done in this period ({0}).", quest.Period);
                await player.ShowBlueMessageAsync(quest.Period == QuestPeriod.Daily
                    ? "You already completed this quest today. Come back tomorrow."
                    : "You already completed this quest this week. Come back next week.").ConfigureAwait(false);
                return;
            }
        }
        else if (Equals(questState.LastFinishedQuest, quest) && !quest.Repeatable)
        {
            player.Logger.LogDebug("The quest is not repeatable.");
            return;
        }

        if (quest.RequiredStartMoney > 0)
        {
            if (player.TryRemoveMoney(quest.RequiredStartMoney))
            {
                await player.InvokeViewPlugInAsync<IUpdateMoneyPlugIn>(p => p.UpdateMoneyAsync()).ConfigureAwait(false);
            }
            else
            {
                await player.ShowLocalizedBlueMessageAsync(nameof(PlayerMessage.NotEnoughMoneyToProceed)).ConfigureAwait(false);
                return;
            }
        }

        await questState.ClearAsync(player.PersistenceContext).ConfigureAwait(false);
        questState.ActiveQuest = quest;
        await player.InvokeViewPlugInAsync<IQuestStartedPlugIn>(p => p.QuestStartedAsync(quest)).ConfigureAwait(false);
    }
}