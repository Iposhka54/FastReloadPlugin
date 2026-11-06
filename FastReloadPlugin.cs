using System.Text.Json;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Admin;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Utils;
using Microsoft.Extensions.Logging;

namespace FastReloadPlugin;

// partial — часть класса лежит в VipMenu.cs
public partial class FastReloadPlugin : BasePlugin
{
  public override string ModuleName => "FastReload";
  public override string ModuleVersion => "0.1.0";

  public override string ModuleAuthor => "Iposhka54";
  public override string ModuleDescription => "Plugin for auto-bhop and fast reload";

  private const string ConfigFileName = "config.json";
  private const float DefaultRate = 1f;

  private static readonly JsonSerializerOptions JsonOptions = new()
  {
    WriteIndented = true,
    PropertyNameCaseInsensitive = true,
    ReadCommentHandling = JsonCommentHandling.Skip,
    AllowTrailingCommas = true,
  };

  private PluginConfig _config = new();

  // значение в словаре — явный выбор игрока через команду; если записи нет, берётся EnabledByDefault
  private readonly Dictionary<ulong, bool> _bhopEnabled = new();
  private readonly Dictionary<ulong, bool> _fastReloadEnabled = new();

  // какой NextPrimaryAttackTick мы уже выставили — чтобы не сокращать один и тот же таймер повторно
  private readonly Dictionary<ulong, int> _shortenedTo = new();

  public override void Load(bool hotReload)
  {
    LoadConfig();

    // префикс css_ нужен, чтобы команда работала из чата как !bhop
    AddCommand("css_bhop", "Toggle auto-bhop", OnBhopCommand);
    AddCommand("css_fastreload", "Toggle fast reload", OnFastReloadCommand);
    AddCommand("css_fastreload_config", "Reload FastReload config.json", OnReloadConfigCommand);
    RegisterVipMenu();
    RegisterNoSpread();

    RegisterListener<Listeners.OnTick>(OnTickForBhop);
    RegisterListener<Listeners.OnTick>(OnTickForReload);
  }

  private void LoadConfig()
  {
    var path = Path.Combine(ModuleDirectory, ConfigFileName);

    if (!File.Exists(path))
    {
      _config = new PluginConfig();
      File.WriteAllText(path, JsonSerializer.Serialize(_config, JsonOptions));
      Logger.LogInformation("Создан {Path} со значениями по умолчанию", path);
      return;
    }

    try
    {
      _config = JsonSerializer.Deserialize<PluginConfig>(File.ReadAllText(path), JsonOptions) ?? new PluginConfig();
    }
    catch (JsonException e)
    {
      Logger.LogError("Ошибка в {Path}: {Message}. Используются значения по умолчанию", path, e.Message);
      _config = new PluginConfig();
      return;
    }

    if (_config.FastReload.Rate < 1f)
    {
      Logger.LogWarning("FastReload.Rate = {Rate} меньше 1, будет использовано 1", _config.FastReload.Rate);
      _config.FastReload.Rate = 1f;
    }

    Logger.LogInformation("Конфиг загружен из {Path}", path);
  }

  private void OnReloadConfigCommand(CCSPlayerController? player, CommandInfo commandInfo)
  {
    if (player != null && !AdminManager.PlayerHasPermissions(player, "@css/root"))
    {
      commandInfo.ReplyToCommand("[FastReload] Нет доступа");
      return;
    }

    // ускорение могло быть выставлено по старым правилам — сбрасываем, тики выставят заново
    foreach (var p in Utilities.GetPlayers())
    {
      if (p.IsValid && !p.IsBot) SetActiveWeaponRate(p, DefaultRate);
    }

    LoadConfig();
    commandInfo.ReplyToCommand("[FastReload] Конфиг перечитан");
  }

  private static bool IsAllowed(CCSPlayerController player, AccessConfig feature)
  {
    if (!feature.Enabled) return false;

    var steamId = player.SteamID.ToString();
    foreach (var entry in feature.AllowedPlayers)
    {
      if (entry == "*" || entry == steamId) return true;
      if (entry.StartsWith('@') && AdminManager.PlayerHasPermissions(player, entry)) return true;
    }

    return false;
  }

  private static bool IsActive(CCSPlayerController player, Dictionary<ulong, bool> toggles, FeatureConfig feature)
  {
    return toggles.GetValueOrDefault(player.SteamID, feature.EnabledByDefault) && IsAllowed(player, feature);
  }

  // общая логика !bhop и !fastreload; возвращает новое состояние или null, если переключить нельзя
  private static bool? Toggle(CCSPlayerController player, Dictionary<ulong, bool> toggles, FeatureConfig feature, string tag)
  {
    if (!feature.Enabled)
    {
      player.PrintToChat($" {ChatColors.Red}[{tag}]{ChatColors.Default} Отключено на сервере");
      return null;
    }

    if (!IsAllowed(player, feature))
    {
      player.PrintToChat($" {ChatColors.Red}[{tag}]{ChatColors.Default} Нет доступа");
      return null;
    }

    var newState = !toggles.GetValueOrDefault(player.SteamID, feature.EnabledByDefault);
    toggles[player.SteamID] = newState;
    return newState;
  }

  private void OnBhopCommand(CCSPlayerController? player, CommandInfo commandInfo)
  {
    if (player == null || !player.IsValid) return;
    ToggleBhop(player);
  }

  private void OnFastReloadCommand(CCSPlayerController? player, CommandInfo commandInfo)
  {
    if (player == null || !player.IsValid) return;
    ToggleFastReload(player);
  }

  // вынесено из команд, чтобы то же самое вызывать из меню
  private void ToggleBhop(CCSPlayerController player)
  {
    var newState = Toggle(player, _bhopEnabled, _config.Bhop, "Bhop");
    if (newState == null) return;

    player.PrintToChat($" {ChatColors.Green}[Bhop]{(newState.Value ? " Включён" : " Выключен")}");
  }

  private void ToggleFastReload(CCSPlayerController player)
  {
    var newState = Toggle(player, _fastReloadEnabled, _config.FastReload, "FastReload");
    if (newState == null) return;

    // если выключили посреди перезарядки — не оставляем оружие ускоренным
    if (!newState.Value) SetActiveWeaponRate(player, DefaultRate);

    player.PrintToChat($" {ChatColors.Green}[FastReload]{(newState.Value ? " Включена" : " Выключена")}");
  }

  private void OnTickForBhop()
  {
    if (!_config.Bhop.Enabled) return;

    foreach (var player in Utilities.GetPlayers())
    {
      if (!player.IsValid || player.IsBot) continue;

      if (!IsActive(player, _bhopEnabled, _config.Bhop)) continue;

      var pawn = player.PlayerPawn.Value;
      if (pawn == null || !pawn.IsValid || pawn.LifeState != (byte)LifeState_t.LIFE_ALIVE) continue;

      bool onGround = (pawn.Flags & (uint)PlayerFlags.FL_ONGROUND) != 0;
      bool holdingJump = (player.Buttons & PlayerButtons.Jump) != 0;

      bool onLadder = pawn.MoveType == MoveType_t.MOVETYPE_LADDER;

      if (onGround && holdingJump && !onLadder)
      {
        pawn.AbsVelocity.Z = _config.Bhop.JumpVelocity;
      }
    }
  }

  // пришлось сделать проверку перезарядки через тики, потому что событие перезарядки(EventWeaponReload) оружие выбрасывается
  // только при ручной перезарядке, если патроны закончились в обойме, то перезарядка начинается сама, в этом случае
  // событие EventWeaponReload не выкидывается
  private void OnTickForReload()
  {
    if (!_config.FastReload.Enabled) return;

    foreach (var player in Utilities.GetPlayers())
    {
      if (!player.IsValid || player.IsBot) continue;
      if (!IsActive(player, _fastReloadEnabled, _config.FastReload)) continue;

      var weapon = player.PlayerPawn.Value?.WeaponServices?.ActiveWeapon.Value?.As<CCSWeaponBase>();
      if (weapon == null || !weapon.IsValid) continue;

      if (!weapon.InReload)
      {
        SetActiveWeaponRate(player, DefaultRate);
        continue;
      }

      SetActiveWeaponRate(player, _config.FastReload.Rate);
      ShortenAttackDelay(player);
    }
  }

  // PlaybackRate за анимацию, но когда разрешить стрельбу отвечает NextPrimaryAttackTick
  private void ShortenAttackDelay(CCSPlayerController player)
  {
    var weapon = player.PlayerPawn.Value?.WeaponServices?.ActiveWeapon.Value;
    if (weapon == null || !weapon.IsValid) return;

    if (_shortenedTo.TryGetValue(player.SteamID, out var shortened) && weapon.NextPrimaryAttackTick == shortened) return;

    int now = Server.TickCount;
    int ticksLeft = weapon.NextPrimaryAttackTick - now;

    if (ticksLeft <= 0) return;

    var rate = _config.FastReload.Rate;

    if (_config.Debug)
      player.PrintToChat($" [debug] ждать было {ticksLeft} тиков, станет {(int)Math.Ceiling(ticksLeft / rate)}");

    weapon.NextPrimaryAttackTick = now + (int)Math.Ceiling(ticksLeft / rate);
    Utilities.SetStateChanged(weapon, "CBasePlayerWeapon", "m_nNextPrimaryAttackTick");
    _shortenedTo[player.SteamID] = weapon.NextPrimaryAttackTick;

    // вторую атаку так же ускоряем
    int secondaryLeft = weapon.NextSecondaryAttackTick - now;
    if (secondaryLeft > 0)
    {
      weapon.NextSecondaryAttackTick = now + (int)Math.Ceiling(secondaryLeft / rate);
      Utilities.SetStateChanged(weapon, "CBasePlayerWeapon", "m_nNextSecondaryAttackTick");
    }
  }

  private static void SetActiveWeaponRate(CCSPlayerController player, float rate)
  {
    var pawn = player.PlayerPawn.Value;
    if (pawn == null || !pawn.IsValid) return;

    var weapon = pawn.WeaponServices?.ActiveWeapon.Value;
    if (weapon == null || !weapon.IsValid) return;

    var csWeapon = weapon.As<CCSWeaponBase>();
    if (csWeapon.WeaponActionPlaybackRate == rate) return;

    csWeapon.WeaponActionPlaybackRate = rate;
    Utilities.SetStateChanged(csWeapon, "CCSWeaponBase", "m_flWeaponActionPlaybackRate");
  }

  public override void Unload(bool hotReload)
  {
    UnhookGetInaccuracy();

    foreach (var player in Utilities.GetPlayers())
    {
      if (!player.IsValid || player.IsBot) continue;

      SetActiveWeaponRate(player, DefaultRate);
      if (IsNoSpreadActive(player)) SyncClientConVars(player, false);
    }

    _bhopEnabled.Clear();
    _fastReloadEnabled.Clear();
    _noSpreadEnabled.Clear();
    _shortenedTo.Clear();
  }
}
