namespace ApacheBalancerWasmInterface.Services;

/// <summary>Raised when the BFF rejects a request (HTTP 400/502 with a ProblemDetails body).</summary>
public sealed class BalancerApiException : Exception
{
    public BalancerApiException(string message)
        : base(message)
    {
    }
}
