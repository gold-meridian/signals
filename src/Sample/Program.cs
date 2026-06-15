using System.Diagnostics;
using System.Numerics;
using System.Reflection;
using Signals;
using Signals.Systems;

namespace Sample;

unsafe partial class Program {
    internal struct Initialize;
    internal struct Update;
    
    internal struct Age {
        public i32 Frames;
    }
    
    private const int entity_count = 1;
    
    static void Main() { 
        using var world = new World();
        var app = new App(world);
        
        app
            .AddSystem(SpawnSomeEntities)
            .InCallback<Initialize>()
            .Before("IncrementEntities")
            .Build();
        
        app
            .AddSystem(AgeAllEntities)
            .InCallback<Update>()
            .WithTag("IncrementEntities")
            .Build();
        
        app
            .AddSystem(KillOldEntities)
            .InCallback<Update>()
            .After("IncrementEntities")
            .Build();
        
        app.RunCallback<Initialize>();

        for(var i = 0; i < 4; i++) {
            app.RunCallback<Update>();
        }
    }

    [System]
    static partial void SpawnSomeEntities(Commands cmds) {
        for(var i = 0; i < entity_count; i++) {
            cmds
                .Spawn()
                .Set(new Age());
        }
        
        Console.WriteLine("spawned entities");
    }
    
    [System]
    static partial void AgeAllEntities(Entity entity, ref Age age) {
        age.Frames += 1;
        
        Console.WriteLine($"entity #{entity.Id} is now {age.Frames} frames old!");
    }
    
    [System]
    static partial void KillOldEntities(Commands cmds, Entity entity, ref Age age) {
        if(age.Frames == 4) {
            entity.Destroy();
        } 
    }
}
