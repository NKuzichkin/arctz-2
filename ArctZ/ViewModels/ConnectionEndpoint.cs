namespace ArctZ.ViewModels;

public enum ConnectionEndpointKind
{
    RealDevice,
    Demo
}

public sealed record ConnectionEndpoint(string Id, string DisplayName, ConnectionEndpointKind Kind, bool IsPaired = true)
{
    public string? StatusLabel => Kind switch
    {
        ConnectionEndpointKind.RealDevice => IsPaired ? "спарено" : "не спарено",
        _ => null,
    };
}
