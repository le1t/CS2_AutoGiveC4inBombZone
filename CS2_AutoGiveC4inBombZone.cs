using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Utils;
using Microsoft.Extensions.Logging;
using System.Globalization;
using System.Text.Json.Serialization;

namespace CS2AutoGiveC4inBombZone;

public class CS2AutoGiveC4inBombZoneConfig : BasePluginConfig
{
    /// <summary>
    /// Включить/выключить плагин (по умолчанию: 1)
    /// 0 - выключен, 1 - включен
    /// </summary>
    [JsonPropertyName("css_autogivec4_enable")]
    public int Enabled { get; set; } = 1;

    /// <summary>
    /// Не выдавать C4, если она лежит на земле (по умолчанию: 0)
    /// 0 - выдавать даже если есть C4 на земле
    /// 1 - не выдавать, пока C4 лежит на земле (чтобы не дублировать)
    /// </summary>
    [JsonPropertyName("css_autogivec4_nogive_ground")]
    public int NoGiveIfOnGround { get; set; } = 0;

    /// <summary>
    /// Интервал проверки положения игроков в бомбовых зонах (секунды) (по умолчанию: 1.0)
    /// Диапазон: 0.1 - 10.0
    /// </summary>
    [JsonPropertyName("css_autogivec4_check_interval")]
    public float CheckInterval { get; set; } = 1.0f;

    /// <summary>
    /// Режим отладки (по умолчанию: 0)
    /// 0 - выключен, 1 - включен (выводит больше отладочной информации)
    /// </summary>
    [JsonPropertyName("css_autogivec4_debug")]
    public int Debug { get; set; } = 0;

    /// <summary>
    /// Уровень логирования (по умолчанию: 4 - Error)
    /// 0 - Trace
    /// 1 - Debug
    /// 2 - Information
    /// 3 - Warning
    /// 4 - Error
    /// 5 - Critical
    /// </summary>
    [JsonPropertyName("css_autogivec4_loglevel")]
    public int LogLevel { get; set; } = 4;
}

[MinimumApiVersion(362)]
public class CS2AutoGiveC4inBombZone : BasePlugin, IPluginConfig<CS2AutoGiveC4inBombZoneConfig>
{
    public override string ModuleName => "CS2 AutoGiveC4inBombZone";
    public override string ModuleVersion => "1.7";
    public override string ModuleAuthor => "Fixed by le1t1337 + AI DeepSeek. Code logic by wS";

    private bool _isEnabled = true;
    private int _count = 0;
    private Vector[] _mins = new Vector[3];
    private Vector[] _maxs = new Vector[3];
    private bool _bombZonesLoaded = false;
    private DateTime _lastCheckTime = DateTime.MinValue;

    public required CS2AutoGiveC4inBombZoneConfig Config { get; set; }

    public void OnConfigParsed(CS2AutoGiveC4inBombZoneConfig config)
    {
        config.Enabled = Math.Clamp(config.Enabled, 0, 1);
        config.NoGiveIfOnGround = Math.Clamp(config.NoGiveIfOnGround, 0, 1);
        config.CheckInterval = Math.Clamp(config.CheckInterval, 0.1f, 10.0f);
        config.Debug = Math.Clamp(config.Debug, 0, 1);
        config.LogLevel = Math.Clamp(config.LogLevel, 0, 5);
        Config = config;

        Log(LogLevel.Information, "Конфиг загружен:");
        Log(LogLevel.Information, $"  Включено: {Config.Enabled}");
        Log(LogLevel.Information, $"  Не выдавать если на земле: {Config.NoGiveIfOnGround}");
        Log(LogLevel.Information, $"  Интервал проверки: {Config.CheckInterval} сек.");
        Log(LogLevel.Information, $"  Режим отладки: {Config.Debug}");
        Log(LogLevel.Information, $"  Уровень логирования: {Config.LogLevel}");
    }

    private void Log(LogLevel level, string message)
    {
        if ((int)level >= Config.LogLevel)
            Logger.Log(level, "[AutoGiveC4] {Message}", message);
    }

    public override void Load(bool hotReload)
    {
        // Удаляем старый конфиг из папки plugins, если он существует
        string oldConfigPath = Path.Combine(Server.GameDirectory, "counterstrikesharp", "plugins", "CS2AutoGiveC4inBombZone.json");
        if (File.Exists(oldConfigPath))
        {
            File.Delete(oldConfigPath);
            Log(LogLevel.Information, "Старый конфиг удалён из папки plugins.");
        }

        for (int i = 0; i < 3; i++)
        {
            _mins[i] = new Vector(0, 0, 0);
            _maxs[i] = new Vector(0, 0, 0);
        }

        AddCommand("css_autogivec4_help", "Показать справку", OnHelpCommand);
        AddCommand("css_autogivec4_settings", "Показать текущие настройки", OnSettingsCommand);
        AddCommand("css_autogivec4_test", "Тестовая команда", OnTestCommand);
        AddCommand("css_autogivec4_reload", "Перезагрузить конфигурацию", OnReloadCommand);

        AddCommand("css_autogivec4_setenabled", "Включить/выключить плагин (0/1) (по умолчанию: 1)", OnSetEnabledCommand);
        AddCommand("css_autogivec4_setnogiveground", "Не выдавать если на земле (0/1) (по умолчанию: 0)", OnSetNoGiveGroundCommand);
        AddCommand("css_autogivec4_setcheckinterval", "Интервал проверки в секундах (0.1-10.0) (по умолчанию: 1.0)", OnSetCheckIntervalCommand);
        AddCommand("css_autogivec4_setdebug", "Режим отладки (0/1) (по умолчанию: 0)", OnSetDebugCommand);
        AddCommand("css_autogivec4_setloglevel", "Уровень логирования (0-5) (по умолчанию: 4)", OnSetLogLevelCommand);

        RegisterListener<Listeners.OnTick>(OnGameTick);
        RegisterListener<Listeners.OnMapStart>(OnMapStart);
        RegisterEventHandler<EventRoundStart>(OnRoundStart);
        RegisterEventHandler<EventBombBeginplant>(OnBombBeginPlant);
        RegisterEventHandler<EventBombPlanted>(OnBombPlanted);
        RegisterEventHandler<EventEnterBombzone>(OnEnterBombZone);

        PrintInfo();

        if (hotReload)
        {
            AddTimer(2.0f, TryLoadBombZones);
        }
    }

    private void PrintInfo()
    {
        Log(LogLevel.Information, "===============================================");
        Log(LogLevel.Information, $"Плагин {ModuleName} версии {ModuleVersion} успешно загружен!");
        Log(LogLevel.Information, $"Автор: {ModuleAuthor}");
        Log(LogLevel.Information, "Текущие настройки:");
        Log(LogLevel.Information, $"  css_autogivec4_enable = {Config.Enabled}");
        Log(LogLevel.Information, $"  css_autogivec4_nogive_ground = {Config.NoGiveIfOnGround}");
        Log(LogLevel.Information, $"  css_autogivec4_check_interval = {Config.CheckInterval}");
        Log(LogLevel.Information, $"  css_autogivec4_debug = {Config.Debug}");
        Log(LogLevel.Information, $"  css_autogivec4_loglevel = {Config.LogLevel}");
        Log(LogLevel.Information, "Команды:");
        Log(LogLevel.Information, "  css_autogivec4_help - справка");
        Log(LogLevel.Information, "  css_autogivec4_settings - настройки");
        Log(LogLevel.Information, "  css_autogivec4_test - тест");
        Log(LogLevel.Information, "  css_autogivec4_reload - перезагрузить конфиг");
        Log(LogLevel.Information, "  css_autogivec4_setenabled <0/1>");
        Log(LogLevel.Information, "  css_autogivec4_setnogiveground <0/1>");
        Log(LogLevel.Information, "  css_autogivec4_setcheckinterval <сек>");
        Log(LogLevel.Information, "  css_autogivec4_setdebug <0/1>");
        Log(LogLevel.Information, "  css_autogivec4_setloglevel <0-5>");
        Log(LogLevel.Information, "===============================================");
    }

    private HookResult OnRoundStart(EventRoundStart @event, GameEventInfo info)
    {
        _isEnabled = true;
        if (Config.Debug == 1)
            Log(LogLevel.Debug, "Раунд начался, плагин включён");
        return HookResult.Continue;
    }

    private HookResult OnBombBeginPlant(EventBombBeginplant @event, GameEventInfo info)
    {
        _isEnabled = false;
        if (Config.Debug == 1)
            Log(LogLevel.Debug, "Начата установка бомбы, плагин выключен");
        return HookResult.Continue;
    }

    private HookResult OnBombPlanted(EventBombPlanted @event, GameEventInfo info)
    {
        _isEnabled = false;
        if (Config.Debug == 1)
            Log(LogLevel.Debug, "Бомба установлена, плагин выключен");
        return HookResult.Continue;
    }

    private HookResult OnEnterBombZone(EventEnterBombzone @event, GameEventInfo info)
    {
        if (Config.Enabled == 0 || !_isEnabled || !_bombZonesLoaded)
            return HookResult.Continue;

        var client = @event.Userid;
        if (client == null || !client.IsValid || !client.PawnIsAlive)
            return HookResult.Continue;

        if (client.TeamNum == (int)CsTeam.Terrorist)
        {
            if (Config.Debug == 1)
                Log(LogLevel.Debug, $"Игрок {client.PlayerName} вошёл в бомбовую зону (событие)");
            GiveC4(client);
        }
        return HookResult.Continue;
    }

    private void OnMapStart(string mapName)
    {
        _bombZonesLoaded = false;
        _count = 0;
        AddTimer(3.0f, TryLoadBombZones);
        Log(LogLevel.Information, $"Карта {mapName} загружается, поиск зон через 3 секунды...");
    }

    private void TryLoadBombZones()
    {
        try
        {
            GetBombZones();
            _bombZonesLoaded = true;
        }
        catch (Exception ex)
        {
            Log(LogLevel.Error, $"Ошибка при загрузке бомбовых зон: {ex.Message}");
            if (!_bombZonesLoaded)
            {
                AddTimer(5.0f, () =>
                {
                    Log(LogLevel.Information, "Повторная попытка загрузки зон...");
                    TryLoadBombZones();
                });
            }
        }
    }

    private void GetBombZones()
    {
        _count = 0;
        try
        {
            var bombZones = Utilities.FindAllEntitiesByDesignerName<CBaseEntity>("func_bomb_target");
            foreach (var zone in bombZones)
            {
                if (zone == null || !zone.IsValid)
                    continue;

                var origin = zone.AbsOrigin;
                if (origin == null) continue;

                var mins = new Vector(-256, -256, -64);
                var maxs = new Vector(256, 256, 64);

                var calculatedMins = new Vector(origin.X + mins.X, origin.Y + mins.Y, origin.Z + mins.Z);
                var calculatedMaxs = new Vector(origin.X + maxs.X, origin.Y + maxs.Y, origin.Z + maxs.Z);

                if (_count < _mins.Length && _count < _maxs.Length)
                {
                    _mins[_count] = calculatedMins;
                    _maxs[_count] = calculatedMaxs;

                    if (Config.Debug == 1)
                        Log(LogLevel.Debug, $"Найдена зона {_count + 1} в позиции {origin}");

                    _count++;
                }

                if (_count >= 3) break;
            }

            Log(LogLevel.Information, $"Всего найдено зон: {_count}");
            if (_count == 0)
                Log(LogLevel.Warning, "Внимание: бомбовые зоны не найдены! Плагин может не работать.");
        }
        catch (Exception ex)
        {
            Log(LogLevel.Error, $"Критическая ошибка в GetBombZones: {ex.Message}");
            throw;
        }
    }

    private void OnGameTick()
    {
        if (Config.Enabled == 0 || !_isEnabled || !_bombZonesLoaded || _count == 0)
            return;

        var now = DateTime.Now;
        if ((now - _lastCheckTime).TotalSeconds < Config.CheckInterval)
            return;

        _lastCheckTime = now;

        try
        {
            foreach (var player in Utilities.GetPlayers())
            {
                if (player == null || !player.IsValid || !player.PawnIsAlive)
                    continue;

                if (player.TeamNum != (int)CsTeam.Terrorist)
                    continue;

                var pawn = player.PlayerPawn?.Value;
                if (pawn == null || !pawn.IsValid)
                    continue;

                var origin = pawn.AbsOrigin;
                if (origin == null) continue;

                for (int x = 0; x < _count; x++)
                {
                    if (_mins[x] == null || _maxs[x] == null)
                        continue;

                    if (origin.X >= _mins[x].X && origin.X <= _maxs[x].X &&
                        origin.Y >= _mins[x].Y && origin.Y <= _maxs[x].Y &&
                        origin.Z >= _mins[x].Z && origin.Z <= _maxs[x].Z)
                    {
                        if (Config.Debug == 1)
                            Log(LogLevel.Debug, $"Игрок {player.PlayerName} в зоне {x + 1}");
                        GiveC4(player);
                        break;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            if (Config.Debug == 1)
                Log(LogLevel.Debug, $"Ошибка в OnGameTick: {ex.Message}");
        }
    }

    private void GiveC4(CCSPlayerController player)
    {
        if (player == null || !player.IsValid)
            return;

        var pawn = player.PlayerPawn?.Value;
        if (pawn == null || !pawn.IsValid)
            return;

        if (PlayerHasC4(player))
        {
            if (Config.Debug == 1)
                Log(LogLevel.Debug, $"У игрока {player.PlayerName} уже есть C4, пропускаем");
            return;
        }

        if (Config.NoGiveIfOnGround == 1)
        {
            try
            {
                var c4OnGround = Utilities.FindAllEntitiesByDesignerName<CBaseEntity>("weapon_c4")
                    .FirstOrDefault(w => w != null && w.IsValid);

                if (c4OnGround != null)
                {
                    var owner = c4OnGround.OwnerEntity;
                    if (owner == null || !owner.IsValid)
                    {
                        if (Config.Debug == 1)
                            Log(LogLevel.Debug, $"C4 на земле, не выдаём игроку {player.PlayerName}");
                        else
                            Log(LogLevel.Information, "C4 на земле, выдача отменена");
                        return;
                    }
                }
            }
            catch (Exception ex)
            {
                Log(LogLevel.Error, $"Ошибка при проверке C4 на земле: {ex.Message}");
            }
        }

        try
        {
            var allC4 = Utilities.FindAllEntitiesByDesignerName<CBaseEntity>("weapon_c4");
            foreach (var c4 in allC4)
            {
                if (c4 != null && c4.IsValid)
                {
                    var owner = c4.OwnerEntity;
                    if (owner == null || !owner.IsValid)
                    {
                        c4.Remove();
                        if (Config.Debug == 1)
                            Log(LogLevel.Debug, $"Удалена C4 с земли перед выдачей");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Log(LogLevel.Error, $"Ошибка при удалении C4 с земли: {ex.Message}");
        }

        try
        {
            player.GiveNamedItem("weapon_c4");
            Log(LogLevel.Information, $"Выдана C4 игроку {player.PlayerName}");
        }
        catch (Exception ex)
        {
            Log(LogLevel.Error, $"Ошибка при выдаче C4 игроку {player.PlayerName}: {ex.Message}");
        }
    }

    private bool PlayerHasC4(CCSPlayerController player)
    {
        var pawn = player.PlayerPawn?.Value;
        if (pawn == null || !pawn.IsValid)
            return false;

        var weapons = pawn.WeaponServices?.MyWeapons;
        if (weapons != null)
        {
            foreach (var weaponHandle in weapons)
            {
                if (!weaponHandle.IsValid) continue;
                var weapon = weaponHandle.Value;
                if (weapon == null) continue;
                if (!string.IsNullOrEmpty(weapon.DesignerName) && weapon.DesignerName.Contains("weapon_c4"))
                    return true;
            }
        }
        return false;
    }

    private void SaveConfig()
    {
        try
        {
            string configPath = Path.Combine(Server.GameDirectory, "counterstrikesharp", "configs", "plugins", "CS2AutoGiveC4inBombZone", "CS2AutoGiveC4inBombZone.json");
            Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);
            var options = new System.Text.Json.JsonSerializerOptions { WriteIndented = true };
            string json = System.Text.Json.JsonSerializer.Serialize(Config, options);
            File.WriteAllText(configPath, json);
            Log(LogLevel.Information, "Конфигурация сохранена.");
        }
        catch (Exception ex)
        {
            Log(LogLevel.Error, $"Ошибка сохранения конфига: {ex.Message}");
        }
    }

    // --- Команды ---

    private void OnHelpCommand(CCSPlayerController? player, CommandInfo command)
    {
        string help = $"""
            ================================================
            СПРАВКА ПО ПЛАГИНУ {ModuleName} v{ModuleVersion}
            ================================================
            ОПИСАНИЕ:
              Автоматически выдаёт C4 террористам, находящимся в бомбовых зонах.
              Работает как по событию входа в зону, так и по периодической проверке.
              Поддерживает ботов.

            КОМАНДЫ (только консоль):
              css_autogivec4_help                       - эта справка
              css_autogivec4_settings                    - текущие настройки
              css_autogivec4_test                        - тест (пересканировать зоны и проверить игроков)
              css_autogivec4_reload                       - перезагрузить конфиг

              css_autogivec4_setenabled <0/1>             - вкл/выкл плагин (1)
              css_autogivec4_setnogiveground <0/1>        - не выдавать, если C4 на земле (0)
              css_autogivec4_setcheckinterval <сек>       - интервал проверки (1.0)
              css_autogivec4_setdebug <0/1>                - режим отладки (0)
              css_autogivec4_setloglevel <0-5>             - уровень логов (4)

            ПРИМЕРЫ:
              css_autogivec4_setenabled 1
              css_autogivec4_setcheckinterval 2.5
              css_autogivec4_setloglevel 2
            ================================================
            """;
        command.ReplyToCommand(help);
    }

    private void OnSettingsCommand(CCSPlayerController? player, CommandInfo command)
    {
        string status = _isEnabled ? "активен" : "приостановлен (идёт установка бомбы)";
        string settings = $"""
            ================================================
            ТЕКУЩИЕ НАСТРОЙКИ {ModuleName} v{ModuleVersion}
            ================================================
            Плагин включен: {Config.Enabled}
            Не выдавать при C4 на земле: {Config.NoGiveIfOnGround}
            Интервал проверки: {Config.CheckInterval.ToString(CultureInfo.InvariantCulture)} сек.
            Режим отладки: {Config.Debug}
            Уровень логирования: {Config.LogLevel} (0-Trace,1-Debug,2-Info,3-Warning,4-Error,5-Critical)
            ------------------------------------------------
            Статистика:
              Зоны загружены: {_bombZonesLoaded}
              Зон найдено: {_count}
              Состояние плагина в раунде: {status}
            ================================================
            """;
        command.ReplyToCommand(settings);
    }

    private void OnTestCommand(CCSPlayerController? player, CommandInfo command)
    {
        command.ReplyToCommand("[AutoGiveC4] Тестовая команда: принудительное сканирование зон и проверка игроков.");
        TryLoadBombZones();

        foreach (var p in Utilities.GetPlayers())
        {
            if (p == null || !p.IsValid || !p.PawnIsAlive || p.TeamNum != (int)CsTeam.Terrorist)
                continue;

            var pawn = p.PlayerPawn?.Value;
            if (pawn?.AbsOrigin == null) continue;

            for (int i = 0; i < _count; i++)
            {
                if (_mins[i] == null || _maxs[i] == null) continue;
                var o = pawn.AbsOrigin;
                if (o.X >= _mins[i].X && o.X <= _maxs[i].X &&
                    o.Y >= _mins[i].Y && o.Y <= _maxs[i].Y &&
                    o.Z >= _mins[i].Z && o.Z <= _maxs[i].Z)
                {
                    GiveC4(p);
                    break;
                }
            }
        }
        command.ReplyToCommand("[AutoGiveC4] Тест завершён.");
    }

    private void OnReloadCommand(CCSPlayerController? player, CommandInfo command)
    {
        command.ReplyToCommand("[AutoGiveC4] Перезагрузка конфигурации...");
        OnConfigParsed(Config);
        TryLoadBombZones();
        command.ReplyToCommand("[AutoGiveC4] Конфигурация перезагружена.");
    }

    private void OnSetEnabledCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (command.ArgCount < 2)
        {
            command.ReplyToCommand($"[AutoGiveC4] Текущее значение enabled: {Config.Enabled}. Использование: css_autogivec4_setenabled <0/1>");
            return;
        }

        string arg = command.GetArg(1);
        if (int.TryParse(arg, out int val) && (val == 0 || val == 1))
        {
            int old = Config.Enabled;
            Config.Enabled = val;
            SaveConfig();
            command.ReplyToCommand($"[AutoGiveC4] enabled изменён с {old} на {val} (по умолчанию: 1).");
            Log(LogLevel.Information, $"enabled изменён на {val} (команда от {(player?.PlayerName ?? "консоли")})");
        }
        else
        {
            command.ReplyToCommand($"[AutoGiveC4] Неверное значение. Используйте 0 или 1.");
        }
    }

    private void OnSetNoGiveGroundCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (command.ArgCount < 2)
        {
            command.ReplyToCommand($"[AutoGiveC4] Текущее значение nogive_ground: {Config.NoGiveIfOnGround}. Использование: css_autogivec4_setnogiveground <0/1>");
            return;
        }

        string arg = command.GetArg(1);
        if (int.TryParse(arg, out int val) && (val == 0 || val == 1))
        {
            int old = Config.NoGiveIfOnGround;
            Config.NoGiveIfOnGround = val;
            SaveConfig();
            command.ReplyToCommand($"[AutoGiveC4] nogive_ground изменён с {old} на {val} (по умолчанию: 0).");
            Log(LogLevel.Information, $"nogive_ground изменён на {val} (команда от {(player?.PlayerName ?? "консоли")})");
        }
        else
        {
            command.ReplyToCommand($"[AutoGiveC4] Неверное значение. Используйте 0 или 1.");
        }
    }

    private void OnSetCheckIntervalCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (command.ArgCount < 2)
        {
            command.ReplyToCommand($"[AutoGiveC4] Текущее значение check_interval: {Config.CheckInterval.ToString(CultureInfo.InvariantCulture)}. Использование: css_autogivec4_setcheckinterval <сек> (0.1 - 10.0)");
            return;
        }

        string arg = command.GetArg(1).Replace(',', '.');
        if (float.TryParse(arg, NumberStyles.Float, CultureInfo.InvariantCulture, out float val))
        {
            float old = Config.CheckInterval;
            Config.CheckInterval = Math.Clamp(val, 0.1f, 10.0f);
            SaveConfig();
            command.ReplyToCommand($"[AutoGiveC4] check_interval изменён с {old.ToString(CultureInfo.InvariantCulture)} на {Config.CheckInterval.ToString(CultureInfo.InvariantCulture)} (по умолчанию: 1.0).");
            Log(LogLevel.Information, $"check_interval изменён на {Config.CheckInterval} (команда от {(player?.PlayerName ?? "консоли")})");
        }
        else
        {
            command.ReplyToCommand($"[AutoGiveC4] Неверное значение. Используйте число с точкой, например 2.5.");
        }
    }

    private void OnSetDebugCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (command.ArgCount < 2)
        {
            command.ReplyToCommand($"[AutoGiveC4] Текущее значение debug: {Config.Debug}. Использование: css_autogivec4_setdebug <0/1>");
            return;
        }

        string arg = command.GetArg(1);
        if (int.TryParse(arg, out int val) && (val == 0 || val == 1))
        {
            int old = Config.Debug;
            Config.Debug = val;
            SaveConfig();
            command.ReplyToCommand($"[AutoGiveC4] debug изменён с {old} на {val} (по умолчанию: 0).");
            Log(LogLevel.Information, $"debug изменён на {val} (команда от {(player?.PlayerName ?? "консоли")})");
        }
        else
        {
            command.ReplyToCommand($"[AutoGiveC4] Неверное значение. Используйте 0 или 1.");
        }
    }

    private void OnSetLogLevelCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (command.ArgCount < 2)
        {
            command.ReplyToCommand($"[AutoGiveC4] Текущее значение loglevel: {Config.LogLevel} (0-Trace,1-Debug,2-Info,3-Warning,4-Error,5-Critical). Использование: css_autogivec4_setloglevel <0-5>");
            return;
        }

        string arg = command.GetArg(1);
        if (int.TryParse(arg, out int val) && val >= 0 && val <= 5)
        {
            int old = Config.LogLevel;
            Config.LogLevel = val;
            SaveConfig();
            command.ReplyToCommand($"[AutoGiveC4] loglevel изменён с {old} на {val} (по умолчанию: 4).");
            Log(LogLevel.Information, $"loglevel изменён на {val} (команда от {(player?.PlayerName ?? "консоли")})");
        }
        else
        {
            command.ReplyToCommand($"[AutoGiveC4] Неверное значение. Допустимо 0-5.");
        }
    }
}