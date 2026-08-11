using System;

namespace ArctZ.Services.Program;

public sealed record ProgramSummary(Guid Id, string Name, DateTimeOffset CreatedAt, DateTimeOffset ModifiedAt);
