using ArctZ.Services.Device.Commands;

namespace ArctZ.Services.Device;

public sealed record CommandRejectedEventArgs(GCodeLineCommand Command, int? ErrorCode);
