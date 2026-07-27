namespace ArctZ.Services.Device;

public abstract record FluidNcLine;

public sealed record StatusReportLine(DeviceStatus Status) : FluidNcLine;

public sealed record OkLine : FluidNcLine;

public sealed record ErrorLine(int Code) : FluidNcLine;

public sealed record AlarmLine(int Code) : FluidNcLine;

public sealed record UnrecognizedLine(string Raw) : FluidNcLine;
