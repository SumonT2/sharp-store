namespace Shop.Infrastructure.Importing;

public sealed class ImportResult
{
    private const int MaxMessages = 300;
    private readonly List<string> _messages = new();

    public int ImportLogId { get; set; }
    public int TotalRows { get; set; }
    public int Created { get; set; }
    public int Updated { get; set; }
    public int Failed { get; set; }
    public int Repriced { get; set; }
    public IReadOnlyList<string> Messages => _messages;

    public void Error(int line, string message)
    {
        Failed++;
        Note(line, message);
    }

    public void Note(int line, string message)
    {
        if (_messages.Count < MaxMessages)
            _messages.Add(line > 0 ? $"Line {line}: {message}" : message);
        else if (_messages.Count == MaxMessages)
            _messages.Add("… more messages were omitted.");
    }
}
