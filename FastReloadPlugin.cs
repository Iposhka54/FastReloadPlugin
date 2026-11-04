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

  private readonly Dictionary<ulong, bool> _bhopEnabled = new();

  public override void Load(bool hotReload)
  {
    // префикс css_ нужен, чтобы команда работала из чата как !bhop
    AddCommand("css_bhop", "Toggle auto-bhop", OnBhopCommand);

    RegisterListener<Listeners.OnTick>(OnTick);
  }

  private void OnBhopCommand(CCSPlayerController? player, CommandInfo commandInfo)
  {
    if (player == null || !player.IsValid) return;

    var steamId = player.SteamID;
    bool newState = !_bhopEnabled.GetValueOrDefault(steamId, false);
    _bhopEnabled[steamId] = newState;

    player.PrintToChat($" {ChatColors.Green}[Bhop]{(newState ? " Включён" : " Выключен")}");
  }

  private void OnTick()
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

  public override void Unload(bool hotReload)
  {
    _bhopEnabled.Clear();
  }
}
