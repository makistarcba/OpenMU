// <copyright file="LaCleonTimeService.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns.Vorthania.LaCleon;

using System.Globalization;
using System.IO;
using System.Text.Json;

/// <summary>
/// VORTHANIA: Lleva el tiempo de LaCleon de cada cuenta: lo ganado hoy y lo usado hoy.
/// </summary>
/// <remarks>
/// El saldo es por cuenta y se pierde todos los días a las 00:00 (hora del servidor), así que no
/// necesita una tabla en la base: vive en memoria y se copia a un archivo JSON cada pocos segundos
/// para sobrevivir a un reinicio. Si el archivo no se puede escribir, el evento sigue funcionando
/// y solo se pierde el saldo del día al reiniciar.
/// Es estático a propósito: todos los game servers del proceso comparten el mismo saldo.
/// </remarks>
public static class LaCleonTimeService
{
    private static readonly TimeSpan SaveInterval = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Si pasa más que esto entre dos descuentos, se toma como una entrada nueva y no se cobra el hueco.
    /// </summary>
    private static readonly TimeSpan MaximumChargeGap = TimeSpan.FromSeconds(5);

    private static readonly object SyncRoot = new();

    private static readonly Dictionary<string, AccountTime> Accounts = new(StringComparer.OrdinalIgnoreCase);

    private static bool _isLoaded;

    private static bool _isDirty;

    private static DateTime _lastSaveUtc = DateTime.MinValue;

    private static bool _saveFailureLogged;

    /// <summary>
    /// Gets the current day of the server, in the time zone of the server.
    /// </summary>
    /// <param name="gameContext">The game context.</param>
    /// <returns>The day, formatted as yyyy-MM-dd.</returns>
    public static string GetServerDay(IGameContext gameContext)
    {
        var local = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, gameContext.ServerTimeZone);
        return local.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Gets the time of the account for today.
    /// </summary>
    /// <param name="gameContext">The game context.</param>
    /// <param name="stateFile">The path of the state file.</param>
    /// <param name="loginName">The login name of the account.</param>
    /// <returns>The earned and the remaining seconds.</returns>
    public static (int EarnedSeconds, double RemainingSeconds) GetTime(IGameContext gameContext, string stateFile, string loginName)
    {
        lock (SyncRoot)
        {
            EnsureLoaded(gameContext, stateFile);
            var account = GetAccountOfToday(gameContext, loginName, false);
            return account is null ? (0, 0) : (account.EarnedSeconds, account.RemainingSeconds);
        }
    }

    /// <summary>
    /// Adds earned time to the account, up to the daily cap.
    /// </summary>
    /// <param name="gameContext">The game context.</param>
    /// <param name="stateFile">The path of the state file.</param>
    /// <param name="loginName">The login name of the account.</param>
    /// <param name="seconds">The seconds to add.</param>
    /// <param name="dailyCapSeconds">The maximum of seconds which can be earned per day.</param>
    /// <returns>The seconds which were really added, the earned seconds of today and the remaining seconds.</returns>
    public static (int AddedSeconds, int EarnedSeconds, double RemainingSeconds) AddEarnedTime(IGameContext gameContext, string stateFile, string loginName, int seconds, int dailyCapSeconds)
    {
        lock (SyncRoot)
        {
            EnsureLoaded(gameContext, stateFile);
            var account = GetAccountOfToday(gameContext, loginName, true)!;
            var added = Math.Max(0, Math.Min(seconds, dailyCapSeconds - account.EarnedSeconds));
            if (added > 0)
            {
                account.EarnedSeconds += added;
                _isDirty = true;
            }

            return (added, account.EarnedSeconds, account.RemainingSeconds);
        }
    }

    /// <summary>
    /// Charges the time which passed since the last call for this account. Meant to be called every second
    /// while a character of the account is inside of the map.
    /// </summary>
    /// <param name="gameContext">The game context.</param>
    /// <param name="stateFile">The path of the state file.</param>
    /// <param name="loginName">The login name of the account.</param>
    /// <returns>
    /// The remaining seconds before and after the charge, and a flag which tells if this is the first
    /// charge after entering the map.
    /// </returns>
    public static (double PreviousSeconds, double RemainingSeconds, bool IsFirstCharge) Charge(IGameContext gameContext, string stateFile, string loginName)
    {
        lock (SyncRoot)
        {
            EnsureLoaded(gameContext, stateFile);
            var account = GetAccountOfToday(gameContext, loginName, true)!;
            var now = DateTime.UtcNow;
            var previous = account.RemainingSeconds;
            var isFirstCharge = account.LastChargeUtc is not { } lastCharge || now - lastCharge > MaximumChargeGap || now < lastCharge;
            if (!isFirstCharge)
            {
                var elapsed = (now - account.LastChargeUtc!.Value).TotalSeconds;
                if (elapsed > 0 && previous > 0)
                {
                    account.UsedSeconds = Math.Min(account.EarnedSeconds, account.UsedSeconds + elapsed);
                    _isDirty = true;
                }
            }

            account.LastChargeUtc = now;
            return (previous, account.RemainingSeconds, isFirstCharge);
        }
    }

    /// <summary>
    /// Saves the state to the file, when something changed and the last save is old enough.
    /// </summary>
    /// <param name="gameContext">The game context.</param>
    /// <param name="stateFile">The path of the state file.</param>
    public static void SaveIfDue(IGameContext gameContext, string stateFile)
    {
        string json;
        lock (SyncRoot)
        {
            if (!_isDirty || DateTime.UtcNow - _lastSaveUtc < SaveInterval)
            {
                return;
            }

            _isDirty = false;
            _lastSaveUtc = DateTime.UtcNow;
            var today = GetServerDay(gameContext);
            var entries = Accounts
                .Where(pair => pair.Value.Day == today && pair.Value.EarnedSeconds > 0)
                .Select(pair => new StoredAccountTime
                {
                    LoginName = pair.Key,
                    Day = pair.Value.Day,
                    EarnedSeconds = pair.Value.EarnedSeconds,
                    UsedSeconds = pair.Value.UsedSeconds,
                })
                .ToList();
            json = JsonSerializer.Serialize(entries);
        }

        try
        {
            var fullPath = Path.GetFullPath(stateFile);
            if (Path.GetDirectoryName(fullPath) is { Length: > 0 } directory)
            {
                Directory.CreateDirectory(directory);
            }

            var temporaryPath = fullPath + ".tmp";
            File.WriteAllText(temporaryPath, json);
            File.Move(temporaryPath, fullPath, true);
            _saveFailureLogged = false;
        }
        catch (Exception ex) when (!_saveFailureLogged)
        {
            _saveFailureLogged = true;
            GetLogger(gameContext).LogWarning(ex, "LaCleon: no se pudo guardar el tiempo en {File}. El evento sigue, pero el saldo de hoy se pierde si se reinicia.", stateFile);
        }
        catch (Exception)
        {
            // Ya se avisó una vez; no se llena el log.
        }
    }

    private static ILogger GetLogger(IGameContext gameContext) => gameContext.LoggerFactory.CreateLogger(typeof(LaCleonTimeService).FullName!);

    private static AccountTime? GetAccountOfToday(IGameContext gameContext, string loginName, bool createIfMissing)
    {
        var today = GetServerDay(gameContext);
        if (Accounts.TryGetValue(loginName, out var account))
        {
            if (account.Day != today)
            {
                // Pasaron las 00:00: el saldo no se acumula.
                account.Day = today;
                account.EarnedSeconds = 0;
                account.UsedSeconds = 0;
                _isDirty = true;
            }

            return account;
        }

        if (!createIfMissing)
        {
            return null;
        }

        account = new AccountTime { Day = today };
        Accounts[loginName] = account;
        return account;
    }

    private static void EnsureLoaded(IGameContext gameContext, string stateFile)
    {
        if (_isLoaded)
        {
            return;
        }

        _isLoaded = true;
        try
        {
            var fullPath = Path.GetFullPath(stateFile);
            if (!File.Exists(fullPath))
            {
                return;
            }

            var entries = JsonSerializer.Deserialize<List<StoredAccountTime>>(File.ReadAllText(fullPath));
            if (entries is null)
            {
                return;
            }

            foreach (var entry in entries)
            {
                if (string.IsNullOrEmpty(entry.LoginName) || string.IsNullOrEmpty(entry.Day))
                {
                    continue;
                }

                Accounts[entry.LoginName] = new AccountTime
                {
                    Day = entry.Day,
                    EarnedSeconds = Math.Max(0, entry.EarnedSeconds),
                    UsedSeconds = Math.Max(0, entry.UsedSeconds),
                };
            }

            GetLogger(gameContext).LogInformation("LaCleon: tiempo de {Count} cuentas leído de {File}.", Accounts.Count, stateFile);
        }
        catch (Exception ex)
        {
            GetLogger(gameContext).LogWarning(ex, "LaCleon: no se pudo leer {File}. Se arranca con el saldo en cero.", stateFile);
        }
    }

    private sealed class AccountTime
    {
        public string Day { get; set; } = string.Empty;

        public int EarnedSeconds { get; set; }

        public double UsedSeconds { get; set; }

        public DateTime? LastChargeUtc { get; set; }

        public double RemainingSeconds => Math.Max(0, this.EarnedSeconds - this.UsedSeconds);
    }

    private sealed class StoredAccountTime
    {
        public string LoginName { get; set; } = string.Empty;

        public string Day { get; set; } = string.Empty;

        public int EarnedSeconds { get; set; }

        public double UsedSeconds { get; set; }
    }
}
