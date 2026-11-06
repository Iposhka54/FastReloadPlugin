namespace FastReloadPlugin;

// Структура config.json, который лежит рядом с FastReloadPlugin.dll
public class PluginConfig
{
  // выводить в чат отладочную информацию о перезарядке
  public bool Debug { get; set; } = false;

  public BhopConfig Bhop { get; set; } = new();
  public FastReloadConfig FastReload { get; set; } = new();
  public NoSpreadConfig NoSpread { get; set; } = new();
  public VipMenuConfig VipMenu { get; set; } = new();
}

// Кто может пользоваться функцией или командой
public class AccessConfig
{
  // false — выключено на сервере для всех, команда не работает
  public bool Enabled { get; set; } = true;

  // Кому можно пользоваться:
  //   "*"                  — всем
  //   "76561198000000000"  — конкретному игроку по SteamID64
  //   "@css/vip"           — всем, у кого есть этот флаг в admins.json CounterStrikeSharp
  public List<string> AllowedPlayers { get; set; } = ["*"];
}

public class FeatureConfig : AccessConfig
{
  // true — функция включена у игрока сразу, без ввода команды
  public bool EnabledByDefault { get; set; } = false;
}

public class NoSpreadConfig : FeatureConfig
{
  // заодно убирать отдачу (подброс прицела при стрельбе)
  public bool NoRecoil { get; set; } = true;

  // Сигнатура CCSWeaponBaseGun::GetInaccuracy в server.dll (Windows), взята из плагина Dropshot.
  // После обновления CS2 может перестать находиться — тогда её нужно обновить здесь.
  // Пусто — хук не ставится, остаётся только обнуление накопленного разброса.
  public string InaccuracySignature { get; set; } = "48 89 5C 24 ? 55 56 57 48 81 EC ? ? ? ? 44 0F 29 84 24";
}

// меню !vip
public class VipMenuConfig : AccessConfig
{
  public VipMenuConfig()
  {
    AllowedPlayers = ["@css/vip"];
  }
}

public class BhopConfig : FeatureConfig
{
  // вертикальная скорость прыжка
  public float JumpVelocity { get; set; } = 300f;
}

public class FastReloadConfig : FeatureConfig
{
  // во сколько раз ускоряется перезарядка
  public float Rate { get; set; } = 10f;
}
