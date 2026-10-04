namespace Timekeeper.Core;

/// <summary>A screenshot of the foreground window, with the text recognised in it.</summary>
public sealed class Screenshot
{
    public long Id { get; set; }
    public required long SegmentId { get; init; }
    public required DateTime TakenUtc { get; init; }
    public required string FilePath { get; init; }
    public string? OcrText { get; init; }
}
