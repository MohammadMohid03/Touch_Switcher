using TouchSwitcher.Configuration;

namespace TouchSwitcher.Gesture;

internal enum SwipeDirection
{
    Left,
    Right,
    Up,
    Down
}

internal sealed class GestureRecognizer
{
    private readonly Dictionary<int, (int X, int Y)> _origins = new();
    private DateTime _originUtc;
    private DateTime _lastFireUtc = DateTime.MinValue;
    private bool _armed;
    private bool _firedThisGesture;
    private int _peakFingers;
    private bool _isDown;

    public event Action? ThreeFingersDown;
    public event Action? FingersLifted;
    public event Action<SwipeDirection>? SwipeRecognized;
    public event Action<double, double, int>? MovementUpdated;

    public void Process(TouchFrame frame, AppSettings settings)
    {
        int fingers = Math.Max(frame.ContactCount, frame.Contacts.Count);
        DateTime now = DateTime.UtcNow;

        if (fingers == 0)
        {
            if (_isDown)
            {
                _isDown = false;
                FingersLifted?.Invoke();
            }
            ResetGesture();
            MovementUpdated?.Invoke(0, 0, 0);
            return;
        }

        if (fingers != settings.RequiredFingers)
        {
            _peakFingers = Math.Max(_peakFingers, fingers);
            if (fingers > settings.RequiredFingers || _peakFingers > settings.RequiredFingers)
            {
                _firedThisGesture = true;
                _armed = false;
                if (_isDown)
                {
                    _isDown = false;
                    FingersLifted?.Invoke();
                }
            }

            if (fingers < settings.RequiredFingers && !_armed)
            {
                if (_isDown)
                {
                    _isDown = false;
                    FingersLifted?.Invoke();
                }
                ResetGesture();
            }

            MovementUpdated?.Invoke(0, 0, fingers);
            return;
        }

        // Exactly RequiredFingers (3 fingers) on touchpad
        if (!_isDown)
        {
            _isDown = true;
            ThreeFingersDown?.Invoke();
        }

        if (_firedThisGesture)
        {
            // If cooldown has elapsed, re-arm at current coordinates so the user can swipe again
            // without needing to lift their fingers
            if ((now - _lastFireUtc).TotalMilliseconds >= settings.CooldownMs && frame.Contacts.Count >= settings.RequiredFingers)
            {
                CaptureOrigins(frame.Contacts);
                _originUtc = now;
                _firedThisGesture = false;
                _armed = true;
            }
            else
            {
                return;
            }
        }

        if (!_armed)
        {
            CaptureOrigins(frame.Contacts);
            _originUtc = now;
            _armed = true;
            _peakFingers = fingers;
            MovementUpdated?.Invoke(0, 0, fingers);
            return;
        }

        if (!HasMoved(frame.Contacts, out double dx, out double dy))
        {
            MovementUpdated?.Invoke(0, 0, fingers);
            return;
        }

        MovementUpdated?.Invoke(dx, dy, fingers);

        double absX = Math.Abs(dx);
        double absY = Math.Abs(dy);
        double threshold = GetEffectiveDistance(settings);

        // Check if movement meets horizontal criteria
        bool isHorizontal = absX >= threshold && (absY == 0 || (absX / absY) >= settings.HorizontalVerticalRatio);

        // Check if movement meets vertical criteria
        // Touchpads are wide rectangles, so vertical travel is naturally shorter (~75% threshold).
        // Downward wrist motion also naturally has a slight diagonal arc, so we use a responsive 1.15 ratio.
        double vThreshold = Math.Max(20, threshold * 0.75);
        bool isVertical = absY >= vThreshold && (absX == 0 || (absY / absX) >= 1.15);

        if (!isHorizontal && !isVertical)
        {
            return;
        }

        if ((now - _lastFireUtc).TotalMilliseconds < settings.CooldownMs)
        {
            return;
        }

        _firedThisGesture = true;
        _lastFireUtc = now;

        SwipeDirection direction;
        if (isHorizontal && (!isVertical || absX >= absY))
        {
            direction = dx >= 0 ? SwipeDirection.Right : SwipeDirection.Left;
        }
        else
        {
            // In touchpad coordinates, moving upward toward the screen yields dy < 0
            direction = dy < 0 ? SwipeDirection.Up : SwipeDirection.Down;
        }

        SwipeRecognized?.Invoke(direction);
    }

    private void CaptureOrigins(IReadOnlyList<TouchContact> contacts)
    {
        _origins.Clear();
        foreach (TouchContact contact in contacts)
        {
            _origins[contact.ContactId] = (contact.X, contact.Y);
        }
    }

    private bool HasMoved(IReadOnlyList<TouchContact> contacts, out double dx, out double dy)
    {
        dx = 0;
        dy = 0;
        int matched = 0;

        foreach (TouchContact contact in contacts)
        {
            if (!_origins.TryGetValue(contact.ContactId, out (int X, int Y) origin))
            {
                continue;
            }

            dx += contact.X - origin.X;
            dy += contact.Y - origin.Y;
            matched++;
        }

        if (matched == 0)
        {
            return false;
        }

        dx /= matched;
        dy /= matched;
        return Math.Abs(dx) > 1 || Math.Abs(dy) > 1;
    }

    public static double GetEffectiveDistance(AppSettings settings)
    {
        // Sensitivity 0-100. Higher sensitivity -> shorter required distance.
        double factor = 1.4 - (settings.Sensitivity / 100.0);
        factor = Math.Clamp(factor, 0.45, 1.4);
        return Math.Max(24, settings.MinSwipeDistance * factor);
    }

    private void ResetGesture()
    {
        _origins.Clear();
        _armed = false;
        _firedThisGesture = false;
        _peakFingers = 0;
    }
}
