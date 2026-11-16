namespace Marquee.Audit;

public sealed class AuditOptions
{
    /// <summary>Kafka brokers carrying the Debezium change events. Leave empty to run without the audit trail.</summary>
    public string? KafkaBootstrapServers { get; set; }

    /// <summary>Regex subscription; matches the topics Debezium creates per captured table.</summary>
    public string TopicPattern { get; set; } = @"^marquee\.(booking|payments)\..*";

    public string ConsumerGroup { get; set; } = "marquee-audit";

    public bool Enabled => !string.IsNullOrWhiteSpace(KafkaBootstrapServers);
}
