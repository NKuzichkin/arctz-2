namespace ArctZ.Services.Program;

public sealed record ProgramSegment(int Index, Waypoint From, Waypoint To, TransitionSettings Transition);
