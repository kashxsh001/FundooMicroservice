using Confluent.Kafka;
using Fundoo.Notes.Application.Interfaces;
using Microsoft.Extensions.Options;

namespace Fundoo.Notes.Api.Messaging;

public sealed class KafkaOptions
{
    public string BootstrapServers { get; set; } = "localhost:9092";
    public string Topic { get; set; } = "fundoo.note-events";
}

public sealed class KafkaNoteEventPublisher : INoteEventPublisher, IDisposable
{
    private readonly IProducer<string, string> _producer;
    private readonly KafkaOptions _options;

    public KafkaNoteEventPublisher(IOptions<KafkaOptions> options)
    {
        _options = options.Value;
        _producer = new ProducerBuilder<string, string>(new ProducerConfig
        {
            BootstrapServers = _options.BootstrapServers,
            ClientId = "fundoo-notes-api"
        }).Build();
    }

    public async Task PublishAsync(string eventType, Guid noteId, Guid userId,
        CancellationToken cancellationToken = default)
    {
        var value = System.Text.Json.JsonSerializer.Serialize(new NoteEvent(
            Guid.NewGuid(), eventType, noteId, userId, DateTime.UtcNow));
        await _producer.ProduceAsync(_options.Topic,
            new Message<string, string> { Key = userId.ToString(), Value = value }, cancellationToken);
    }

    public void Dispose() => _producer.Dispose();

    private sealed record NoteEvent(Guid EventId, string EventType, Guid NoteId, Guid UserId, DateTime OccurredAtUtc);
}
