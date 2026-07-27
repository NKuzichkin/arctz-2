namespace ArctZ.ViewModels;

public enum ConnectionEndpointKind
{
    RealDevice,
    Demo
}

public sealed record ConnectionEndpoint(string Id, string DisplayName, ConnectionEndpointKind Kind);
