using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Utils;
using System.Text.Json.Serialization;

namespace CS2AutoGiveC4inBombZone;

public class CS2AutoGiveC4inBombZoneConfig : BasePluginConfig
{
    [JsonPropertyName("css_autogivec4_enable")]
    public bool Enabled { get; set; } = true;
    
    [JsonPropertyName("css_autogivec4_nogive_ground")]
    public bool NoGiveIfOnGround { get; set; } = false;
    
    [JsonPropertyName("css_autogivec4_check_interval")]
    public float CheckInterval { get; set; } = 1.0f;
    
    [JsonPropertyName("css_autogivec4_debug")]
    public bool Debug { get; set; } = false;
}

[MinimumApiVersion(362)]
public class CS2AutoGiveC4inBombZone : BasePlugin, IPluginConfig<CS2AutoGiveC4inBombZoneConfig>
{
    public override string ModuleName => "CS2 AutoGiveC4inBombZone";
    public override string ModuleVersion => "1.5";
    public override string ModuleAuthor => "Ported by le1t1337 + AI DeepSeek. Code logic by wS";

    private bool _isEnabled = true;
    private int _count = 0;
    private Vector[] _mins = new Vector[3];
    private Vector[] _maxs = new Vector[3];
    private bool _bombZonesLoaded = false;
    
    public required CS2AutoGiveC4inBombZoneConfig Config { get; set; }
    
    public void OnConfigParsed(CS2AutoGiveC4inBombZoneConfig config)
    {
        Config = config;
        
        Console.WriteLine($"[AutoGive C4] Конфиг загружен:");
        Console.WriteLine($"[AutoGive C4]   Включено: {Config.Enabled}");
        Console.WriteLine($"[AutoGive C4]   Не выдавать если на земле: {Config.NoGiveIfOnGround}");
        Console.WriteLine($"[AutoGive C4]   Интервал проверки: {Config.CheckInterval} сек.");
        Console.WriteLine($"[AutoGive C4]   Режим отладки: {Config.Debug}");
    }

    public override void Load(bool hotReload)
    {
        // Инициализация массивов
        for (int i = 0; i < 3; i++)
        {
            _mins[i] = new Vector(0, 0, 0);
            _maxs[i] = new Vector(0, 0, 0);
        }

        // Регистрируем команды только для консоли сервера
        AddCommand("css_autogivec4_help", "Show plugin help", OnHelpCommand);
        AddCommand("css_autogivec4_settings", "Show current settings", OnSettingsCommand);
        AddCommand("css_autogivec4_reload", "Reload plugin configuration", OnReloadCommand);
        
        // Выводим информацию о конфигурации
        PrintConVarInfo();
        
        // Регистрируем обработчик тиков - отложенная инициализация
        RegisterListener<Listeners.OnTick>(OnGameTick);
        
        // Для hot reload сразу пытаемся загрузить зоны
        if (hotReload)
        {
            AddTimer(2.0f, () => {
                TryLoadBombZones();
            });
        }
        
        // Регистрируем обработчик старта карты
        RegisterListener<Listeners.OnMapStart>(OnMapStart);
        
        Console.WriteLine($"[AutoGive C4] Плагин загружен. Ожидание инициализации...");
    }
    
    private void OnMapStart(string mapName)
    {
        // Сбрасываем флаг при смене карты
        _bombZonesLoaded = false;
        _count = 0;
        
        // Ждем 3 секунды перед поиском зон, чтобы Entity System успела инициализироваться
        AddTimer(3.0f, () => {
            TryLoadBombZones();
        });
        
        Console.WriteLine($"[AutoGive C4] Карта {mapName} загружается, поиск бомбовых зон через 3 секунды...");
    }

    private void PrintConVarInfo()
    {
        Console.WriteLine("===============================================");
        Console.WriteLine("[AutoGive C4] Plugin successfully loaded!");
        Console.WriteLine($"[AutoGive C4] Version: {ModuleVersion}");
        Console.WriteLine($"[AutoGive C4] Minimum API Version: 362");
        Console.WriteLine("[AutoGive C4] Configuration file created automatically!");
        Console.WriteLine("[AutoGive C4] Current settings:");
        Console.WriteLine($"[AutoGive C4]   css_autogivec4_enable = {Config.Enabled}");
        Console.WriteLine($"[AutoGive C4]   css_autogivec4_nogive_ground = {Config.NoGiveIfOnGround}");
        Console.WriteLine($"[AutoGive C4]   css_autogivec4_check_interval = {Config.CheckInterval}");
        Console.WriteLine($"[AutoGive C4]   css_autogivec4_debug = {Config.Debug}");
        Console.WriteLine("[AutoGive C4] Console commands:");
        Console.WriteLine("[AutoGive C4]   css_autogivec4_help - Show plugin help");
        Console.WriteLine("[AutoGive C4]   css_autogivec4_settings - Show current settings");
        Console.WriteLine("[AutoGive C4]   css_autogivec4_reload - Reload configuration");
        Console.WriteLine("===============================================");
    }

    [GameEventHandler(HookMode.Post)]
    public HookResult OnRoundStart(EventRoundStart @event, GameEventInfo info)
    {
        _isEnabled = true;
        if (Config.Debug)
            Console.WriteLine($"[AutoGive C4] Раунд начался, плагин включен");
        return HookResult.Continue;
    }

    [GameEventHandler(HookMode.Post)]
    public HookResult OnBombBeginPlant(EventBombBeginplant @event, GameEventInfo info)
    {
        _isEnabled = false;
        if (Config.Debug)
            Console.WriteLine($"[AutoGive C4] Начата установка бомбы, плагин выключен");
        return HookResult.Continue;
    }

    [GameEventHandler(HookMode.Post)]
    public HookResult OnBombPlanted(EventBombPlanted @event, GameEventInfo info)
    {
        _isEnabled = false;
        if (Config.Debug)
            Console.WriteLine($"[AutoGive C4] Бомба установлена, плагин выключен");
        return HookResult.Continue;
    }

    [GameEventHandler(HookMode.Post)]
    public HookResult OnEnterBombZone(EventEnterBombzone @event, GameEventInfo info)
    {
        if (!Config.Enabled || !_isEnabled || !_bombZonesLoaded) 
            return HookResult.Continue;

        var client = @event.Userid;
        if (client == null || !client.IsValid || !client.PawnIsAlive) 
            return HookResult.Continue;

        if (client.TeamNum == (int)CsTeam.Terrorist)
        {
            if (Config.Debug)
                Console.WriteLine($"[AutoGive C4] Игрок {client.PlayerName} вошел в бомбовую зону (событие)");
            GiveC4(client);
        }
        
        return HookResult.Continue;
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
            Console.WriteLine($"[AutoGive C4] Ошибка при загрузке бомбовых зон: {ex.Message}");
            
            // Пробуем еще раз через 5 секунд
            if (!_bombZonesLoaded)
            {
                AddTimer(5.0f, () => {
                    Console.WriteLine($"[AutoGive C4] Повторная попытка загрузки бомбовых зон...");
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
            // Ищем все бомбовые зоны на карте
            var bombZones = Utilities.FindAllEntitiesByDesignerName<CBaseEntity>("func_bomb_target");
            
            foreach (var zone in bombZones)
            {
                if (zone == null || !zone.IsValid)
                    continue;

                try
                {
                    var origin = zone.AbsOrigin;
                    if (origin == null) continue;
                    
                    // Используем фиксированный размер зоны
                    var mins = new Vector(-256, -256, -64);
                    var maxs = new Vector(256, 256, 64);
                    
                    // Создаем новые векторы для вычислений
                    var calculatedMins = new Vector(
                        origin.X + mins.X,
                        origin.Y + mins.Y,
                        origin.Z + mins.Z
                    );
                    
                    var calculatedMaxs = new Vector(
                        origin.X + maxs.X,
                        origin.Y + maxs.Y,
                        origin.Z + maxs.Z
                    );
                    
                    if (_count < _mins.Length && _count < _maxs.Length)
                    {
                        _mins[_count] = calculatedMins;
                        _maxs[_count] = calculatedMaxs;
                        
                        if (Config.Debug)
                            Console.WriteLine($"[AutoGive C4] Найдена бомбовая зона {_count + 1} в позиции {origin}");
                        else
                            Console.WriteLine($"[AutoGive C4] Найдена бомбовая зона {_count + 1}");
                        
                        _count++;
                    }
                    
                    if (_count >= 3)
                        break;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[AutoGive C4] Ошибка при обработке зоны: {ex.Message}");
                }
            }
            
            Console.WriteLine($"[AutoGive C4] Всего найдено зон бомбы: {_count}");
            
            if (_count == 0)
            {
                Console.WriteLine($"[AutoGive C4] Внимание: бомбовые зоны не найдены! Плагин может не работать.");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[AutoGive C4] Критическая ошибка в GetBombZones: {ex.Message}");
            throw; // Перебрасываем исключение для TryLoadBombZones
        }
    }

    // Таймер для проверки игроков
    private DateTime _lastCheckTime = DateTime.MinValue;
    
    private void OnGameTick()
    {
        if (!Config.Enabled || !_isEnabled || !_bombZonesLoaded || _count == 0)
            return;

        // Проверяем игроков с указанным интервалом
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
                    // Проверяем что векторы инициализированы
                    if (_mins[x] == null || _maxs[x] == null)
                        continue;
                        
                    if (origin.X >= _mins[x].X && origin.X <= _maxs[x].X &&
                        origin.Y >= _mins[x].Y && origin.Y <= _maxs[x].Y &&
                        origin.Z >= _mins[x].Z && origin.Z <= _maxs[x].Z)
                    {
                        if (Config.Debug)
                            Console.WriteLine($"[AutoGive C4] Игрок {player.PlayerName} в бомбовой зоне {x + 1}");
                        GiveC4(player);
                        break; // Не проверяем другие зоны для этого игрока
                    }
                }
            }
        }
        catch (Exception ex)
        {
            // Игнорируем ошибки тиков, чтобы не крашить плагин
            if (Config.Debug)
                Console.WriteLine($"[AutoGive C4] Ошибка в OnGameTick: {ex.Message}");
        }
    }

    private void GiveC4(CCSPlayerController player)
    {
        if (player == null || !player.IsValid) 
            return;

        var pawn = player.PlayerPawn?.Value;
        if (pawn == null || !pawn.IsValid) 
            return;

        // Проверяем, есть ли у игрока уже C4
        if (PlayerHasC4(player))
        {
            if (Config.Debug)
                Console.WriteLine($"[AutoGive C4] У игрока {player.PlayerName} уже есть C4, пропускаем выдачу");
            return;
        }

        // Проверяем C4 на земле
        if (Config.NoGiveIfOnGround)
        {
            try
            {
                var c4OnGround = Utilities.FindAllEntitiesByDesignerName<CBaseEntity>("weapon_c4")
                    .FirstOrDefault(w => w != null && w.IsValid);
                
                if (c4OnGround != null)
                {
                    // Проверяем, что C4 не принадлежит игроку
                    var owner = c4OnGround.OwnerEntity;
                    if (owner == null || !owner.IsValid)
                    {
                        if (Config.Debug)
                            Console.WriteLine($"[AutoGive C4] C4 найдена на земле, не выдаем новую игроку {player.PlayerName}");
                        else
                            Console.WriteLine($"[AutoGive C4] C4 на земле, выдача отменена");
                        return;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[AutoGive C4] Ошибка при проверке C4 на земле: {ex.Message}");
            }
        }

        // Удаляем существующие C4 на карте (кроме тех, что у игроков)
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
                        // Используем безопасное удаление
                        c4.Remove();
                        if (Config.Debug)
                            Console.WriteLine($"[AutoGive C4] Удалена C4 с земли перед выдачей игроку {player.PlayerName}");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[AutoGive C4] Ошибка при удалении C4 с земли: {ex.Message}");
        }

        // Выдаем C4 игроку
        try
        {
            player.GiveNamedItem("weapon_c4");
            Console.WriteLine($"[AutoGive C4] Выдана C4 игроку {player.PlayerName}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[AutoGive C4] Ошибка при выдаче C4 игроку {player.PlayerName}: {ex.Message}");
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
                if (!weaponHandle.IsValid) 
                    continue;

                var weapon = weaponHandle.Value;
                if (weapon == null) 
                    continue;

                var designerName = weapon.DesignerName;
                if (!string.IsNullOrEmpty(designerName) && designerName.Contains("weapon_c4"))
                {
                    return true;
                }
            }
        }
        return false;
    }

    private void OnHelpCommand(CCSPlayerController? player, CommandInfo command)
    {
        Console.WriteLine($"[AutoGive C4] ================ Plugin Help ================");
        Console.WriteLine($"[AutoGive C4] Plugin: {ModuleName}");
        Console.WriteLine($"[AutoGive C4] Version: {ModuleVersion}");
        Console.WriteLine($"[AutoGive C4] Author: {ModuleAuthor}");
        Console.WriteLine($"[AutoGive C4] Description: Automatically gives C4 to terrorists in bomb zones");
        Console.WriteLine($"[AutoGive C4] ");
        Console.WriteLine($"[AutoGive C4] Console commands:");
        Console.WriteLine($"[AutoGive C4]   css_autogivec4_help - Show this help");
        Console.WriteLine($"[AutoGive C4]   css_autogivec4_settings - Show current settings");
        Console.WriteLine($"[AutoGive C4]   css_autogivec4_reload - Reload configuration");
        Console.WriteLine($"[AutoGive C4] ");
        Console.WriteLine($"[AutoGive C4] Configuration:");
        Console.WriteLine($"[AutoGive C4]   File: addons/counterstrikesharp/configs/plugins/CS2AutoGiveC4inBombZone/CS2AutoGiveC4inBombZone.json");
        Console.WriteLine($"[AutoGive C4] ");
        Console.WriteLine($"[AutoGive C4] Configuration parameters:");
        Console.WriteLine($"[AutoGive C4]   css_autogivec4_enable (true/false) - Enable/disable plugin");
        Console.WriteLine($"[AutoGive C4]   css_autogivec4_nogive_ground (true/false) - Don't give C4 if it's on ground");
        Console.WriteLine($"[AutoGive C4]   css_autogivec4_check_interval (float) - Check interval in seconds");
        Console.WriteLine($"[AutoGive C4]   css_autogivec4_debug (true/false) - Enable debug mode");
        Console.WriteLine($"[AutoGive C4] ");
        Console.WriteLine($"[AutoGive C4] Current status:");
        Console.WriteLine($"[AutoGive C4]   Zones loaded: {_bombZonesLoaded}");
        Console.WriteLine($"[AutoGive C4]   Zones found: {_count}");
        Console.WriteLine($"[AutoGive C4] ===========================================");
    }

    private void OnSettingsCommand(CCSPlayerController? player, CommandInfo command)
    {
        Console.WriteLine($"[AutoGive C4] ============== Current Settings ==============");
        Console.WriteLine($"[AutoGive C4] Plugin enabled: {Config.Enabled}");
        Console.WriteLine($"[AutoGive C4] No give if on ground: {Config.NoGiveIfOnGround}");
        Console.WriteLine($"[AutoGive C4] Check interval: {Config.CheckInterval} sec.");
        Console.WriteLine($"[AutoGive C4] Debug mode: {Config.Debug}");
        Console.WriteLine($"[AutoGive C4] Bomb zones loaded: {_bombZonesLoaded}");
        Console.WriteLine($"[AutoGive C4] Bomb zones found: {_count}");
        Console.WriteLine($"[AutoGive C4] Plugin active: {_isEnabled}");
        Console.WriteLine($"[AutoGive C4] ===========================================");
    }

    private void OnReloadCommand(CCSPlayerController? player, CommandInfo command)
    {
        Console.WriteLine($"[AutoGive C4] Reloading configuration...");
        // Конфиг автоматически перезагрузится CounterStrikeSharp
        // Загружаем зоны заново
        TryLoadBombZones();
        Console.WriteLine($"[AutoGive C4] Configuration reloaded!");
    }
}