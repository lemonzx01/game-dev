using FantasyWorld.Simulation;
using System.Collections.Immutable;
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

const long God = 1;
var world = new SimulationWorld(new SimulationOptions
{
    WorldId = new WorldId(1),
    Seed = seed,
    PrewarmStreams = [.. tags],
    Species = SpeciesCatalog.Default,
    Grants = ImmutableSortedDictionary<long, AuthorityGrant>.Empty.Add(God, AuthorityGrant.God),
});

// Deterministic command script: targets are found by scanning the map in ascending cell order.
var map = world.ReadMap();
(int X, int Y) land = (-1, -1), food = (-1, -1);
for (int i = 0; i < map.Cells.Length && (land.X < 0 || food.X < 0); i++)
{
    int x = i % map.Width, y = i / map.Width;
    if (land.X < 0 && map.Cells[i] is Terrain.Grass or Terrain.Forest) land = (x, y);
    if (food.X < 0 && world.ReadResource(x, y, ResourceKind.Food) is not null) food = (x, y);
}

for (int i = 0; i < ticks; i++)
{
    if (perturb) Thread.Sleep(1);
    long next = world.LastCompletedTick + 1;
    if (next % 10 == 5)
        world.SubmitCommand(new SpawnEntityCommand(new WorldId(1), God, CommandModes.God, land.X, land.Y, 1));
    if (next == 15) // a predator joins so replay covers hunting and carcasses
        world.SubmitCommand(new SpawnEntityCommand(new WorldId(1), God, CommandModes.God, land.X, land.Y, SpeciesCatalog.Wolf));
    if (next % 25 == 0 && food.X >= 0)
        world.SubmitCommand(new TakeResourceCommand(new WorldId(1), God, CommandModes.God, food.X, food.Y, ResourceKind.Food, 3));
    world.Step();
    Console.WriteLine($"{world.ReadSnapshot().Tick} {world.StateHash()}");
}
return 0;
