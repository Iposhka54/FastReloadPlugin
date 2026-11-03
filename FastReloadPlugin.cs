using CounterStrikeSharp.API.Core;

namespace FastReloadPlugin;

public class FastReloadPlugin : BasePlugin
{
  public override string ModuleName => "FastReload";
  public override string ModuleVersion => "0.1.0";

  public override void Load(bool hotReload)
  {
    Console.WriteLine("Hello World!");
  }
}
