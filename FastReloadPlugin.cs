using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Utils;

namespace FastReloadPlugin;

public class FastReloadPlugin : BasePlugin
{
  public override string ModuleName => "FastReload";
  public override string ModuleVersion => "0.1.0";

  public override string ModuleAuthor => "Iposhka54";
  public override string ModuleDescription => "Plugin for auto-bhop and fast reload";

  // во сколько раз ускоряется перезарядка
  private const float FastReloadRate = 10f;
  private const float DefaultRate = 1f;

  private readonly Dictionary<ulong, bool> _bhopEnabled = new();
  private readonly Dictionary<ulong, bool> _fastReloadEnabled = new();

  // какой NextPrimaryAttackTick мы уже выставили — чтобы не сокращать один и тот же таймер повторно
  private readonly Dictionary<ulong, int> _shortenedTo = new();

  public override void Load(bool hotReload)
  {
    // префикс css_ нужен, чтобы команда работала из чата как !bhop
    AddCommand("css_bhop", "Toggle auto-bhop", OnBhopCommand);
    AddCommand("css_fastreload", "Toggle fast reload", OnFastReloadCommand);

    RegisterListener<Listeners.OnTick>(OnTickForBhop);
    RegisterListener<Listeners.OnTick>(OnTickForReload);
  }

  private void OnBhopCommand(CCSPlayerController? player, CommandInfo commandInfo)
  {
    if (player == null || !player.IsValid) return;

    var steamId = player.SteamID;
    bool newState = !_bhopEnabled.GetValueOrDefault(steamId, false);
    _bhopEnabled[steamId] = newState;

    player.PrintToChat($" {ChatColors.Green}[Bhop]{(newState ? " Включён" : " Выключен")}");
  }

  private void OnFastReloadCommand(CCSPlayerController? player, CommandInfo commandInfo)
  {
    if (player == null || !player.IsValid) return;

    var steamId = player.SteamID;
    var newState = !_fastReloadEnabled.GetValueOrDefault(steamId, false);
    _fastReloadEnabled[steamId] = newState;

    // если выключили посреди перезарядки — не оставляем оружие ускоренным
    if (!newState) SetActiveWeaponRate(player, DefaultRate);

    player.PrintToChat($" {ChatColors.Green}[FastReload]{(newState ? " Включена" : " Выключена")}");
  }

  private void OnTickForBhop()
  {
    if (_bhopEnabled.Count == 0) return;

    foreach (var player in Utilities.GetPlayers())
    {
      if (!player.IsValid || player.IsBot) continue;

      if (!_bhopEnabled.GetValueOrDefault(player.SteamID, false)) continue;

      var pawn = player.PlayerPawn.Value;
      if (pawn == null || !pawn.IsValid || pawn.LifeState != (byte)LifeState_t.LIFE_ALIVE) continue;

      bool onGround = (pawn.Flags & (uint)PlayerFlags.FL_ONGROUND) != 0;
      bool holdingJump = (player.Buttons & PlayerButtons.Jump) != 0;

      bool onLadder = pawn.MoveType == MoveType_t.MOVETYPE_LADDER;

      if (onGround && holdingJump && !onLadder)
      {
        pawn.AbsVelocity.Z = 300f;
      }
    }
  }

  // пришлось сделать проверку перезарядки через тики, потому что событие перезарядки(EventWeaponReload) оружие выбрасывается
  // только при ручной перезарядке, если патроны закончились в обойме, то перезарядка начинается сама, в этом случае
  // событие EventWeaponReload не выкидывается
  private void OnTickForReload()
  {
    if (_fastReloadEnabled.Count == 0) return;

    foreach (var player in Utilities.GetPlayers())
    {
      if (!player.IsValid || player.IsBot) continue;
      if (!_fastReloadEnabled.GetValueOrDefault(player.SteamID, false)) continue;

      var weapon = player.PlayerPawn.Value?.WeaponServices?.ActiveWeapon.Value?.As<CCSWeaponBase>();
      if (weapon == null || !weapon.IsValid) continue;

      if (!weapon.InReload)
      {
        SetActiveWeaponRate(player, DefaultRate);
        continue;
      }

      SetActiveWeaponRate(player, FastReloadRate);
      ShortenAttackDelay(player, debug: true);
    }
  }

  // PlaybackRate за анимацию, но когда разрешить стрельбу отвечает NextPrimaryAttackTick
  private void ShortenAttackDelay(CCSPlayerController player, bool debug = false)
  {
    var weapon = player.PlayerPawn.Value?.WeaponServices?.ActiveWeapon.Value;
    if (weapon == null || !weapon.IsValid) return;

    if (_shortenedTo.TryGetValue(player.SteamID, out var shortened) && weapon.NextPrimaryAttackTick == shortened) return;

    int now = Server.TickCount;
    int ticksLeft = weapon.NextPrimaryAttackTick - now;

    if (ticksLeft <= 0) return;

    if (debug)
      player.PrintToChat($" [debug] ждать было {ticksLeft} тиков, станет {(int)Math.Ceiling(ticksLeft / FastReloadRate)}");

    weapon.NextPrimaryAttackTick = now + (int)Math.Ceiling(ticksLeft / FastReloadRate);
    Utilities.SetStateChanged(weapon, "CBasePlayerWeapon", "m_nNextPrimaryAttackTick");
    _shortenedTo[player.SteamID] = weapon.NextPrimaryAttackTick;

    // вторую атаку так же ускоряем
    int secondaryLeft = weapon.NextSecondaryAttackTick - now;
    if (secondaryLeft > 0)
    {
      weapon.NextSecondaryAttackTick = now + (int)Math.Ceiling(secondaryLeft / FastReloadRate);
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
    foreach (var player in Utilities.GetPlayers())
    {
      if (player.IsValid && _fastReloadEnabled.GetValueOrDefault(player.SteamID, false))
        SetActiveWeaponRate(player, DefaultRate);
    }

    _bhopEnabled.Clear();
    _fastReloadEnabled.Clear();
    _shortenedTo.Clear();
  }
}
