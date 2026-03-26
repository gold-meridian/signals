using NUnit.Framework;
using Signals;

namespace Tests;

[TestFixture]
public class ComponentTests {
    [Test]
    public void Test_ComponentIdsAreUniquePerType() {
        int id1 = ComponentStore.GetId<Struct1>();
        int id2 = ComponentStore.GetId<Struct2>();
        int id3 = ComponentStore.GetId<int>();

        Assert.That(id1, Is.Not.EqualTo(id2));
        Assert.That(id1, Is.Not.EqualTo(id3));
    }

    [Test]
    public void Test_ComponentGenericIdSame() {
        Assert.That(ComponentStore.GetId<Struct1>(), Is.EqualTo(ComponentStore.GetId(typeof(Struct1))));
    }
}