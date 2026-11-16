using System.Text.Json;

namespace Marquee.Audit;

/// <summary>One committed row change, as read from the PostgreSQL write-ahead log by Debezium.</summary>
internal sealed class ChangeEvent
{
    public long Id { get; private set; }

    /// <summary>Kafka coordinates of the source message. Unique, which makes redelivery harmless.</summary>
    public required string Position { get; init; }

    public required string Table { get; init; }

    public required string Operation { get; init; }

    public string? Before { get; init; }

    public string? After { get; init; }

    public required DateTimeOffset CommittedAt { get; init; }

    /// <summary>Reads a Debezium envelope (JSON converter, schemas disabled).</summary>
    public static ChangeEvent? Parse(string position, string envelope)
    {
        using var document = JsonDocument.Parse(envelope);
        var root = document.RootElement;
        if (!root.TryGetProperty("op", out var op) || !root.TryGetProperty("source", out var source))
        {
            return null;
        }

        return new ChangeEvent
        {
            Position = position,
            Table = $"{source.GetProperty("schema").GetString()}.{source.GetProperty("table").GetString()}",
            Operation = op.GetString() switch
            {
                "c" => "insert",
                "u" => "update",
                "d" => "delete",
                "r" => "snapshot",
                var other => other ?? "unknown",
            },
            Before = RawOrNull(root, "before"),
            After = RawOrNull(root, "after"),
            CommittedAt = DateTimeOffset.FromUnixTimeMilliseconds(source.GetProperty("ts_ms").GetInt64()),
        };
    }

    private static string? RawOrNull(JsonElement root, string property) =>
        root.TryGetProperty(property, out var value) && value.ValueKind != JsonValueKind.Null
            ? value.GetRawText()
            : null;
}
