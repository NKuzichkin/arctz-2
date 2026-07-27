using System;
using ArctZ.Services.Device;

namespace ArctZ.Services.Program;

public sealed record Waypoint(Guid Id, string? Label, MachinePose Pose);
