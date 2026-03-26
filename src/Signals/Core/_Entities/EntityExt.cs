using System.Runtime.CompilerServices;

namespace Signals.V2;

public static partial class EntityExt {
    extension(Entity entity) {
        public EntityView<T1> View<T1>() where T1 : struct {
            return new EntityView<T1>(entity);
        }
    }
}