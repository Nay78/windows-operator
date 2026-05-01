using WindowsOperator.Core.Contracts;

namespace WindowsOperator.Core;

public sealed class OperatorFailureException : Exception
{
    public OperatorFailureException(OperatorError error)
        : base(error.Message)
    {
        Error = error;
    }

    public OperatorError Error { get; }
}
