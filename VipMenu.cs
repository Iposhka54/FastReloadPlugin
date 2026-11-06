using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Menu;
using CounterStrikeSharp.API.Modules.Utils;

namespace FastReloadPlugin;

public partial class FastReloadPlugin
{
  private void RegisterVipMenu()
  {
    // Пункты выбираются через !1, !2... в чате. Цифры на клавиатуре (slot1..slot9)
    // обрабатываются только клиентом и до сервера не доходят — проверено.
    AddCommand("css_vip", "Open VIP menu", OnVipCommand);
  }

  private void OnVipCommand(CCSPlayerController? player, CommandInfo commandInfo)
  {
    if (player == null || !player.IsValid) return;

    if (!IsAllowed(player, _config.VipMenu))
    {
      player.PrintToChat($" {ChatColors.Red}[VIP]{ChatColors.Default} Меню доступно только VIP");
      return;
    }

    OpenVipMenu(player);
  }

  private void OpenVipMenu(CCSPlayerController player)
  {
    var menu = new CenterHtmlMenu("VIP меню", this)
    {
      TitleColor = "yellow",
      EnabledColor = "#FFD700",
      DisabledColor = "gray",
      ExitButton = true,
      PostSelectAction = PostSelectAction.Nothing,
    };

    AddToggleOption(menu, player, "Бхоп", _bhopEnabled, _config.Bhop, ToggleBhop);
    AddToggleOption(menu, player, "Быстрая перезарядка", _fastReloadEnabled, _config.FastReload, ToggleFastReload);
    AddToggleOption(menu, player, "Анти-разброс", _noSpreadEnabled, _config.NoSpread, ToggleNoSpread);

    MenuManager.OpenCenterHtmlMenu(this, player, menu);
  }

  private void AddToggleOption(CenterHtmlMenu menu, CCSPlayerController player, string name,
    Dictionary<ulong, bool> toggles, FeatureConfig feature, Action<CCSPlayerController> toggle)
  {
    var state = IsActive(player, toggles, feature) ? "вкл" : "выкл";

    menu.AddMenuOption($"{name} [{state}]", (p, _) =>
    {
      toggle(p);
      OpenVipMenu(p);
    }, disabled: !IsAllowed(player, feature)); // нет доступа к функции — пункт серый
  }
}
