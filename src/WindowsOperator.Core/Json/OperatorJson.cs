using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using WindowsOperator.Core.Contracts;

namespace WindowsOperator.Core.Json;

public static class OperatorJson
{
    public static JsonSerializerOptions SerializerOptions { get; } = Create();
    public static JsonSerializerOptions PublicSerializerOptions { get; } = CreatePublic();

    public static void Configure(JsonSerializerOptions options)
    {
        options.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        options.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
    }

    public static void ConfigureHttp(JsonSerializerOptions options)
    {
        Configure(options);
        ConfigurePublicResolver(options);
    }

    private static void ConfigurePublicResolver(JsonSerializerOptions options)
    {
        var resolver = new DefaultJsonTypeInfoResolver();
        resolver.Modifiers.Add(typeInfo =>
        {
            for (var index = typeInfo.Properties.Count - 1; index >= 0; index--)
            {
                var provider = typeInfo.Properties[index].AttributeProvider;
                if (provider?.IsDefined(typeof(OperatorInternalAttribute), inherit: true) == true)
                {
                    typeInfo.Properties.RemoveAt(index);
                }
            }
        });
        options.TypeInfoResolver = resolver;
    }

    private static JsonSerializerOptions Create()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        Configure(options);
        return options;
    }

    private static JsonSerializerOptions CreatePublic()
    {
        var options = Create();
        ConfigurePublicResolver(options);
        return options;
    }
}
