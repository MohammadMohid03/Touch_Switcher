using TouchSwitcher.Configuration;

namespace TouchSwitcher.WindowManagement;

internal sealed class ApplicationGroup
{
    public required string Key { get; init; }
    public required List<SwitchableWindow> Windows { get; init; }
}

internal sealed class MruManager
{
    private readonly object _gate = new();
    private readonly List<string> _mru = new();
    private bool _ignoreNextForeground;

    public void IgnoreNextForeground() => _ignoreNextForeground = true;

    public void RecordForeground(SwitchableWindow? window, SwitchMode mode)
    {
        if (window is null)
        {
            return;
        }

        if (_ignoreNextForeground)
        {
            _ignoreNextForeground = false;
            return;
        }

        string key = MakeKey(window, mode);
        lock (_gate)
        {
            _mru.RemoveAll(k => string.Equals(k, key, StringComparison.OrdinalIgnoreCase));
            _mru.Insert(0, key);
            if (_mru.Count > 40)
            {
                _mru.RemoveRange(40, _mru.Count - 40);
            }
        }
    }

    public IReadOnlyList<ApplicationGroup> BuildCycle(
        IReadOnlyList<SwitchableWindow> windows,
        SwitchMode mode)
    {
        IEnumerable<IGrouping<string, SwitchableWindow>> grouped = mode == SwitchMode.Window
            ? windows.GroupBy(w => w.Handle.ToString("X"))
            : windows.GroupBy(w => w.ProcessName, StringComparer.OrdinalIgnoreCase);

        var groups = grouped
            .Select(g => new ApplicationGroup
            {
                Key = g.Key,
                Windows = g.ToList()
            })
            .ToList();

        lock (_gate)
        {
            return groups
                .OrderBy(g =>
                {
                    int index = _mru.FindIndex(k => string.Equals(k, g.Key, StringComparison.OrdinalIgnoreCase));
                    return index < 0 ? int.MaxValue : index;
                })
                .ThenBy(g => g.Windows[0].Title, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }
    }

    public static string MakeKey(SwitchableWindow window, SwitchMode mode) =>
        mode == SwitchMode.Window ? window.Handle.ToString("X") : window.ProcessName;
}
