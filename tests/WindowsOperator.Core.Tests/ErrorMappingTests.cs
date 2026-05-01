namespace WindowsOperator.Core.Tests;

public sealed class ErrorMappingTests
{
    [Fact]
    public void OperatorErrors_IncludeRemediation()
    {
        var error = WindowsOperator.Core.OperatorErrors.ElevatedTarget("detail");

        Assert.Equal(WindowsOperator.Core.ErrorCodes.ElevatedTarget, error.Code);
        Assert.False(string.IsNullOrWhiteSpace(error.Remediation));
        Assert.Equal("detail", error.Details?["detail"]);
    }
}
