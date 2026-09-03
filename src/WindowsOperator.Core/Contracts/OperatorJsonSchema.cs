using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using WindowsOperator.Core.Json;

namespace WindowsOperator.Core.Contracts;

public static class OperatorJsonSchema
{
    public static JsonObject For<T>() =>
        BuildStandalone(new Registry("#/$defs/"), registry => registry.Ref<T>());

    public static JsonObject InputFor<T>() =>
        BuildStandalone(new Registry("#/$defs/"), registry => registry.Input<T>());

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
        private readonly Dictionary<(Type Type, SchemaDirection Direction), string> _names = new();
        private readonly HashSet<(Type Type, SchemaDirection Direction)> _building = new();

        public Registry(string refPrefix)
        {
            _refPrefix = refPrefix;
        }

        public Dictionary<string, object?> Schemas { get; } = new(StringComparer.Ordinal);

        public object Ref<T>() => SchemaFor(typeof(T), SchemaDirection.Output);

        public object Input<T>() => SchemaFor(typeof(T), SchemaDirection.Input);

        public object ArrayOf<T>() =>
            new Dictionary<string, object?>
            {
                ["type"] = "array",
                ["items"] = Ref<T>(),
            };

        private object SchemaFor(Type rawType, SchemaDirection direction)
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

            if (type == typeof(int) || type == typeof(long) || type == typeof(short) || type == typeof(sbyte))
            {
                return Primitive("integer", type == typeof(long) ? "int64" : "int32");
            }

            if (type == typeof(uint) || type == typeof(ulong) || type == typeof(ushort) || type == typeof(byte))
            {
                var schema = Primitive(
                    "integer",
                    type == typeof(ushort) ? "int32" : "int64");
                schema["minimum"] = 0;
                return schema;
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
                var name = Register(type, SchemaDirection.Output);
                if (!Schemas.ContainsKey(name))
                {
                    Schemas[name] = new Dictionary<string, object?>
                    {
                        ["type"] = "string",
                        ["enum"] = Enum.GetNames(type).Select(CamelCase).ToArray(),
                        ["description"] = $"Allowed values for {type.Name}.",
                    };
                }

                return Ref(name);
            }

            if (TryGetDictionaryValue(type, out var valueType))
            {
                return new Dictionary<string, object?>
                {
                    ["type"] = "object",
                    ["additionalProperties"] = SchemaFor(valueType, direction),
                };
            }

            if (type != typeof(byte[]) && TryGetEnumerableElement(type, out var elementType))
            {
                return new Dictionary<string, object?>
                {
                    ["type"] = "array",
                    ["items"] = SchemaFor(elementType, direction),
                };
            }

            var schemaName = Register(type, direction);
            BuildObjectSchema(type, schemaName, direction);
            return Ref(schemaName);
        }

        private string Register(Type type, SchemaDirection direction)
        {
            var key = (type, direction);
            if (_names.TryGetValue(key, out var existing))
            {
                return existing;
            }

            var isRequest = type.Name.EndsWith("Request", StringComparison.Ordinal);
            var name = direction switch
            {
                SchemaDirection.Input when isRequest => type.Name,
                SchemaDirection.Input => $"{type.Name}Input",
                SchemaDirection.Output when isRequest => $"{type.Name}Output",
                _ => type.Name,
            };
            _names[key] = name;
            return name;
        }

        private void BuildObjectSchema(Type type, string schemaName, SchemaDirection direction)
        {
            var key = (type, direction);
            if (Schemas.ContainsKey(schemaName) || !_building.Add(key))
            {
                return;
            }

            var properties = type.GetProperties(BindingFlags.Instance | BindingFlags.Public)
                .Where(property =>
                    property.GetMethod is not null &&
                    property.GetIndexParameters().Length == 0 &&
                    !property.IsDefined(typeof(JsonIgnoreAttribute), inherit: true) &&
                    !property.IsDefined(typeof(OperatorInternalAttribute), inherit: true))
                .ToArray();
            var required = properties
                .Where(property => IsRequired(property, direction))
                .Select(JsonName)
                .ToArray();
            var defaultInstance = direction == SchemaDirection.Input
                ? CreateDefaultInstance(type)
                : null;

            Schemas[schemaName] = new Dictionary<string, object?>
            {
                ["type"] = "object",
                ["additionalProperties"] = false,
                ["properties"] = properties.ToDictionary(
                    JsonName,
                    property => PropertySchema(property, direction, defaultInstance),
                    StringComparer.Ordinal),
            };

            if (required.Length > 0)
            {
                ((Dictionary<string, object?>)Schemas[schemaName]!)["required"] = required;
            }

            _building.Remove(key);
        }

        private object PropertySchema(
            PropertyInfo property,
            SchemaDirection direction,
            object? defaultInstance)
        {
            var schema = (Dictionary<string, object?>)SchemaFor(property.PropertyType, direction);
            var nullable = IsNullable(property);
            object? defaultValue = null;
            var hasDefault = direction == SchemaDirection.Input &&
                !IsRequired(property, direction) &&
                TryGetScalarDefault(property, defaultInstance, out defaultValue);

            Dictionary<string, object?> result;
            if (schema.ContainsKey("$ref") && (nullable || hasDefault))
            {
                result = new Dictionary<string, object?>
                {
                    ["allOf"] = new[] { schema },
                };
            }
            else
            {
                result = new Dictionary<string, object?>(schema, StringComparer.Ordinal);
            }

            if (nullable)
            {
                result["nullable"] = true;
            }

            if (hasDefault)
            {
                result["default"] = defaultValue;
                result["description"] = $"Defaults to {SerializeDefault(defaultValue)} when omitted.";
            }

            return result;
        }

        private object Ref(string name) =>
            new Dictionary<string, object?> { ["$ref"] = $"{_refPrefix}{name}" };

        private static bool IsRequired(PropertyInfo property, SchemaDirection direction) =>
            direction == SchemaDirection.Input
                ? property.IsDefined(typeof(RequiredMemberAttribute), inherit: true)
                : !IsNullable(property);

        private static object? CreateDefaultInstance(Type type)
        {
            try
            {
                return Activator.CreateInstance(type);
            }
            catch (Exception exception) when (
                exception is MissingMethodException or
                MemberAccessException or
                TargetInvocationException)
            {
                return null;
            }
        }

        private static bool TryGetScalarDefault(
            PropertyInfo property,
            object? defaultInstance,
            out object? value)
        {
            value = null;
            if (defaultInstance is null || !IsScalarDefaultType(property.PropertyType))
            {
                return false;
            }

            value = property.GetValue(defaultInstance);
            return value is not null;
        }

        private static bool IsScalarDefaultType(Type rawType)
        {
            var type = Nullable.GetUnderlyingType(rawType) ?? rawType;
            return type == typeof(string) ||
                type == typeof(bool) ||
                type == typeof(byte) ||
                type == typeof(sbyte) ||
                type == typeof(short) ||
                type == typeof(ushort) ||
                type == typeof(int) ||
                type == typeof(uint) ||
                type == typeof(long) ||
                type == typeof(ulong) ||
                type == typeof(float) ||
                type == typeof(double) ||
                type == typeof(decimal) ||
                type.IsEnum;
        }

        private static string SerializeDefault(object? value) =>
            JsonSerializer.Serialize(value, OperatorJson.SerializerOptions);

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

        private enum SchemaDirection
        {
            Input,
            Output,
        }
    }

    private static Dictionary<string, object?> Primitive(string type, string? format = null)
    {
        var schema = new Dictionary<string, object?> { ["type"] = type };
        if (!string.IsNullOrWhiteSpace(format))
        {
            schema["format"] = format;
        }

        return schema;
    }
}
