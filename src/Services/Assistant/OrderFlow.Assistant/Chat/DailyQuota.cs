namespace OrderFlow.Assistant.Chat;

/// <summary>
/// Caps assistant requests per UTC day for the whole deployment, so the free model quota is shared fairly
/// and a runaway client can't exhaust it. In memory: a restart resets the count, which is fine for a demo.
/// </summary>
public sealed class DailyQuota(AssistantOptions options, TimeProvider clock)
{
    private readonly Lock _lock = new();
    private DateOnly _day;
    private int _used;

    public bool TryTake()
    {
        lock (_lock)
        {
            var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
            if (today != _day)
            {
                _day = today;
                _used = 0;
            }
            if (_used >= options.DailyLimit) return false;
            _used++;
            return true;
        }
    }
}
