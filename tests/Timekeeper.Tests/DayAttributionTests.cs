using Timekeeper.Core;
using Timekeeper.Core.Timesheets;

namespace Timekeeper.Tests;

public class DayAttributionTests
{
    private static readonly DateTime T0 = new(2026, 10, 5, 7, 0, 0, DateTimeKind.Utc);
    private static readonly Client[] Clients = [new(1, "Bageriet i Lund AB", ["Bageriet"]), new(2, "Svensson Bygg AB", ["Svensson"])];
    private static readonly WindowClassifier Classifier = new(new TimesheetOptions(), null, null, Clients);
    private static readonly Dictionary<string, WindowLabel> NoWindowLabels = [];
    private static readonly Dictionary<long, WindowLabel> NoOccasionLabels = [];

    private static long _nextId;

    private static SegmentEvidence Window(double start, double minutes, string title, string process = "EXCEL", string? ocr = null) =>
        new(
            new ActivitySegment
            {
                Id = Interlocked.Increment(ref _nextId),
                StartUtc = T0.AddMinutes(start),
                EndUtc = T0.AddMinutes(start + minutes),
                State = ActivityState.Active,
                ProcessName = process,
                WindowTitle = title,
            },
            ocr,
            ScreenshotPath: null);

    private static SegmentEvidence Fortnox(double start, double minutes, string? ocr = null) =>
        Window(start, minutes, "Bokföring - Fortnox", process: "msedge", ocr: ocr);

    private static IReadOnlyList<AttributedSegment> Attribute(
        IReadOnlyList<SegmentEvidence> day,
        Dictionary<string, WindowLabel>? windowLabels = null,
        Dictionary<long, WindowLabel>? occasionLabels = null) =>
        DayAttribution.Attribute(day, Classifier, windowLabels ?? NoWindowLabels, occasionLabels ?? NoOccasionLabels);

    [Fact]
    public void Quick_look_at_a_shared_window_between_work_for_one_client_belongs_to_that_client()
    {
        var day = Attribute([Window(0, 20, "Bageriet.xlsx"), Fortnox(20, 4), Window(24, 20, "Bageriet.xlsx")]);

        Assert.Equal(("Bageriet i Lund AB", AttributionReason.WorkBeforeAndAfter), (day[1].Client, day[1].Reason));
    }

    [Fact]
    public void Between_two_clients_the_nearer_work_decides()
    {
        var day = Attribute([Window(0, 20, "Bageriet.xlsx"), Fortnox(21, 3), Window(30, 20, "Svensson moms.xlsx")]);

        Assert.Equal(("Bageriet i Lund AB", AttributionReason.WorkBefore), (day[1].Client, day[1].Reason));
    }

    [Fact]
    public void Work_right_after_internal_work_does_not_inherit_an_earlier_client()
    {
        var meeting = Window(20, 5, "Personalmöte.docx", process: "WINWORD");
        var internalAnswer = new Dictionary<string, WindowLabel>
        {
            [WindowSignature.Of(meeting.Segment)] = new(WindowSignature.Of(meeting.Segment), WindowClassifier.InternalLabel, 1, LabelSource.User),
        };

        var day = Attribute([Window(0, 20, "Bageriet.xlsx"), meeting, Fortnox(25, 3)], internalAnswer);

        Assert.Equal(AttributionReason.None, day[2].Reason);
    }

    [Fact]
    public void Long_stretches_and_distant_work_do_not_inherit()
    {
        var day = Attribute([Window(0, 20, "Bageriet.xlsx"), Fortnox(20, 25), Fortnox(60, 3)]);

        Assert.Equal(AttributionReason.None, day[1].Reason);
        Assert.Equal(AttributionReason.None, day[2].Reason);
    }

    [Fact]
    public void Client_named_at_the_top_of_the_screen_is_stronger_than_one_further_down()
    {
        var header = string.Join('\n', ["Fortnox", "Bageriet i Lund AB", "Bokföring", .. Enumerable.Repeat("rad", 20)]);
        var body = string.Join('\n', [.. Enumerable.Repeat("rad", 20), "Leverantör: Svensson Bygg AB"]);
        var both = header + "\nSvensson Bygg AB";

        var day = Attribute([Fortnox(0, 30, header), Fortnox(40, 30, body), Fortnox(80, 30, both)]);

        Assert.Equal(("Bageriet i Lund AB", 0.85, AttributionReason.NameOnScreen), (day[0].Client, day[0].Confidence, day[0].Reason));
        Assert.Equal(("Svensson Bygg AB", 0.7), (day[1].Client, day[1].Confidence));
        Assert.Equal(("Bageriet i Lund AB", AttributionReason.NameOnScreen), (day[2].Client, day[2].Reason));
    }

    [Fact]
    public void Shared_windows_use_the_label_for_the_occasion_not_the_window()
    {
        var first = Fortnox(0, 30);
        var second = Fortnox(40, 30);
        var signature = WindowSignature.Of(first.Segment);

        var day = Attribute(
            [first, second],
            new() { [signature] = new(signature, "Svensson Bygg AB", 1, LabelSource.User) },
            new() { [second.Segment.Id] = new(signature, "Bageriet i Lund AB", 1, LabelSource.User) });

        Assert.Equal(AttributionReason.None, day[0].Reason);
        Assert.Equal(("Bageriet i Lund AB", AttributionReason.UserAnswer), (day[1].Client, day[1].Reason));
    }

    [Fact]
    public void Surroundings_describe_the_work_before_and_after_with_known_clients()
    {
        var day = Attribute([Window(0, 20, "Bageriet.xlsx"), Fortnox(20, 4), Window(24, 6, "Mejl.docx", process: "WINWORD")]);

        var text = DayAttribution.Surroundings(day, 1);

        Assert.Contains("20 min, 0 min före: EXCEL · Bageriet.xlsx → Bageriet i Lund AB", text);
        Assert.Contains("6 min, 0 min efter: WINWORD · Mejl.docx", text);
    }
}
