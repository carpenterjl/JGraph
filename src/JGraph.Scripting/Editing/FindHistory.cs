namespace JGraph.Scripting.Editing;

/// <summary>
/// The find box's history (R2025b's <c>FindPreference._setHistoryList</c>): the most recent first, a text moved to the front when searched
/// again, the oldest dropped past the limit. The drop-down (<c>getFindMenuItems</c>) leaves empty texts out and shows at most the limit.
/// </summary>
public sealed class FindHistory
{
    /// <summary>How many texts are kept (<c>_MAX_HISTORY_LIMIT</c>).</summary>
    public const int Limit = 20;

    private readonly List<string> _texts = [];

    /// <summary>The texts, the most recent first.</summary>
    public IReadOnlyList<string> Texts => _texts;

    /// <summary>The most recent text, or "" (<c>getFindText</c>).</summary>
    public string Latest => _texts.Count == 0 ? string.Empty : _texts[0];

    /// <summary>The drop-down's items (<c>getFindMenuItems</c>): the texts that are not empty, at most <see cref="Limit"/>.</summary>
    public IReadOnlyList<string> MenuItems => [.. _texts.Where(static t => t.Length > 0).Take(Limit)];

    /// <summary>Puts <paramref name="text"/> first.</summary>
    public void Add(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        int at = _texts.IndexOf(text);
        if (at >= 0)
        {
            _texts.RemoveAt(at);
        }

        // R2025b drops the oldest only past the limit, so the list holds one more for a moment.
        if (_texts.Count > Limit)
        {
            _texts.RemoveAt(_texts.Count - 1);
        }

        _texts.Insert(0, text);
    }

    /// <summary>Replaces the texts (<c>refillFindHistory</c>).</summary>
    public void Refill(IEnumerable<string> texts)
    {
        ArgumentNullException.ThrowIfNull(texts);
        _texts.Clear();
        _texts.AddRange(texts);
    }

    /// <summary>Forgets every text.</summary>
    public void Clear() => _texts.Clear();
}
