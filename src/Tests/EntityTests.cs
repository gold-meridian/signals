using NUnit.Framework;
using Signals.V2;

namespace Tests;

[TestFixture]
public class EntityTests {
    [Test]
    public void Test_ProperEntityCreation() {
        using var world = new World();

        var e = world.Create();
        
        Assert.That(e.IsAlive);
    }
    
    [Test]
    public void Test_EntityCtorCreatesNull() {
        Assert.That(new Entity(), Is.EqualTo(default(Entity)));
    }
}