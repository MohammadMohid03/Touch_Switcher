using System.Windows.Forms;

namespace TouchSwitcher.Gesture;

internal sealed class TouchFrameCoalescer : IDisposable
{
    private readonly Dictionary<int, TouchContact> _currentScanContacts = new();
    private uint _currentScanTime;
    private DateTime _lastContactUtc = DateTime.MinValue;
    private readonly System.Windows.Forms.Timer _watchdogTimer;

    public event Action<TouchFrame>? FrameReady;

    public TouchFrameCoalescer()
    {
        // 140ms watchdog: if fingers are lifted and hardware does not send a ContactCount=0 report,
        // this ensures the gesture recognizer resets cleanly without interrupting resting idle fingers.
        _watchdogTimer = new System.Windows.Forms.Timer
        {
            Interval = 140
        };
        _watchdogTimer.Tick += OnWatchdogTick;
    }

    public void ProcessRawReport(TouchFrame report)
    {
        DateTime now = DateTime.UtcNow;
        double elapsedMs = (now - _lastContactUtc).TotalMilliseconds;

        // If hardware explicitly reports 0 contacts, all fingers are lifted
        if (report.ContactCount == 0 && report.Contacts.Count == 0)
        {
            FlushAndReset();
            return;
        }

        foreach (var contact in report.Contacts)
        {
            // A new scan has begun if:
            // 1. ScanTime changed (and is non-zero)
            // 2. We already recorded this ContactId in the current scan batch
            // 3. More than 4ms have passed since the last contact (inter-scan delay)
            bool scanTimeChanged = report.ScanTime != 0 && report.ScanTime != _currentScanTime;
            bool idAlreadyPresent = _currentScanContacts.ContainsKey(contact.ContactId);
            bool timeGap = _currentScanContacts.Count > 0 && elapsedMs >= 4.0;

            if (_currentScanContacts.Count > 0 && (scanTimeChanged || idAlreadyPresent || timeGap))
            {
                // The previous scan is complete — emit it with all accumulated contacts!
                EmitFrame();
                _currentScanContacts.Clear();
            }

            _currentScanTime = report.ScanTime;
            _currentScanContacts[contact.ContactId] = contact;
            _lastContactUtc = now;
            elapsedMs = 0;
        }

        // Restart watchdog timer
        _watchdogTimer.Stop();
        _watchdogTimer.Start();
    }

    private void EmitFrame()
    {
        if (_currentScanContacts.Count == 0)
        {
            return;
        }

        var contactsList = _currentScanContacts.Values.ToList();
        var frame = new TouchFrame
        {
            ContactCount = contactsList.Count,
            Contacts = contactsList,
            ScanTime = _currentScanTime,
            TimestampUtc = DateTime.UtcNow
        };

        FrameReady?.Invoke(frame);
    }

    private void FlushAndReset()
    {
        _watchdogTimer.Stop();

        // If there was a pending scan batch, emit it first
        if (_currentScanContacts.Count > 0)
        {
            EmitFrame();
            _currentScanContacts.Clear();
        }

        _currentScanTime = 0;

        // Emit empty frame so recognizer resets
        var emptyFrame = new TouchFrame
        {
            ContactCount = 0,
            Contacts = Array.Empty<TouchContact>(),
            ScanTime = 0,
            TimestampUtc = DateTime.UtcNow
        };

        FrameReady?.Invoke(emptyFrame);
    }

    private void OnWatchdogTick(object? sender, EventArgs e)
    {
        _watchdogTimer.Stop();
        FlushAndReset();
    }

    public void Dispose()
    {
        _watchdogTimer.Stop();
        _watchdogTimer.Dispose();
    }
}
