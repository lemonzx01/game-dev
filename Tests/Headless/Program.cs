using FantasyWorld.Simulation;
using System.Globalization;

if (args.Length is < 1 or > 3 || !ulong.TryParse(args[0], CultureInfo.InvariantCulture, out ulong seed))
{
    Console.Error.WriteLine("Usage: dotnet Headless.dll SEED [TICKS] [--perturb]");
    return 2;
}
int ticks = 100;
if (args.Length >= 2 && (!int.TryParse(args[1], out ticks) || ticks < 0)) return 2;
bool perturb = args.Length == 3;
if (perturb && args[2] != "--perturb") return 2;

// Both fixtures contain the same three world streams after setup.
var tags = new HashSet<uint>();
if (perturb)
{
    tags.UnionWith([3u, 99u, 2u]);
    tags.Remove(99);
    tags.Add(1);
}
else
{
    tags.UnionWith([1u, 2u, 3u]);
}

var world = new SimulationWorld(new WorldId(1), seed, tags);
for (int i = 0; i < ticks; i++)
{
    if (perturb) Thread.Sleep(1);
    world.Step();
    Console.WriteLine($"{world.ReadSnapshot().Tick} {world.StateHash()}");
}
return 0;
