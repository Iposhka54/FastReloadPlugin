using System.Globalization;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Cvars;
using CounterStrikeSharp.API.Modules.Memory;
using CounterStrikeSharp.API.Modules.Memory.DynamicFunctions;
using CounterStrikeSharp.API.Modules.Utils;
using Microsoft.Extensions.Logging;

namespace FastReloadPlugin;

// Анти-разброс и анти-отдача для отдельного игрока.
// Разброс от бега и прыжка движок считает при каждом выстреле в CCSWeaponBaseGun::GetInaccuracy,
// поэтому перехватываем эту функцию (как в плагине Dropshot) и возвращаем 0 нужным игрокам.
public partial class FastReloadPlugin
{
  private readonly Dictionary<ulong, bool> _noSpreadEnabled = new();

  // параметры кроме первого (оружие) нам не нужны, но их количество и типы должны совпадать с оригиналом
  private MemoryFunctionWithReturn<CCSWeaponBaseGun, IntPtr, IntPtr, float>? _getInaccuracy;

  // чтобы отладка не заспамила чат — функция вызывается очень часто
  private readonly Dictionary<ulong, int> _lastInaccuracyDebugTick = new();

  private void RegisterNoSpread()
  {
    AddCommand("css_nospread", "Toggle no-spread", OnNoSpreadCommand);
    RegisterListener<Listeners.OnTick>(OnTickForNoSpread);
    RegisterEventHandler<EventPlayerSpawn>(OnPlayerSpawnForNoSpread);

    HookGetInaccuracy();
  }

  private void HookGetInaccuracy()
  {
    var signature = _config.NoSpread.InaccuracySignature;
    if (string.IsNullOrWhiteSpace(signature)) return;

    try
    {
      _getInaccuracy = new MemoryFunctionWithReturn<CCSWeaponBaseGun, IntPtr, IntPtr, float>(signature);
      _getInaccuracy.Hook(OnGetInaccuracy, HookMode.Post);
      Logger.LogInformation("Хук GetInaccuracy установлен");
    }
    catch (Exception e)
    {
      // сигнатура не нашлась (скорее всего, обновился CS2) — работаем без хука
      _getInaccuracy = null;
      Logger.LogError("Не удалось поставить хук GetInaccuracy: {Message}", e.Message);
    }
  }

  // Обязательно снимать хук при выгрузке: иначе после hot reload движок будет вызывать
  // код уже выгруженной сборки, и сервер упадёт.
  private void UnhookGetInaccuracy()
  {
    _getInaccuracy?.Unhook(OnGetInaccuracy, HookMode.Post);
    _getInaccuracy = null;
  }

  private HookResult OnGetInaccuracy(DynamicHook hook)
  {
    // исключение, вылетевшее в нативный код движка, может уронить сервер
    try
    {
      var weaponPtr = hook.GetParam<IntPtr>(0);
      if (weaponPtr == IntPtr.Zero) return HookResult.Continue;

      var pawn = new CCSWeaponBaseGun(weaponPtr).OwnerEntity.Value?.As<CCSPlayerPawn>();
      var player = pawn?.Controller.Value?.As<CCSPlayerController>();
      if (player == null || !player.IsValid || player.IsBot) return HookResult.Continue;
      if (!IsActive(player, _noSpreadEnabled, _config.NoSpread)) return HookResult.Continue;

      if (_config.Debug && Server.TickCount - _lastInaccuracyDebugTick.GetValueOrDefault(player.SteamID) > 32)
      {
        _lastInaccuracyDebugTick[player.SteamID] = Server.TickCount;
        player.PrintToChat($" [debug] GetInaccuracy вернул {hook.GetReturn<float>():F4}, заменяем на 0");
      }

      hook.SetReturn(0f);
      return HookResult.Changed;
    }
    catch (Exception e)
    {
      Logger.LogError("Ошибка в хуке GetInaccuracy: {Message}", e.Message);
      return HookResult.Continue;
    }
  }

  // Клиент сам предсказывает выстрелы: разброс по weapon_accuracy_nospread, подброс прицела по
  // weapon_recoil_scale. Если сервер их убирает, а клиент нет — следы пуль разлетаются, а прицел
  // дёргается. Поэтому говорим клиенту этого игрока те же значения, что применяет сервер.
  private void SyncClientConVars(CCSPlayerController player, bool enabled)
  {
    if (_getInaccuracy != null)
    {
      var serverNoSpread = ConVar.Find("weapon_accuracy_nospread")?.GetPrimitiveValue<bool>() ?? false;
      player.ReplicateConVar("weapon_accuracy_nospread", enabled || serverNoSpread ? "true" : "false");
    }

    if (_config.NoSpread.NoRecoil)
    {
      var serverRecoil = ConVar.Find("weapon_recoil_scale")?.GetPrimitiveValue<float>() ?? 2f;
      player.ReplicateConVar("weapon_recoil_scale", enabled ? "0" : serverRecoil.ToString(CultureInfo.InvariantCulture));
    }
  }

  private bool IsNoSpreadActive(CCSPlayerController player)
  {
    return IsActive(player, _noSpreadEnabled, _config.NoSpread);
  }

  private HookResult OnPlayerSpawnForNoSpread(EventPlayerSpawn @event, GameEventInfo info)
  {
    var player = @event.Userid;
    // после реконнекта клиент забывает реплицированное значение
    if (player != null && player.IsValid && !player.IsBot && IsNoSpreadActive(player))
      SyncClientConVars(player, true);

    return HookResult.Continue;
  }

  private void OnNoSpreadCommand(CCSPlayerController? player, CommandInfo commandInfo)
  {
    if (player == null || !player.IsValid) return;
    ToggleNoSpread(player);
  }

  private void ToggleNoSpread(CCSPlayerController player)
  {
    var newState = Toggle(player, _noSpreadEnabled, _config.NoSpread, "NoSpread");
    if (newState == null) return;

    SyncClientConVars(player, newState.Value);

    player.PrintToChat($" {ChatColors.Green}[NoSpread]{(newState.Value ? " Включён" : " Выключен")}");
  }

  private void OnTickForNoSpread()
  {
    if (!_config.NoSpread.Enabled) return;

    foreach (var player in Utilities.GetPlayers())
    {
      if (!player.IsValid || player.IsBot) continue;
      if (!IsActive(player, _noSpreadEnabled, _config.NoSpread)) continue;

      var weapon = player.PlayerPawn.Value?.WeaponServices?.ActiveWeapon.Value?.As<CCSWeaponBase>();
      if (weapon == null || !weapon.IsValid) continue;

      // штраф точности, который растёт с каждым выстрелом
      if (weapon.AccuracyPenalty != 0f)
      {
        weapon.AccuracyPenalty = 0f;
        Utilities.SetStateChanged(weapon, "CCSWeaponBase", "m_fAccuracyPenalty");
      }

      // разброс от резкого поворота камеры
      if (weapon.TurningInaccuracy != 0f)
      {
        weapon.TurningInaccuracy = 0f;
        Utilities.SetStateChanged(weapon, "CCSWeaponBase", "m_flTurningInaccuracy");
      }

      if (!_config.NoSpread.NoRecoil) continue;

      // номер выстрела в очереди — по нему движок выбирает, куда дёрнуть прицел.
      // Всегда 0 — каждый выстрел как первый, паттерна отдачи нет.
      if (weapon.IRecoilIndex != 0 || weapon.FlRecoilIndex != 0f)
      {
        weapon.IRecoilIndex = 0;
        weapon.FlRecoilIndex = 0f;
        Utilities.SetStateChanged(weapon, "CCSWeaponBase", "m_iRecoilIndex");
        Utilities.SetStateChanged(weapon, "CCSWeaponBase", "m_flRecoilIndex");
      }

      // накопленный подброс прицела — его сервер добавляет к направлению пуль
      var pawn = player.PlayerPawn.Value;
      var aimPunch = pawn?.AimPunchServices;
      if (pawn == null || aimPunch == null) continue;

      var angle = aimPunch.PredictableBaseAngle;
      var angleVel = aimPunch.PredictableBaseAngleVel;
      if (angle.X != 0f || angle.Y != 0f || angleVel.X != 0f || angleVel.Y != 0f)
      {
        if (_config.Debug) player.PrintToChat($" [debug] подброс {angle.X:F2} {angle.Y:F2}");

        angle.X = angle.Y = angle.Z = 0f;
        angleVel.X = angleVel.Y = angleVel.Z = 0f;
        Utilities.SetStateChanged(pawn, "CCSPlayerPawn", "m_pAimPunchServices");
      }
    }
  }
}
