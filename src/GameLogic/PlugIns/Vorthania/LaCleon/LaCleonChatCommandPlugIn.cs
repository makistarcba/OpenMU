// <copyright file="LaCleonChatCommandPlugIn.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns.Vorthania.LaCleon;

using System.Runtime.InteropServices;
using MUnique.OpenMU.GameLogic.PlugIns.ChatCommands;
using MUnique.OpenMU.PlugIns;

/// <summary>
/// VORTHANIA: <c>/lacleon</c> writes the time of the account for the LaCleon event into the chat.
/// </summary>
[Guid("E7B25D90-4C1A-4F36-8D7E-92A60C3B5F81")]
[PlugIn]
[Display(Name = "Vorthania: /lacleon", Description = "Shows the time which the account has for the LaCleon event.")]
[ChatCommandHelp(Command, typeof(LaCleonChatCommandArgs), CharacterStatus.Normal)]
public class LaCleonChatCommandPlugIn : ChatCommandPlugInBase<LaCleonChatCommandArgs>, IChatCommandPlugIn
{
    private const string Command = "/lacleon";

    /// <inheritdoc/>
    public override string Key => Command;

    /// <inheritdoc/>
    public override CharacterStatus MinCharacterStatusRequirement => CharacterStatus.Normal;

    /// <inheritdoc/>
    protected override async ValueTask DoHandleCommandAsync(Player player, LaCleonChatCommandArgs arguments)
    {
        if (player.GameContext.FeaturePlugIns.GetPlugIn<LaCleonEventPlugIn>() is { } plugIn)
        {
            await plugIn.ShowTimeAsync(player).ConfigureAwait(false);
        }
        else
        {
            await player.ShowBlueMessageAsync("El evento LaCleon está apagado.").ConfigureAwait(false);
        }
    }
}

/// <summary>
/// VORTHANIA: Arguments of <see cref="LaCleonChatCommandPlugIn"/>.
/// </summary>
public class LaCleonChatCommandArgs : ArgumentsBase
{
    /// <summary>
    /// Gets or sets an optional text. It's not used.
    /// </summary>
    [Argument("texto", false)]
    public string? Text { get; set; }
}
