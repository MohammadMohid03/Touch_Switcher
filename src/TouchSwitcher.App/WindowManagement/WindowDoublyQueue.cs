using TouchSwitcher.Configuration;
using TouchSwitcher.Interop;

namespace TouchSwitcher.WindowManagement;

internal sealed class WindowDoublyQueue
{
    private readonly object _gate = new();
    private readonly LinkedList<ApplicationGroup> _deque = new();
    private LinkedListNode<ApplicationGroup>? _currentNode;

    public IReadOnlyList<string> CurrentQueueKeys
    {
        get
        {
            lock (_gate)
            {
                return _deque.Select(n => n.Key).ToList();
            }
        }
    }

    public string? CurrentKey
    {
        get
        {
            lock (_gate)
            {
                return _currentNode?.Value.Key;
            }
        }
    }

    public void Sync(IReadOnlyList<SwitchableWindow> activeWindows, SwitchMode mode)
    {
        lock (_gate)
        {
            // Group active windows according to switch mode
            IEnumerable<IGrouping<string, SwitchableWindow>> grouped = mode == SwitchMode.Window
                ? activeWindows.GroupBy(w => w.Handle.ToString("X"))
                : activeWindows.GroupBy(w => w.ProcessName, StringComparer.OrdinalIgnoreCase);

            var activeGroups = grouped.ToDictionary(
                g => g.Key,
                g => new ApplicationGroup { Key = g.Key, Windows = g.ToList() },
                StringComparer.OrdinalIgnoreCase);

            // 1. Remove nodes from deque that are no longer active
            var node = _deque.First;
            while (node != null)
            {
                var next = node.Next;
                if (!activeGroups.ContainsKey(node.Value.Key))
                {
                    if (node == _currentNode)
                    {
                        _currentNode = next ?? node.Previous;
                    }
                    _deque.Remove(node);
                }
                else
                {
                    // Update the active windows list for this group
                    node.Value = activeGroups[node.Value.Key];
                }
                node = next;
            }

            // 2. Add newly opened windows/apps to the end of the deque
            foreach (var kvp in activeGroups)
            {
                bool exists = _deque.Any(n => string.Equals(n.Key, kvp.Key, StringComparison.OrdinalIgnoreCase));
                if (!exists)
                {
                    _deque.AddLast(kvp.Value);
                }
            }

            // 3. If _currentNode is null or was removed, point to current foreground window or First
            if (_currentNode == null || !_deque.Contains(_currentNode.Value))
            {
                IntPtr fg = NativeMethods.GetForegroundWindow();
                SwitchableWindow? fgWindow = activeWindows.FirstOrDefault(w => w.Handle == fg)
                                             ?? WindowEnumerator.FromHandle(fg);
                string? fgKey = fgWindow != null ? MruManager.MakeKey(fgWindow, mode) : null;

                _currentNode = FindNode(fgKey) ?? _deque.First;
            }
        }
    }

    public SwitchableWindow? MoveNext(IReadOnlyList<SwitchableWindow> activeWindows, SwitchMode mode)
    {
        lock (_gate)
        {
            Sync(activeWindows, mode);

            if (_deque.Count == 0)
            {
                return null;
            }

            if (_deque.Count == 1)
            {
                return PickWindow(_deque.First!.Value);
            }

            // If the user manually focused a different window, align _currentNode first
            AlignCurrentToForeground(activeWindows, mode);

            // Move forward in the doubly-linked queue (circular)
            _currentNode = _currentNode?.Next ?? _deque.First;

            return PickWindow(_currentNode!.Value);
        }
    }

    public SwitchableWindow? MovePrevious(IReadOnlyList<SwitchableWindow> activeWindows, SwitchMode mode)
    {
        lock (_gate)
        {
            Sync(activeWindows, mode);

            if (_deque.Count == 0)
            {
                return null;
            }

            if (_deque.Count == 1)
            {
                return PickWindow(_deque.First!.Value);
            }

            // If the user manually focused a different window, align _currentNode first
            AlignCurrentToForeground(activeWindows, mode);

            // Move backward in the doubly-linked queue (circular)
            _currentNode = _currentNode?.Previous ?? _deque.Last;

            return PickWindow(_currentNode!.Value);
        }
    }

    public (SwitchableWindow? Current, SwitchableWindow? Prev, SwitchableWindow? Next) GetCurrentTriad(
        IReadOnlyList<SwitchableWindow> activeWindows,
        SwitchMode mode)
    {
        lock (_gate)
        {
            Sync(activeWindows, mode);
            AlignCurrentToForeground(activeWindows, mode);

            if (_deque.Count == 0)
            {
                return (null, null, null);
            }

            if (_deque.Count == 1)
            {
                var cur = PickWindow(_deque.First!.Value);
                return (cur, null, null);
            }

            if (_currentNode == null)
            {
                _currentNode = _deque.First;
            }

            var current = PickWindow(_currentNode!.Value);
            var prevNode = _currentNode.Previous ?? _deque.Last;
            var nextNode = _currentNode.Next ?? _deque.First;

            var prev = PickWindow(prevNode!.Value);
            var next = PickWindow(nextNode!.Value);

            return (current, prev, next);
        }
    }

    private void AlignCurrentToForeground(IReadOnlyList<SwitchableWindow> activeWindows, SwitchMode mode)
    {
        IntPtr fg = NativeMethods.GetForegroundWindow();
        if (fg == IntPtr.Zero)
        {
            return;
        }

        // If foreground is already part of current node, stay on it
        if (_currentNode != null && _currentNode.Value.Windows.Any(w => w.Handle == fg))
        {
            return;
        }

        SwitchableWindow? fgWindow = activeWindows.FirstOrDefault(w => w.Handle == fg)
                                     ?? WindowEnumerator.FromHandle(fg);
        if (fgWindow != null)
        {
            string key = MruManager.MakeKey(fgWindow, mode);
            var match = FindNode(key);
            if (match != null)
            {
                _currentNode = match;
            }
        }
    }

    private LinkedListNode<ApplicationGroup>? FindNode(string? key)
    {
        if (string.IsNullOrEmpty(key)) return null;

        var node = _deque.First;
        while (node != null)
        {
            if (string.Equals(node.Value.Key, key, StringComparison.OrdinalIgnoreCase))
            {
                return node;
            }
            node = node.Next;
        }
        return null;
    }

    private static SwitchableWindow PickWindow(ApplicationGroup group)
    {
        SwitchableWindow? restored = group.Windows.FirstOrDefault(w => !NativeMethods.IsIconic(w.Handle));
        return restored ?? group.Windows[0];
    }
}
