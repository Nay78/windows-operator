using System.Reflection;
using System.Reflection.Emit;
using WindowsOperator.Core.Services;

namespace WindowsOperator.Core.Tests;

public sealed class RuntimeBuildIdentityReaderTests
{
    [Fact]
    public void Read_UsesAssemblyIdentityAndEmbeddedRevision()
    {
        var assembly = CreateAssembly(
            "3.2.1+abcdef123456",
            new KeyValuePair<string, string>("SourceRevisionId", "abcdef123456"));

        var result = RuntimeBuildIdentityReader.Read(assembly);

        Assert.Equal("3.2.1+abcdef123456", result.InformationalVersion);
        Assert.Equal("3.2.1.0", result.AssemblyVersion);
        Assert.Equal("abcdef123456", result.SourceRevision);
    }

    [Fact]
    public void Read_ExtractsSdkInformationalVersionRevision()
    {
        var assembly = CreateAssembly("3.2.1+fedcba987654");

        var result = RuntimeBuildIdentityReader.Read(assembly);

        Assert.Equal("fedcba987654", result.SourceRevision);
    }

    [Fact]
    public void Read_UsesExplicitUnavailableValues()
    {
        var assembly = AssemblyBuilder.DefineDynamicAssembly(
            new AssemblyName($"WindowsOperator.NoBuildIdentity.{Guid.NewGuid():N}")
            {
                Version = new Version(2, 0, 0, 0),
            },
            AssemblyBuilderAccess.Run);

        var result = RuntimeBuildIdentityReader.Read(assembly);

        Assert.Equal(RuntimeBuildIdentityReader.Unavailable, result.InformationalVersion);
        Assert.Equal("2.0.0.0", result.AssemblyVersion);
        Assert.Equal(RuntimeBuildIdentityReader.Unavailable, result.SourceRevision);
    }

    private static AssemblyBuilder CreateAssembly(
        string informationalVersion,
        params KeyValuePair<string, string>[] metadata)
    {
        var assembly = AssemblyBuilder.DefineDynamicAssembly(
            new AssemblyName($"WindowsOperator.BuildIdentity.{Guid.NewGuid():N}")
            {
                Version = new Version(3, 2, 1, 0),
            },
            AssemblyBuilderAccess.Run);
        assembly.SetCustomAttribute(
            new CustomAttributeBuilder(
                typeof(AssemblyInformationalVersionAttribute).GetConstructor([typeof(string)])!,
                [informationalVersion]));

        foreach (var item in metadata)
        {
            assembly.SetCustomAttribute(
                new CustomAttributeBuilder(
                    typeof(AssemblyMetadataAttribute).GetConstructor([typeof(string), typeof(string)])!,
                    [item.Key, item.Value]));
        }

        return assembly;
    }
}
