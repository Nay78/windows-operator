using System.Collections;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using WindowsOperator.Core.Json;

namespace WindowsOperator.Core.Contracts;

public static class OperatorJsonSchema
{
    public static JsonObject For<T>() => BuildStandalone(new Registry("#/$defs/"), registry => registry.Ref<T>());

    public static JsonObject ArrayFor<T>() => BuildStandalone(new Registry("#/$defs/"), registry => registry.ArrayOf<T>());

    private static JsonObject BuildStandalone(Registry registry, Func<Registry, object> rootFactory)
    {
        var root = JsonSerializer.SerializeToNode(rootFactory(registry), OperatorJson.SerializerOptions)!.AsObject();
        root["$schema"] = "https://json-schema.org/draft/2020-12/schema";
        if (registry.Schemas.Count > 0)
        {
            root["$defs"] = JsonSerializer.SerializeToNode(registry.Schemas, OperatorJson.SerializerOptions);
        }

        return root;
    }

    public sealed class Registry
    {
        private readonly string _refPrefix;
        private readonly Dictionary<Type, string> _names = new();
        private readonly HashSet<Type> _building = new();

        public Registry(string refPrefix)
        {
            _refPrefix = refPrefix;
        }

        public Dictionary<string, object?> Schemas { get; } = new(StringComparer.Ordinal);

        public object Ref<T>() => SchemaFor(typeof(T));

        public object ArrayOf<T>() =>
            new Dictionary<string, object?>
            {
                ["type"] = "array",
                ["items"] = Ref<T>(),
            };

        private object SchemaFor(Type rawType)
        {
            var type = Nullable.GetUnderlyingType(rawType) ?? rawType;
            if (type == typeof(string))
            {
                return Primitive("string");
            }

            if (type == typeof(bool))
            {
                return Primitive("boolean");
            }

            if (type == typeof(int) || type == typeof(long) || type == typeof(short))
            {
                return Primitive("integer", type == typeof(long) ? "int64" : "int32");
            }

            if (type == typeof(float) || type == typeof(double) || type == typeof(decimal))
            {
                return Primitive("number", type == typeof(float) ? "float" : "double");
            }

            if (type == typeof(DateTimeOffset) || type == typeof(DateTime))
            {
                return Primitive("string", "date-time");
            }

            if (type.IsEnum)
            {
                var name = Register(type);
                if (!Schemas.ContainsKey(name))
                {
                    Schemas[name] = new Dictionary<string, object?>
                    {
                        ["type"] = "string",
                        ["enum"] = Enum.GetNames(type).Select(CamelCase).ToArray(),
                    };
                }

                return Ref(name);
            }

            if (TryGetDictionaryValue(type, out var valueType))
            {
                return new Dictionary<string, object?>
                {
                    ["type"] = "object",
                    ["additionalProperties"] = SchemaFor(valueType),
                };
            }

            if (type != typeof(byte[]) && TryGetEnumerableElement(type, out var elementType))
            {
                return new Dictionary<string, object?>
                {
                    ["type"] = "array",
                    ["items"] = SchemaFor(elementType),
                };
            }

            var schemaName = Register(type);
            BuildObjectSchema(type, schemaName);
            return Ref(schemaName);
        }

        private string Register(Type type)
        {
            if (_names.TryGetValue(type, out var existing))
            {
                return existing;
            }

            var name = type.Name;
            _names[type] = name;
            return name;
        }

        private void BuildObjectSchema(Type type, string schemaName)
        {
            if (Schemas.ContainsKey(schemaName) || !_building.Add(type))
            {
                return;
            }

            var properties = type.GetProperties(BindingFlags.Instance | BindingFlags.Public)
                .Where(property => property.GetMethod is not null && property.GetIndexParameters().Length == 0)
                .ToArray();
            var required = properties
                .Where(property => IsRequired(type, property))
                .Select(JsonName)
                .ToArray();

            Schemas[schemaName] = new Dictionary<string, object?>
            {
                ["type"] = "object",
                ["additionalProperties"] = false,
                ["properties"] = properties.ToDictionary(
                    JsonName,
                    PropertySchema,
                    StringComparer.Ordinal),
            };

            if (required.Length > 0)
            {
                ((Dictionary<string, object?>)Schemas[schemaName]!)["required"] = required;
            }

            _building.Remove(type);
        }

        private object PropertySchema(PropertyInfo property)
        {
            var schema = SchemaFor(property.PropertyType);
            if (IsNullable(property) && schema is Dictionary<string, object?> dict && !dict.ContainsKey("$ref"))
            {
                dict = new Dictionary<string, object?>(dict, StringComparer.Ordinal)
                {
                    ["nullable"] = true,
                };
                return dict;
            }

            if (IsNullable(property) && schema is Dictionary<string, object?> refDict && refDict.ContainsKey("$ref"))
            {
                return new Dictionary<string, object?>
                {
                    ["allOf"] = new[] { refDict },
                    ["nullable"] = true,
                };
            }

            return schema;
        }

        private object Ref(string name) =>
            new Dictionary<string, object?> { ["$ref"] = $"{_refPrefix}{name}" };

        private static bool IsRequired(Type declaringType, PropertyInfo property)
        {
            if (declaringType.Name.EndsWith("Request", StringComparison.Ordinal) ||
                declaringType == typeof(OperatorError))
            {
                return false;
            }

            return !IsNullable(property);
        }

        private static bool IsNullable(PropertyInfo property)
        {
            if (Nullable.GetUnderlyingType(property.PropertyType) is not null)
            {
                return true;
            }

            if (property.PropertyType.IsValueType)
            {
                return false;
            }

            var context = new NullabilityInfoContext();
            return context.Create(property).ReadState == NullabilityState.Nullable;
        }

        private static bool TryGetEnumerableElement(Type type, out Type elementType)
        {
            if (type.IsArray)
            {
                elementType = type.GetElementType()!;
                return true;
            }

            var match = type == typeof(IEnumerable)
                ? null
                : type.GetInterfaces()
                    .Concat(new[] { type })
                    .FirstOrDefault(candidate =>
                        candidate.IsGenericType &&
                        candidate.GetGenericTypeDefinition() == typeof(IEnumerable<>));

            if (match is null)
            {
                elementType = typeof(object);
                return false;
            }

            elementType = match.GetGenericArguments()[0];
            return true;
        }

        private static bool TryGetDictionaryValue(Type type, out Type valueType)
        {
            var match = type.GetInterfaces()
                .Concat(new[] { type })
                .FirstOrDefault(candidate =>
                    candidate.IsGenericType &&
                    candidate.GetGenericTypeDefinition() == typeof(IReadOnlyDictionary<,>) &&
                    candidate.GetGenericArguments()[0] == typeof(string));

            if (match is null)
            {
                valueType = typeof(object);
                return false;
            }

            valueType = match.GetGenericArguments()[1];
            return true;
        }

        private static string JsonName(PropertyInfo property) =>
            property.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name ?? CamelCase(property.Name);

        private static string CamelCase(string value) =>
            string.IsNullOrEmpty(value)
                ? value
                : char.ToLowerInvariant(value[0]) + value[1..];
    }

    private static object Primitive(string type, string? format = null)
    {
        var schema = new Dictionary<string, object?> { ["type"] = type };
        if (!string.IsNullOrWhiteSpace(format))
        {
            schema["format"] = format;
        }

        return schema;
    }
}
