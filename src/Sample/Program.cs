using System.Diagnostics;
using System.Numerics;
using System.Reflection;
using Signals;
using Signals.Systems;

namespace Sample;

unsafe partial class Program {
    internal struct Age {
        public i32 Frames;
    }
    
    private const int entity_count = 1;
    
    static void Main() { 
        using var world = new World();
        var app = new App(world);
        
        for(int i = 0; i < entity_count; i++) {
            var entity = world
                .Create()
                .Set(new Age());
            
            Console.WriteLine($"entity with id {entity.Id} created");
        }
        
        Console.WriteLine(Component.Lookup<Age>.Info.Id);
        
        app
            .AddSystem(AgeAllEntities)
            .InStage(stage: Stage.Update)
            .WithTag("IncrementEntities")
            .Build();

        for(int i = 0; i < 4; i++) {
            app.Run();
        }
    }
    
    [System]
    static partial void AgeAllEntities(Entity entity, ref Age age) {
        age.Frames += 1;
        
        Console.WriteLine($"entity #{entity.Id} is now {age.Frames} frames old!");
    }
}
