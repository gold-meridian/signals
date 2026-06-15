using NUnit.Framework;
using Signals;

namespace Tests;

[TestFixture]
public class ComponentTests {
    [Test]
    public void Test_ComponentIdsAreUniquePerType() {
        int id1 = Component.GetId<Struct1>();
        int id2 = Component.GetId<Struct2>();
        int id3 = Component.GetId<int>();

        Assert.That(id1, Is.Not.EqualTo(id2));
        Assert.That(id1, Is.Not.EqualTo(id3));
    }

    [Test]
    public void Test_ComponentGenericIdSame() {
        Assert.That(Component.GetId<Struct1>(), Is.EqualTo(Component.GetId(typeof(Struct1))));
    }
}