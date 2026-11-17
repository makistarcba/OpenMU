// <copyright file="EventPanelViewPlugIn.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameServer.RemoteView.Vorthania;

using System.Buffers.Binary;
using System.Runtime.InteropServices;
using MUnique.OpenMU.GameLogic.PlugIns.Vorthania.EventPanel;
using MUnique.OpenMU.Network;
using MUnique.OpenMU.Network.PlugIns;
using MUnique.OpenMU.PlugIns;

/// <summary>
/// VORTHANIA: Sends the event panel (key H) to the Vorthania client.
/// </summary>
/// <remarks>
/// The packet is written by hand instead of being defined in the packet XML: it only exists between
/// this server and the Vorthania client, and that way neither the generated packet classes nor the
/// client library have to be regenerated.
///
/// Layout (multi-byte values little endian, except the size of the header):
/// <code>
/// 0  C2, size (2 bytes, big endian), F5, 49
/// 5  server time: hour, minute, second (in the server time zone)
/// 8  number of events (byte)
///    per event:
///      kind (byte), state (byte), start hour (byte), start minute (byte),
///      seconds (uint32): until the start, or until the end while running,
///      name (byte length + UTF-8),
///      number of monster groups (byte)
///      per group: alive (uint16), total (uint16), monster (byte length + UTF-8), map (byte length + UTF-8)
/// </code>
/// </remarks>
[PlugIn]
[Display(Name = "Vorthania: event panel view", Description = "Sends the event panel (key H) to the Vorthania client.")]
[Guid("3B8E5F27-C1D4-4A69-8F02-6D9A4E1C7B53")]
[MinimumClient(106, 3, ClientLanguage.Invariant)]
public class EventPanelViewPlugIn : IEventPanelViewPlugIn
{
    /// <summary>The sub code of the packet in the 0xF5 group.</summary>
    public const byte SubCode = 0x49;

    private const int MaxEvents = 32;
    private const int MaxMonsterGroups = 32;
    private const int MaxTextBytes = 40;

    private readonly RemotePlayer _player;

    /// <summary>
    /// Initializes a new instance of the <see cref="EventPanelViewPlugIn"/> class.
    /// </summary>
    /// <param name="player">The player.</param>
    public EventPanelViewPlugIn(RemotePlayer player) => this._player = player;

    /// <inheritdoc/>
    public async ValueTask ShowEventPanelAsync(IReadOnlyList<EventPanelEntry> entries, DateTime serverNowUtc, TimeZoneInfo serverTimeZone)
    {
        if (this._player.Connection is not { Connected: true } connection)
        {
            return;
        }

        var packet = BuildPacket(entries, serverNowUtc, serverTimeZone);

        int Write()
        {
            var span = connection.Output.GetSpan(packet.Length)[..packet.Length];
            packet.CopyTo(span);
            return packet.Length;
        }

        await connection.SendAsync(Write).ConfigureAwait(false);
    }

    private static byte[] BuildPacket(IReadOnlyList<EventPanelEntry> entries, DateTime serverNowUtc, TimeZoneInfo serverTimeZone)
    {
        var body = new List<byte>(512);
        var nowLocal = TimeZoneInfo.ConvertTimeFromUtc(serverNowUtc, serverTimeZone);
        body.Add((byte)nowLocal.Hour);
        body.Add((byte)nowLocal.Minute);
        body.Add((byte)nowLocal.Second);

        var events = entries.Take(MaxEvents).ToList();
        body.Add((byte)events.Count);
        foreach (var entry in events)
        {
            body.Add((byte)entry.Kind);
            body.Add((byte)entry.State);

            var startLocal = entry.StartUtc is { } start ? TimeZoneInfo.ConvertTimeFromUtc(start, serverTimeZone) : (DateTime?)null;
            body.Add((byte)(startLocal?.Hour ?? 0));
            body.Add((byte)(startLocal?.Minute ?? 0));

            var target = entry.State == EventPanelEntryState.Running ? entry.EndUtc : entry.StartUtc;
            var seconds = target is { } t ? Math.Max(0, (t - serverNowUtc).TotalSeconds) : 0;
            AddUInt32(body, (uint)Math.Min(seconds, uint.MaxValue));

            AddText(body, entry.Name);

            var groups = entry.Monsters.Take(MaxMonsterGroups).ToList();
            body.Add((byte)groups.Count);
            foreach (var group in groups)
            {
                AddUInt16(body, (ushort)Math.Clamp(group.Alive, 0, ushort.MaxValue));
                AddUInt16(body, (ushort)Math.Clamp(group.Total, 0, ushort.MaxValue));
                AddText(body, group.MonsterName);
                AddText(body, group.MapName);
            }
        }

        var size = 5 + body.Count;
        var packet = new byte[size];
        packet[0] = 0xC2;
        BinaryPrimitives.WriteUInt16BigEndian(packet.AsSpan(1, 2), (ushort)size);
        packet[3] = 0xF5;
        packet[4] = SubCode;
        body.CopyTo(packet, 5);
        return packet;
    }

    private static void AddUInt16(List<byte> target, ushort value)
    {
        Span<byte> buffer = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16LittleEndian(buffer, value);
        target.Add(buffer[0]);
        target.Add(buffer[1]);
    }

    private static void AddUInt32(List<byte> target, uint value)
    {
        Span<byte> buffer = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(buffer, value);
        foreach (var b in buffer)
        {
            target.Add(b);
        }
    }

    private static void AddText(List<byte> target, string? text)
    {
        var bytes = Encoding.UTF8.GetBytes(text ?? string.Empty);
        var length = bytes.Length;
        if (length > MaxTextBytes)
        {
            // Don't cut a multi-byte character in half.
            length = MaxTextBytes;
            while (length > 0 && (bytes[length] & 0xC0) == 0x80)
            {
                length--;
            }
        }

        target.Add((byte)length);
        for (var i = 0; i < length; i++)
        {
            target.Add(bytes[i]);
        }
    }
}
