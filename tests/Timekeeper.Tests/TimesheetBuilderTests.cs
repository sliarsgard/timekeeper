using Timekeeper.Core;
using Timekeeper.Core.Timesheets;

namespace Timekeeper.Tests;

public class TimesheetBuilderTests
{
    private static readonly DateTime T0 = new(2026, 10, 5, 7, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly Day = new(2026, 10, 5);

    private static readonly Client[] Clients =
    [
        new(1, "Bageriet i Lund AB", ["Bageriet"]),
        new(2, "Svensson Bygg AB", ["556677-8899"]),
    ];

    // Most tests here are about attribution; rounding to the nearest step keeps their numbers simple.
    private static readonly TimesheetOptions Options = new() { RoundingMinutes = 15, RoundUp = false };

    private static long _nextId;

    private static SegmentEvidence Segment(
        int startMinute,
        int minutes,
        string title,
        string? ocr = null,
        string process = "EXCEL",
        ActivityState state = ActivityState.Active) =>
        new(
            new ActivitySegment
            {
                Id = Interlocked.Increment(ref _nextId),
                StartUtc = T0.AddMinutes(startMinute),
                EndUtc = T0.AddMinutes(startMinute + minutes),
                State = state,
                ProcessName = process,
                WindowTitle = title,
            },
            ocr,
            ScreenshotPath: ocr is null ? null : $"{title}.jpg");

    [Fact]
    public async Task Client_named_in_the_window_title_is_matched_without_any_model()
    {
        var builder = new TimesheetBuilder(Options, decisionModel: null, languageModel: null);

        var entries = await builder.BuildAsync(Day, [Segment(0, 50, "Bageriet bokslut 2026.xlsx - Excel")], Clients);

        var entry = Assert.Single(entries);
        Assert.Equal("Bageriet i Lund AB", entry.Client);
        Assert.Equal(45, entry.Minutes);
        Assert.Equal("Löpande bokföring", entry.Activity);
        Assert.Equal("Bageriet bokslut 2026.xlsx - Excel", entry.Comment);
    }

    [Fact]
    public async Task Idle_time_is_not_logged()
    {
        var builder = new TimesheetBuilder(Options, null, null);

        var entries = await builder.BuildAsync(
            Day,
            [Segment(0, 30, "Bageriet.xlsx"), Segment(30, 60, "", state: ActivityState.Idle)],
            Clients);

        Assert.Equal(30, Assert.Single(entries).Minutes);
    }

    [Fact]
    public async Task Confident_decision_model_is_used_without_asking_the_language_model()
    {
        var jev = new FakeDecisionModel(new Decision("Svensson Bygg AB", 0.9));
        var luna = new FakeLanguageModel(new Decision("Bageriet i Lund AB", 0.9));
        var builder = new TimesheetBuilder(Options, jev, luna);

        var entries = await builder.BuildAsync(Day, [Segment(0, 60, "Fortnox", "Leverantörsfakturor")], Clients);

        Assert.Equal("Svensson Bygg AB", Assert.Single(entries).Client);
        Assert.DoesNotContain(luna.Questions, q => q.Question.Contains("client company"));
    }

    [Fact]
    public async Task Unsure_decision_model_escalates_to_the_language_model_with_the_screenshot()
    {
        var jev = new FakeDecisionModel(new Decision("Svensson Bygg AB", 0.4));
        var luna = new FakeLanguageModel(new Decision("Bageriet i Lund AB", 0.85));
        var builder = new TimesheetBuilder(Options, jev, luna);

        var entries = await builder.BuildAsync(Day, [Segment(0, 60, "Fortnox", "Kundfakturor")], Clients);

        Assert.Equal("Bageriet i Lund AB", Assert.Single(entries).Client);
        Assert.Contains(luna.Questions, q => q.ImagePath == "Fortnox.jpg");
    }

    [Fact]
    public async Task Screenshots_are_not_sent_when_disabled()
    {
        var luna = new FakeLanguageModel(new Decision("Bageriet i Lund AB", 0.85));
        var builder = new TimesheetBuilder(Options with { SendScreenshots = false }, null, luna);

        await builder.BuildAsync(Day, [Segment(0, 60, "Fortnox", "Kundfakturor")], Clients);

        Assert.All(luna.Questions, q => Assert.Null(q.ImagePath));
    }

    [Fact]
    public async Task Answer_outside_the_options_counts_as_internal_with_no_confidence()
    {
        var luna = new FakeLanguageModel(new Decision("Okänt Bolag AB", 0.99));
        var builder = new TimesheetBuilder(Options, null, luna);

        var entries = await builder.BuildAsync(Day, [Segment(0, 60, "Fortnox")], Clients);

        var entry = Assert.Single(entries);
        Assert.Equal(TimesheetBuilder.InternalLabel, entry.Client);
        Assert.Equal(0, entry.Confidence);
    }

    [Fact]
    public async Task Short_detour_between_work_for_the_same_client_counts_towards_that_client()
    {
        var builder = new TimesheetBuilder(Options, null, null);

        var entries = await builder.BuildAsync(
            Day,
            [
                Segment(0, 25, "Bageriet.xlsx"),
                Segment(25, 1, "Inkorg - Outlook", process: "olk"),
                Segment(26, 1, "Teams", process: "ms-teams"),
                Segment(27, 33, "Bageriet.xlsx"),
            ],
            Clients);

        var entry = Assert.Single(entries);
        Assert.Equal("Bageriet i Lund AB", entry.Client);
        Assert.Equal(60, entry.Minutes);
    }

    [Fact]
    public async Task Long_detour_is_kept_separate()
    {
        var builder = new TimesheetBuilder(Options, null, null);

        var entries = await builder.BuildAsync(
            Day,
            [
                Segment(0, 30, "Bageriet.xlsx"),
                Segment(30, 30, "Inkorg - Outlook", process: "olk"),
                Segment(60, 30, "Bageriet.xlsx"),
            ],
            Clients);

        Assert.Equal(60, entries.Single(e => e.Client == "Bageriet i Lund AB").Minutes);
        Assert.Equal(30, entries.Single(e => e.Client == TimesheetBuilder.InternalLabel).Minutes);
    }

    [Fact]
    public async Task Entries_shorter_than_half_an_increment_are_dropped()
    {
        var builder = new TimesheetBuilder(Options, null, null);

        var entries = await builder.BuildAsync(
            Day,
            [Segment(0, 60, "Bageriet.xlsx"), Segment(120, 7, "Svensson Bygg offert.docx", process: "WINWORD")],
            Clients);

        Assert.Equal("Bageriet i Lund AB", Assert.Single(entries).Client);
    }

    [Fact]
    public async Task Language_model_writes_the_comment_and_picks_the_activity_when_decision_model_is_missing()
    {
        var luna = new FakeLanguageModel(new Decision("Bokslut", 0.8)) { Comment = "Bokslut, avstämning bank" };
        var builder = new TimesheetBuilder(Options, null, luna);

        var entry = Assert.Single(await builder.BuildAsync(Day, [Segment(0, 60, "Bageriet.xlsx")], Clients));

        Assert.Equal("Bokslut", entry.Activity);
        Assert.Equal("Bokslut, avstämning bank", entry.Comment);
    }

    [Fact]
    public async Task Windows_classified_during_the_day_are_not_sent_to_a_model_again()
    {
        using var database = new TempDatabase();
        var labels = new Timekeeper.Data.SqliteTimesheetStore(database.Path);
        var jev = new CountingDecisionModel(new Decision("Svensson Bygg AB", 0.9));
        var segments = new[] { Segment(0, 60, "Fortnox") };

        await new TimesheetBuilder(Options, jev, null, labels).BuildAsync(Day, segments, Clients);
        var entries = await new TimesheetBuilder(Options, jev, null, labels).BuildAsync(Day, segments, Clients);

        Assert.Equal("Svensson Bygg AB", Assert.Single(entries).Client);
        Assert.Equal(1, jev.ClientQuestions);
    }

    [Fact]
    public void Preview_uses_what_is_already_known_and_groups_the_rest_as_unclassified()
    {
        using var database = new TempDatabase();
        var labels = new Timekeeper.Data.SqliteTimesheetStore(database.Path);
        var ledger = Segment(30, 30, "Leverantörsreskontra");
        labels.SaveLabel(new WindowLabel(WindowSignature.Of(ledger.Segment), "Svensson Bygg AB", 0.8, LabelSource.Model));
        var builder = new TimesheetBuilder(Options, new CountingDecisionModel(new Decision("Internt", 1)), null, labels);

        var preview = builder.Preview(
            [Segment(0, 30, "Bageriet.xlsx"), ledger, Segment(60, 22, "Inkorg - Outlook", process: "olk")],
            Clients);

        Assert.Equal(
            [("Bageriet i Lund AB", 30), ("Svensson Bygg AB", 30), (TimesheetBuilder.UnclassifiedLabel, 15)],
            preview.Select(p => (p.Client, p.Minutes)));
        Assert.Equal(TimeSpan.FromMinutes(22), preview[2].TimeSpent);
        Assert.Equal(0, preview[2].Confidence);
        Assert.Equal("Bageriet.xlsx", preview[0].Summary);
    }

    [Fact]
    public void Preview_never_asks_a_model()
    {
        var jev = new CountingDecisionModel(new Decision("Svensson Bygg AB", 0.9));
        var builder = new TimesheetBuilder(Options, jev, new CountingLanguageModel(new Decision("Internt", 1)));

        builder.Preview([Segment(0, 60, "Fortnox")], Clients);

        Assert.Equal(0, jev.Calls);
    }

    [Fact]
    public void Short_unclassified_detour_counts_towards_the_surrounding_client()
    {
        var builder = new TimesheetBuilder(Options, null, null);

        var preview = builder.Preview(
            [Segment(0, 25, "Bageriet.xlsx"), Segment(25, 2, "Fortnox"), Segment(27, 33, "Bageriet.xlsx")],
            Clients);

        Assert.Equal(("Bageriet i Lund AB", 60), (Assert.Single(preview).Client, preview[0].Minutes));
    }

    [Fact]
    public async Task Time_that_is_not_work_stays_out_of_the_timesheet_and_is_not_folded_into_a_client()
    {
        var luna = new FakeLanguageModel(new Decision(TimesheetBuilder.NotWorkLabel, 0.95));
        var builder = new TimesheetBuilder(Options, null, luna);

        var entries = await builder.BuildAsync(
            Day,
            [Segment(0, 25, "Bageriet.xlsx"), Segment(25, 2, "Lunchmusik", process: "Spotify"), Segment(27, 35, "Bageriet.xlsx")],
            Clients);

        var entry = Assert.Single(entries);
        Assert.Equal(("Bageriet i Lund AB", 60), (entry.Client, entry.Minutes));
    }

    [Fact]
    public async Task By_default_time_is_rounded_up_to_a_started_quarter_and_the_recorded_time_is_kept()
    {
        var builder = new TimesheetBuilder(new TimesheetOptions(), null, null);

        var entries = await builder.BuildAsync(
            Day,
            [Segment(0, 16, "Bageriet.xlsx"), Segment(60, 15, "Svensson offert 556677-8899.docx", process: "WINWORD")],
            Clients);

        Assert.Equal(
            [("Bageriet i Lund AB", 30, 16), ("Svensson Bygg AB", 15, 15)],
            entries.Select(e => (e.Client, e.Minutes, e.RecordedMinutes!.Value)));
    }

    [Fact]
    public async Task A_glance_at_a_client_is_not_rounded_up_to_a_quarter()
    {
        var builder = new TimesheetBuilder(new TimesheetOptions(), null, null);

        var entries = await builder.BuildAsync(Day, [Segment(0, 30, "Bageriet.xlsx"), Segment(40, 2, "Svensson 556677-8899.pdf")], Clients);

        Assert.Equal("Bageriet i Lund AB", Assert.Single(entries).Client);
    }

    [Fact]
    public async Task Shared_window_is_classified_per_occasion_with_the_surrounding_work_as_context()
    {
        var luna = new RecordingLanguageModel(new Decision("Bageriet i Lund AB", 0.9));
        var builder = new TimesheetBuilder(Options, null, luna);

        await builder.BuildAsync(
            Day,
            [Segment(0, 20, "Bageriet.xlsx"), Segment(20, 20, "Bokföring - Fortnox", process: "msedge"), Segment(40, 20, "Bokföring - Fortnox", process: "msedge")],
            Clients);

        Assert.Equal(2, luna.Contexts.Count);
        Assert.Contains(luna.Contexts, c => c.Contains("Arbete strax före och efter") && c.Contains("→ Bageriet i Lund AB"));
    }

    private sealed class RecordingLanguageModel(Decision answer) : ILanguageModel
    {
        public List<string> Contexts { get; } = [];

        public Task<Decision> ChooseAsync(string question, string context, IReadOnlyList<string> options, string? imagePath, CancellationToken cancellationToken)
        {
            if (question.Contains("client company"))
            {
                Contexts.Add(context);
            }

            return Task.FromResult(answer);
        }

        public Task<string> WriteCommentAsync(string context, CancellationToken cancellationToken) => Task.FromResult("Kommentar");
    }

    private sealed class FakeDecisionModel(Decision answer) : IDecisionModel
    {
        public Task<Decision> ChooseAsync(string question, string context, IReadOnlyList<string> options, CancellationToken cancellationToken) =>
            Task.FromResult(answer);
    }

    private sealed class FakeLanguageModel(Decision answer) : ILanguageModel
    {
        public List<(string Question, string? ImagePath)> Questions { get; } = [];

        public string Comment { get; init; } = "Kommentar";

        public Task<Decision> ChooseAsync(
            string question,
            string context,
            IReadOnlyList<string> options,
            string? imagePath,
            CancellationToken cancellationToken)
        {
            Questions.Add((question, imagePath));
            return Task.FromResult(answer);
        }

        public Task<string> WriteCommentAsync(string context, CancellationToken cancellationToken) => Task.FromResult(Comment);
    }
}
