namespace TouchSwitcher.Gesture;

internal readonly record struct TouchContact(int ContactId, int X, int Y);

internal sealed class TouchFrame
{
    public required int ContactCount { get; init; }
    public required IReadOnlyList<TouchContact> Contacts { get; init; }
    public uint ScanTime { get; init; }
    public DateTime TimestampUtc { get; init; } = DateTime.UtcNow;
}
