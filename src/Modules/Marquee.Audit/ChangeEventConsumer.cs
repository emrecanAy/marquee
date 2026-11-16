using Confluent.Kafka;
using Marquee.Common.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Marquee.Audit;

/// <summary>
/// Stores every change Debezium captures from the booking and payments tables. The application
/// code writes no audit records itself: whatever commits to those tables shows up here, including
/// changes made by a migration script or by hand in psql.
/// </summary>
internal sealed class ChangeEventConsumer(
    IServiceScopeFactory scopes,
    IOptions<AuditOptions> options,
    ILogger<ChangeEventConsumer> logger) : BackgroundService
{
    private readonly AuditOptions _options = options.Value;

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            logger.LogInformation("Audit trail is off: no Kafka bootstrap servers configured");
            return Task.CompletedTask;
        }

        // Consume() blocks, so the loop gets its own thread instead of a thread-pool one.
        return Task.Factory.StartNew(
            () => ConsumeAsync(stoppingToken), stoppingToken, TaskCreationOptions.LongRunning, TaskScheduler.Default)
            .Unwrap();
    }

    private async Task ConsumeAsync(CancellationToken ct)
    {
        var config = new ConsumerConfig
        {
            BootstrapServers = _options.KafkaBootstrapServers,
            GroupId = _options.ConsumerGroup,
            AutoOffsetReset = AutoOffsetReset.Earliest,
            // Offsets are committed only after the row is stored: at-least-once, deduplicated by Position.
            EnableAutoCommit = false,
            // Pick up topics for newly captured tables without a restart.
            TopicMetadataRefreshIntervalMs = 5000,
            AllowAutoCreateTopics = false,
        };

        using var consumer = new ConsumerBuilder<Ignore, string>(config)
            // librdkafka writes its own diagnostics to stderr unless they are given somewhere to go.
            .SetLogHandler((_, message) => logger.LogDebug("librdkafka: {Message}", message.Message))
            .Build();
        consumer.Subscribe(_options.TopicPattern);

        try
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    var message = consumer.Consume(ct);
                    await StoreAsync(message, ct);
                    consumer.Commit(message);
                }
                catch (ConsumeException ex) when (ex.Error.Code == ErrorCode.UnknownTopicOrPart)
                {
                    // Debezium creates a table's topic on its first change. Until then there is nothing to read.
                    await Task.Delay(TimeSpan.FromSeconds(2), ct);
                }
                catch (ConsumeException ex)
                {
                    logger.LogWarning("Kafka consume error: {Reason}", ex.Error.Reason);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogError(ex, "Storing a change event failed; retrying");
                    await Task.Delay(TimeSpan.FromSeconds(2), ct);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Shutting down.
        }
        finally
        {
            consumer.Close();
        }
    }

    private async Task StoreAsync(ConsumeResult<Ignore, string> message, CancellationToken ct)
    {
        // Deletes are followed by a tombstone (null value) that carries no information.
        if (message.Message.Value is null)
        {
            return;
        }

        var position = $"{message.Topic}/{message.Partition.Value}/{message.Offset.Value}";
        var change = ChangeEvent.Parse(position, message.Message.Value);
        if (change is null)
        {
            return;
        }

        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AuditDbContext>();
        db.ChangeEvents.Add(change);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.IsUniqueViolation())
        {
            // Already stored before a restart; the offset just had not been committed yet.
        }
    }
}
