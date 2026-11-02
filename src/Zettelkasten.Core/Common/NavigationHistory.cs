namespace Zettelkasten.Core.Common;

public sealed class NavigationHistory
{
    private readonly List<string> _back = [];
    private readonly List<string> _forward = [];

    public string? Current { get; private set; }
    public bool CanGoBack => _back.Count > 0;
    public bool CanGoForward => _forward.Count > 0;

    public void Visit(string id)
    {
        if (Current == id) return;
        if (Current is not null) _back.Add(Current);
        Current = id;
        _forward.Clear();
    }

    public string? GoBack() => Move(_back, _forward);

    public string? GoForward() => Move(_forward, _back);

    public string? Remove(string id)
    {
        _back.RemoveAll(x => x == id);
        _forward.RemoveAll(x => x == id);
        if (Current == id)
        {
            Current = null;
            if (_back.Count > 0) { Current = _back[^1]; _back.RemoveAt(_back.Count - 1); }
        }
        RemoveAdjacentDuplicates(_back);
        return Current;
    }

    private string? Move(List<string> from, List<string> to)
    {
        if (from.Count == 0) return null;
        if (Current is not null) to.Add(Current);
        Current = from[^1];
        from.RemoveAt(from.Count - 1);
        return Current;
    }

    private void RemoveAdjacentDuplicates(List<string> list)
    {
        for (var i = list.Count - 1; i > 0; i--)
            if (list[i] == list[i - 1]) list.RemoveAt(i);
        if (list.Count > 0 && list[^1] == Current) list.RemoveAt(list.Count - 1);
    }
}
