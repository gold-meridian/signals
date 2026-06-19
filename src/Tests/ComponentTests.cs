using NUnit.Framework;
using Signals;

namespace Tests;

public struct Comp0 { public int value; }
public struct Comp1 { public int value; }

[TestFixture]
public class ComponentMaskBucketTests {
    [Test]
    public void ComponentMask_SetClearIsSetSingleBucket() {
        var mask = new ComponentMask();
            
        mask.Set(0);
        mask.Set(127);
        mask.Set(255);
            
        Assert.That(mask.IsSet(0), Is.True);
        Assert.That(mask.IsSet(127), Is.True);
        Assert.That(mask.IsSet(255), Is.True);
        Assert.That(mask.IsSet(100), Is.False);
            
        mask.Clear(127);
        Assert.That(mask.IsSet(127), Is.False);
        Assert.That(mask.IsSet(0), Is.True);
    }

    [Test]
    public void ComponentMask_BoundaryBetweenBuckets() {
        var mask = new ComponentMask();
        
        //255 is bucket0, 256 goes into bucket1, 511 is the end of bucket1
        mask.Set(255);
        mask.Set(256);
        mask.Set(511);
            
        Assert.That(mask.IsSet(255), Is.True);
        Assert.That(mask.IsSet(256), Is.True);
        Assert.That(mask.IsSet(511), Is.True);
            
        mask.Clear(256);
        Assert.That(mask.IsSet(255), Is.True);
        Assert.That(mask.IsSet(256), Is.False);
        Assert.Throws<System.IndexOutOfRangeException>(() => mask.Set(512));
    }
        
    [Test]
    public void ComponentMask_Contains_SingleBucket() {
        var mask1 = new ComponentMask();
        var mask2 = new ComponentMask();
            
        mask1.Set(0);
        mask1.Set(50);
        mask1.Set(100);
            
        mask2.Set(0);
        mask2.Set(50);
            
        Assert.That(mask1.Contains(mask2), Is.True);
        Assert.That(mask2.Contains(mask1), Is.False);
    }
        
    [Test]
    public void ComponentMask_Contains_MultipleBuckets() {
        var mask1 = new ComponentMask();
        var mask2 = new ComponentMask();
            
        mask1.Set(100);  // bucket0
        mask1.Set(300);  // bucket1
        mask1.Set(450);  // bucket1
            
        mask2.Set(100);  // bucket0
        mask2.Set(300);  // bucket1
            
        Assert.That(mask1.Contains(mask2), Is.True);
        Assert.That(mask2.Contains(mask1), Is.False);
            
        mask2.Set(500);  // bucket1, valid bit but missing from mask1
        Assert.That(mask1.Contains(mask2), Is.False);
    }

    [Test]
    public void ComponentMask_AndAny_SameBucket() {
        var mask1 = new ComponentMask();
        var mask2 = new ComponentMask();
            
        mask1.Set(50);
        mask1.Set(100);
            
        mask2.Set(100);
        mask2.Set(150);
            
        Assert.That(mask1.AndAny(mask2), Is.True);
            
        mask2.Clear(100);
        Assert.That(mask1.AndAny(mask2), Is.False);
    }

    [Test]
    public void ComponentMask_AndAny_DifferentBuckets() {
        var mask1 = new ComponentMask();
        var mask2 = new ComponentMask();
            
        mask1.Set(100);  // bucket0
        mask1.Set(350);  // bucket1
            
        mask2.Set(400);  // bucket1
        mask2.Set(200);  // bucket0
            
        Assert.That(mask1.AndAny(mask2), Is.False);
            
        mask2.Set(350);  // overlap in bucketsp1
        Assert.That(mask1.AndAny(mask2), Is.True);
    }

    [Test]
    public void ComponentMask_Reset() {
        var mask = new ComponentMask();
            
        mask.Set(100);
        mask.Set(300);
        mask.Set(450);
            
        mask.Reset();
            
        Assert.That(mask.IsSet(100), Is.False);
        Assert.That(mask.IsSet(300), Is.False);
        Assert.That(mask.IsSet(450), Is.False);
    }
}
    
[TestFixture]
public class WorldBucketIntegrationTests {
    private World world;
        
    [SetUp]
    public void Setup() {
        world = new World();
    }
        
    [TearDown]
    public void Teardown() {
        world.Dispose();
    }
        
    [Test]
    public void SetGetHasComponents() {
        var entity = world.Create();
            
        world.Set(entity.Id, new Comp0 { value = 42 });
        world.Set(entity.Id, new Comp1 { value = 100 });
            
        Assert.That(world.Has<Comp0>(entity.Id), Is.True);
        Assert.That(world.Has<Comp1>(entity.Id), Is.True);
        Assert.That(world.Get<Comp0>(entity.Id).value, Is.EqualTo(42));
    }

    [Test]
    public void RemoveComponent() {
        var entity = world.Create();
            
        world.Set(entity.Id, new Comp0 { value = 42 });
        world.Set(entity.Id, new Comp1 { value = 100 });
            
        world.Remove<Comp0>(entity.Id);
            
        Assert.That(world.Has<Comp0>(entity.Id), Is.False);
        Assert.That(world.Has<Comp1>(entity.Id), Is.True);
    }
        
    [Test]
    public void DestroyEntity() {
        var entity = world.Create();
            
        world.Set(entity.Id, new Comp0 { value = 42 });
        world.Set(entity.Id, new Comp1 { value = 100 });
            
        world.Destroy(entity.Id, entity.Generation);
            
        Assert.That(world.IsValid(entity.Id, entity.Generation), Is.False);
    }
        
    [Test]
    public void QueryWithMultipleComponents() {
        var e1 = world.Create();
        var e2 = world.Create();
        var e3 = world.Create();
            
        world.Set(e1.Id, new Comp0 { value = 1 });
        world.Set(e1.Id, new Comp1 { value = 2 });
            
        world.Set(e2.Id, new Comp0 { value = 3 });
        world.Set(e3.Id, new Comp1 { value = 4 });
            
        var results = new List<uint>();
        var iterator = world.Query()
            .With<Comp0>()
            .With<Comp1>()
            .Iterate();
            
        while (iterator.Next() is { } entity) {
            results.Add(entity.Id);
        }
            
        Assert.That(results.Count, Is.EqualTo(1));
        Assert.That(results[0], Is.EqualTo(e1.Id));
    }
        
    [Test]
    public void QueryWithoutFilter() {
        var e1 = world.Create();
        var e2 = world.Create();
            
        world.Set(e1.Id, new Comp0 { value = 1 });
        world.Set(e2.Id, new Comp0 { value = 2 });
        world.Set(e2.Id, new Comp1 { value = 3 });
            
        var results = new List<uint>();
        var iterator = world.Query()
            .With<Comp0>()
            .Without<Comp1>()
            .Iterate();
            
        while (iterator.Next() is { } entity) {
            results.Add(entity.Id);
        }
            
        Assert.That(results.Count, Is.EqualTo(1));
        Assert.That(results[0], Is.EqualTo(e1.Id));
    }
        
    [Test]
    public void MultipleEntitiesQuery() {
        for (int i = 0; i < 10; i++) {
            var e = world.Create();
            world.Set(e.Id, new Comp0 { value = i });
            if (i % 2 == 0) {
                world.Set(e.Id, new Comp1 { value = i * 2 });
            }
        }
            
        var count = 0;
        var iterator = world.Query()
            .With<Comp0>()
            .With<Comp1>()
            .Iterate();
            
        while (iterator.Next() is { } entity) {
            count++;
        }
            
        Assert.That(count, Is.EqualTo(5));
    }
}