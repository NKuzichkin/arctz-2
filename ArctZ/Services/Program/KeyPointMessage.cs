namespace ArctZ.Services.Program;

public enum MessageLevel
{
    Info,
    Warning,
    Error,
}

/// <summary>One entry in a key point's message history, shown via the "Сообщения" menu item.</summary>
public sealed record KeyPointMessage(MessageLevel Level, string Text);
