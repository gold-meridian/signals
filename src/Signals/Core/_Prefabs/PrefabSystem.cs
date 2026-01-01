// using Hjson;
// using Newtonsoft.Json;
// using Newtonsoft.Json.Linq;
// using Standard;
// using System.Diagnostics.CodeAnalysis;
// using System.Reflection;
// using System.Runtime.CompilerServices;
//
// namespace Signals.Core;
//
// public struct PrefabInfo : IComponent {
//     public required string Identifier;
//     public static implicit operator string(in PrefabInfo info) => info.Identifier;
// }
//
// [JsonConverter(typeof(PrefabJsonConverter))]
// public readonly struct Prefab {
//     public static readonly Prefab Invalid = default;
//
//     public readonly Entity Entity;
//     public readonly bool IsValid => Entity.Valid;
//
//     [MethodImpl(MethodImplOptions.AggressiveInlining)]
//     public Prefab(Entity entity) => Entity = entity;
//
//     [MethodImpl(MethodImplOptions.AggressiveInlining)]
//     public bool Has<T>() where T : struct, IComponent => Components.HasComponent<T>(Entity);
//     [MethodImpl(MethodImplOptions.AggressiveInlining)]
//     public ref T Get<T>() where T : struct, IComponent => ref Components.GetComponent<T>(Entity);
//
//     [MethodImpl(MethodImplOptions.AggressiveInlining)]
//     public ref T Set<T>(in T value) where T : struct, IComponent => ref Components.SetComponent<T>(Entity, value);
//
//     [MethodImpl(MethodImplOptions.AggressiveInlining)]
//     public static implicit operator Entity(Prefab prefab) => prefab.Entity;
// }
//
// public static class Prefabs {
//     private static readonly Dictionary<string, Prefab> prefabsByIdentifier = new();
//     private static readonly Dictionary<uint, Prefab> prefabs_by_index = [];
//     private static uint _prefabWorldId;
//
//     internal static Entity CreatePrefabEntity(string identifier) {
//         var entity = Entities.Create(_prefabWorldId);
//         Components.SetComponent(entity, new PrefabInfo
//         {
//             Identifier = identifier,
//         });
//         return entity;
//     }
//
//     public static EntityQuery EntityQuery() => new EntityQuery(Worlds.PrefabWorld.Index).With<PrefabInfo>();
//
//     public static void RegisterPrefab(string identifier, Prefab prefab) {
//         if(prefabsByIdentifier.ContainsKey(identifier)) {
//             throw new InvalidOperationException($"Prefab with identifier '{identifier}' already registered.");
//         }
//         prefabsByIdentifier[identifier] = prefab;
//         prefabs_by_index[prefab.Entity.Index] = prefab;
//     }
//
//     public static Prefab GetPrefab(string identifier) {
//         if(prefabsByIdentifier.TryGetValue(identifier, out var prefab)) {
//             return prefab;
//         }
//         return Prefab.Invalid;
//     }
//
//     public static Prefab GetPrefabByIndex(uint index) => prefabs_by_index[index];
//     public static bool TryGetPrefabByIndex(uint index, out Prefab prefab) => prefabs_by_index.TryGetValue(index, out prefab);
//
//     public static bool TryGetPrefab(string identifier, [NotNullWhen(true)] out Prefab prefab) {
//         return prefabsByIdentifier.TryGetValue(identifier, out prefab);
//     }
//
//     public static Entity Create(Prefab prefab, uint targetWorldId, Action<Entity> createAction = null) {
//         if(!prefab.IsValid) {
//             return Entity.Invalid;
//         }
//
//         var newEntity = Entities.Create(targetWorldId);
//
//         for(uint i = 1; i <= Components.ComponentCount; i++) {
//             var componentInfo = Components.ComponentInfos[i];
//             var componentType = componentInfo.Type;
//             var componentHandle = Components.GetComponentHandle(componentType);
//
//             var componentId = componentHandle.Id;
//             var entityComponentMaskSpan = Components.GetEntityComponentMaskSpan(prefab.Entity.WorldIndex, prefab.Entity.Index);
//             var (div, rem) = Math.DivRem((int)componentId, Bitset256.CAPACITY);
//
//             if(div < entityComponentMaskSpan.Length && entityComponentMaskSpan[div].IsSet(rem)) {
//                 var getMethod = typeof(Components)
//                     .GetMethod(nameof(Components.GetComponent), System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)
//                     .MakeGenericMethod(componentType);
//
//                 var componentValue = getMethod.Invoke(null, new object[] { prefab.Entity });
//
//                 var setMethod = typeof(Components)
//                     .GetMethod(nameof(Components.SetComponent), System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)
//                     .MakeGenericMethod(componentType);
//
//                 setMethod.Invoke(null, new object[] { newEntity, componentValue });
//             }
//         }
//
//         if(createAction != null) createAction.Invoke(newEntity);
//
//         return newEntity;
//     }
//
// }
//
// public sealed class PrefabLoading {
//     private static readonly string extension = ".prefab.hjson";
//
//     private static readonly JsonSerializer _deserializer = new JsonSerializer();
//     private static readonly HashSet<Type> _registeredConverterTypes = new();
//
//     static PrefabLoading() {
//         RegisterJsonConverter(new Vector2UShortJsonConverter());
//     }
//
//     public static void RegisterJsonConverter(JsonConverter converter) {
//         if(_registeredConverterTypes.Add(converter.GetType())) {
//             _deserializer.Converters.Add(converter);
//         }
//     }
//
//     public static void RegisterComponentTypesFromAssembly(Assembly assembly) {
//         foreach(Type type in assembly.GetTypes()) {
//             if(type.IsValueType && !type.IsInterface && !type.IsAbstract && typeof(IComponent).IsAssignableFrom(type)) {
//                 Components.RegisterComponent(type);
//             }
//         }
//     }
//
//     public static void LoadAllPrefabs(Assembly assembly) {
//         RegisterComponentTypesFromAssembly(assembly);
//         RegisterComponentTypesFromAssembly(typeof(IComponent).Assembly);
//         RegisterComponentTypesFromAssembly(typeof(Vector2UShort).Assembly);
//
//         string? assemblyPath = assembly.Location;
//
//         if(string.IsNullOrEmpty(assemblyPath)) {
//             assemblyPath = AppContext.BaseDirectory;
//         }
//         else {
//             assemblyPath = Path.GetDirectoryName(assemblyPath);
//             if(string.IsNullOrEmpty(assemblyPath)) {
//                 assemblyPath = AppContext.BaseDirectory;
//             }
//         }
// #if DEBUG
//         Console.WriteLine($"Loading prefabs from assembly directory: {assemblyPath}");
// #endif
//         List<Exception>? errors = null;
//
//         foreach(string fullFilePath in Directory.EnumerateFiles(assemblyPath!, $"*{extension}", SearchOption.AllDirectories)) {
//             string identifier = Path.GetFileNameWithoutExtension(fullFilePath);
//             if(identifier.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase)) {
//                 identifier = Path.GetFileNameWithoutExtension(identifier);
//             }
//
//             string hjsonText = File.ReadAllText(fullFilePath);
//             string standardJsonString = HjsonValue.Parse(hjsonText).ToString(Stringify.Plain);
//
//             try {
//                 using(var stringReader = new StringReader(standardJsonString))
//                 using(var jsonReader = new JsonTextReader(stringReader)) {
//                     var prefabHandle = _deserializer.Deserialize<Prefab>(jsonReader);
//
//                     if(!prefabHandle.Has<PrefabInfo>()) {
//                         prefabHandle.Set(new PrefabInfo { Identifier = identifier });
//                     }
//                     Prefabs.RegisterPrefab(identifier, prefabHandle);
//                 }
//
// #if DEBUG
//                 Console.WriteLine($"Successfully loaded prefab: {identifier}");
// #endif
//             }
//             catch(Exception e) {
//                 (errors ??= new()).Add(
//                     new Exception($"Error loading prefab '{identifier}' from '{fullFilePath}': {e.Message}", e));
//             }
//         }
//
//         if(errors != null) {
//             throw new AggregateException($"Errors occurred parsing *{extension} files", errors);
//         }
//     }
// }
//
// internal sealed class PrefabJsonConverter : JsonConverter {
//     public override bool CanConvert(Type objectType) => objectType == typeof(Prefab) || objectType == typeof(Entity);
//
//     public override void WriteJson(JsonWriter writer, object? value, JsonSerializer serializer) => throw new NotImplementedException("Writing prefabs not implemented.");
//
//     private static readonly MethodInfo ComponentsSetMethod = typeof(Components).GetMethod(nameof(Components.SetComponent), BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
//
//     public override object ReadJson(JsonReader reader, Type objectType, object? existingValue, JsonSerializer serializer) {
//         if(reader.TokenType == JsonToken.String) {
//         }
//         else if(reader.TokenType == JsonToken.StartObject) {
//             var jobj = JObject.Load(reader);
//             var tempEntity = Entities.Create(Worlds.PrefabWorld.Index);
//
//             ReadComponentsIntoEntity(tempEntity, jobj, serializer);
//
//             if(objectType == typeof(Prefab)) {
//                 return new Prefab(tempEntity);
//             }
//             else if(objectType == typeof(Entity)) {
//                 return tempEntity;
//             }
//         }
//
//         throw new JsonSerializationException($"unexpected token type {reader.TokenType} when reading prefab.");
//     }
//     internal static void ReadComponentsIntoEntity(Entity entity, JObject jObject, JsonSerializer serializer) {
//         object?[] paramArray = new object?[2];
//         foreach(var pair in jObject) {
//             var jsonElement = pair.Value;
//             var componentType = Components.GetComponentTypeFromName(pair.Key);
//
//             if(componentType == null) {
//                 throw new JsonSerializationException($"unknown component type '{pair.Key}' found during prefab deserialization.");
//             }
//
//             paramArray[0] = entity;
//             paramArray[1] = jsonElement!.ToObject(componentType, serializer);
//
//             var setMethod = ComponentsSetMethod.MakeGenericMethod(componentType);
//             setMethod.Invoke(null, paramArray);
//         }
//     }
// }