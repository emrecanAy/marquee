namespace Marquee.Common.Results;

public enum ErrorKind
{
    Validation,
    NotFound,
    Conflict,
    Forbidden,
}

/// <summary>An expected failure. <see cref="Code"/> is the stable identifier clients switch on.</summary>
public sealed record Error(string Code, string Message, ErrorKind Kind)
{
    public static Error Validation(string code, string message) => new(code, message, ErrorKind.Validation);

    public static Error NotFound(string code, string message) => new(code, message, ErrorKind.NotFound);

    public static Error Conflict(string code, string message) => new(code, message, ErrorKind.Conflict);

    public static Error Forbidden(string code, string message) => new(code, message, ErrorKind.Forbidden);
}
