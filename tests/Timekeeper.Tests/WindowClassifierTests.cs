using Timekeeper.Core;
using Timekeeper.Core.Timesheets;

namespace Timekeeper.Tests;

public class WindowClassifierTests
{
    private static readonly DateTime T0 = new(2026, 10, 5, 7, 0, 0, DateTimeKind.Utc);
    private static readonly Client[] Clients = [new(1, "Bageriet i Lund AB", ["Bageriet"]), new(2, "Svensson Bygg AB", [])];
    private static readonly TimesheetOptions Options = new();

    private static SegmentEvidence Window(string title) =>
        new(
            new ActivitySegment
            {
                StartUtc = T0,
                EndUtc = T0.AddMinutes(10),
                State = ActivityState.Active,
                ProcessName = "msedge",
                WindowTitle = title,
            },
            OcrText: null,
            ScreenshotPath: null);

    private static WindowLabel Label(SegmentEvidence window, string client, LabelSource source) =>
        new(WindowSignature.Of(window.Segment), client, 0.9, source);

    [Fact]
    public async Task What_the_user_said_wins_over_a_keyword_match()
    {
        var window = Window("Bageriet - Fortnox");
        var classifier = new WindowClassifier(Options, null, null, Clients);

        var label = await classifier.ClassifyAsync(window, Label(window, "Svensson Bygg AB", LabelSource.User), CancellationToken.None);

        Assert.Equal("Svensson Bygg AB", label.Client);
        Assert.Equal(LabelSource.User, label.Source);
    }

    [Fact]
    public async Task Keyword_match_wins_over_a_remembered_model_answer()
    {
        var window = Window("Bageriet - Fortnox");
        var classifier = new WindowClassifier(Options, null, null, Clients);

        var label = await classifier.ClassifyAsync(window, Label(window, "Svensson Bygg AB", LabelSource.Model), CancellationToken.None);

        Assert.Equal("Bageriet i Lund AB", label.Client);
        Assert.Equal(LabelSource.Rule, label.Source);
    }

    [Fact]
    public async Task Remembered_model_answer_is_reused_without_calling_the_model()
    {
        var window = Window("Fortnox");
        var jev = new CountingDecisionModel(new Decision("Bageriet i Lund AB", 0.9));
        var classifier = new WindowClassifier(Options, jev, null, Clients);

        var label = await classifier.ClassifyAsync(window, Label(window, "Svensson Bygg AB", LabelSource.Model), CancellationToken.None);

        Assert.Equal("Svensson Bygg AB", label.Client);
        Assert.Equal(0, jev.Calls);
    }

    [Fact]
    public async Task Label_for_a_removed_client_is_classified_again()
    {
        var window = Window("Fortnox");
        var jev = new CountingDecisionModel(new Decision("Bageriet i Lund AB", 0.9));
        var classifier = new WindowClassifier(Options, jev, null, Clients);

        var label = await classifier.ClassifyAsync(window, Label(window, "Borttagen AB", LabelSource.User), CancellationToken.None);

        Assert.Equal("Bageriet i Lund AB", label.Client);
        Assert.Equal(LabelSource.Model, label.Source);
        Assert.Equal(1, jev.Calls);
    }

    [Fact]
    public async Task Without_models_the_result_is_marked_as_a_guess()
    {
        var classifier = new WindowClassifier(Options, null, null, Clients);

        var label = await classifier.ClassifyAsync(Window("Fortnox"), null, CancellationToken.None);

        Assert.Equal(WindowClassifier.InternalLabel, label.Client);
        Assert.Equal(LabelSource.Guess, label.Source);
        Assert.Equal(0, label.Confidence);
    }

    [Fact]
    public async Task Music_and_video_are_not_work_unless_the_user_says_otherwise()
    {
        var window = Window("Genomgång av K2 - YouTube");
        var youtube = window with { Segment = new ActivitySegment
        {
            StartUtc = window.Segment.StartUtc,
            EndUtc = window.Segment.EndUtc,
            State = ActivityState.Active,
            ProcessName = "msedge",
            WindowTitle = window.Segment.WindowTitle,
            Url = "youtube.com/watch?v=abc",
        } };
        var classifier = new WindowClassifier(Options, null, null, Clients);

        var guessed = await classifier.ClassifyAsync(youtube, null, CancellationToken.None);
        var corrected = await classifier.ClassifyAsync(youtube, Label(youtube, WindowClassifier.InternalLabel, LabelSource.User), CancellationToken.None);

        Assert.Equal((WindowClassifier.NotWorkLabel, LabelSource.Rule), (guessed.Client, guessed.Source));
        Assert.Equal(WindowClassifier.InternalLabel, corrected.Client);
    }
}
