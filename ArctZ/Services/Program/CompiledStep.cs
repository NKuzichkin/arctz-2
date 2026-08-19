using ArctZ.Services.Device;
using ArctZ.Services.Device.Commands;

namespace ArctZ.Services.Program;

public sealed record CompiledStep(
    int SegmentIndex,
    IDeviceCommand Command,
    double SegmentProgress,
    double EstimatedDurationSeconds,
    MachinePose Pose,
    bool IsDwellStep);
