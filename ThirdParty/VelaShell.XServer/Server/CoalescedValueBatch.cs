namespace VelaShell.XServer.Server;

/// <summary>收拢尚未处理的同键更新,每个键只保留最新值,并保证同一时刻最多排入一次 flush。</summary>
internal sealed class CoalescedValueBatch<TKey, TValue> where TKey : notnull
{
    private readonly object _gate = new();
    private readonly Dictionary<TKey, TValue> _pending = [];
    private bool _flushQueued;

    /// <returns>调用方是否需要为这一批更新排入 flush 工作项。</returns>
    public bool Set(TKey key, TValue value)
    {
        lock (_gate)
        {
            _pending[key] = value;
            if (_flushQueued)
            {
                return false;
            }

            _flushQueued = true;
            return true;
        }
    }

    public KeyValuePair<TKey, TValue>[] Take()
    {
        lock (_gate)
        {
            KeyValuePair<TKey, TValue>[] values = [.. _pending];
            _pending.Clear();
            _flushQueued = false;
            return values;
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _pending.Clear();
            _flushQueued = false;
        }
    }
}
