using System.Diagnostics;
using System.Numerics;
using System.Reflection;
using Signals;
using Signals.Systems;

namespace Sample;

struct Tag1;
struct Tag2;
struct TestComponent { public int Value; }

unsafe partial class Program {
    private const int entity_count = 10_000;

    private static int entityCount;
    
    static void Main() { 
        using var world = new World();

        var app = new App(world);

        app
            .AddSystem(SpawnSomeEntities)
            .InStage(stage: Stage.Initialization)
            .Label("InitTest")
            .Build();
        
        app
            .AddSystem(TestUpdate)
            .InStage(stage: Stage.Update)
            .Label("UpdateTest")
            .Build();
        
        var cmds = new Commands();
        cmds.Fetch(world);
        
        app.Run();
        
        Console.WriteLine(entityCount);
    }

    [System]
    static partial void SpawnSomeEntities(Commands cmds) {
        for(int i = 0; i < entity_count; i++) {
            var entity = cmds.Spawn().Set(new Tag1());
        
            if (i % 2 == 0) {
                entity.Set(new Tag2());
            }
        }
        
        Console.WriteLine($"spawned {entity_count} entities");
    }
    
    [System, Without<Tag2>]
    static partial void TestUpdate(Entity entity, Tag1 tagComponent) {
        entityCount++;
    }
}
