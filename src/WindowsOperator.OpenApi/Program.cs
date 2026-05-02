using System.Text.Json;
using WindowsOperator.Core.Contracts;
using WindowsOperator.Core.Json;

var outputPath = args.Length > 0
    ? args[0]
    : Path.Combine("openapi", "windows-operator.openapi.json");

var directory = Path.GetDirectoryName(outputPath);
if (!string.IsNullOrWhiteSpace(directory))
{
    Directory.CreateDirectory(directory);
}

await File.WriteAllTextAsync(
    outputPath,
    JsonSerializer.Serialize(OperatorOpenApi.Document, OperatorJson.SerializerOptions) + Environment.NewLine);

Console.WriteLine(outputPath);
