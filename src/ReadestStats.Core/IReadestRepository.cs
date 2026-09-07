namespace ReadestStats.Core;

public interface IReadestRepository
{
    string DatabasePath { get; }
    Task<SchemaValidation> ValidateAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Book>> GetBooksAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ReadingEvent>> GetEventsAsync(DateTimeOffset? start = null, DateTimeOffset? end = null, long? bookId = null, CancellationToken cancellationToken = default);
}
