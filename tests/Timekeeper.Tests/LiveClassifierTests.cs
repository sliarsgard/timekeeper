using Timekeeper.Core;
using Timekeeper.Core.Timesheets;
using Timekeeper.Data;

namespace Timekeeper.Tests;

public sealed class LiveClassifierTests : IDisposable
{
    private static readonly DateTime DayStart = new(2026, 10, 5, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime DayEnd = DayStart.AddDays(1);
    private static readonly DateTime T0 = DayStart.AddHours(7);
    private static readonly TimesheetOptions Options = new() { ConfidenceThreshold = 0.75 };

    private readonly TempDatabase _database = new();
    private readonly SqliteActivityStore _activity;
    private readonly SqliteTimesheetStore _timesheets;
    private readonly LiveClassifier _live;

    public LiveClassifierTests()
    {
        _activity = new SqliteActivityStore(_database.Path);
        _timesheets = new SqliteTimesheetStore(_database.Path);
        _timesheets.SaveClient(new Client(0, "Bageriet i Lund AB", ["Bageriet"]));
        _timesheets.SaveClient(new Client(0, "Svensson Bygg AB", []));
        _live = new LiveClassifier(_activity, _timesheets, _timesheets);
    }

    public void Dispose() => _database.Dispose();

    private ActivitySegment Record(double startMinute, double minutes, string title, string process = "WINWORD")
    {
        var segment = new ActivitySegment
        {
            StartUtc = T0.AddMinutes(startMinute),
            EndUtc = T0.AddMinutes(startMinute + minutes),
            State = ActivityState.Active,
            ProcessName = process,
            WindowTitle = title,
        };
        _activity.InsertSegment(segment);
        return segment;
    }

    private ActivitySegment RecordFortnox(double startMinute, double minutes) =>
        Record(startMinute, minutes, "Bokföring - Fortnox", process: "msedge");

    private static LiveClassifierSetup Setup(IDecisionModel? jev = null, bool ask = true) => new(Options, jev, null, ask);

    private Task<WindowQuestion?> Run(LiveClassifierSetup setup, int minutesAfterStart = 120) =>
        _live.RunOnceAsync(setup, DayStart, DayEnd, T0.AddMinutes(minutesAfterStart));

    [Fact]
    public async Task Windows_used_long_enough_are_classified_once_and_remembered()
    {
        var segment = Record(0, 5, "Avtal.docx - Word");
        var jev = new CountingDecisionModel(new Decision("Svensson Bygg AB", 0.9));

        await Run(Setup(jev));
        await Run(Setup(jev));

        var label = _timesheets.GetLabels()[WindowSignature.Of(segment)];
        Assert.Equal(("Svensson Bygg AB", LabelSource.Model), (label.Client, label.Source));
        Assert.Equal(1, jev.ClientQuestions);
    }

    [Fact]
    public async Task Windows_that_only_flash_by_are_not_sent_to_a_model()
    {
        Record(0, 0.5, "Avtal.docx - Word");
        var jev = new CountingDecisionModel(new Decision("Svensson Bygg AB", 0.9));

        await Run(Setup(jev));

        Assert.Equal(0, jev.Calls);
        Assert.Empty(_timesheets.GetLabels());
    }

    [Fact]
    public async Task Nothing_is_sent_or_stored_without_a_model()
    {
        Record(0, 5, "Avtal.docx - Word");

        await Run(Setup());

        Assert.Empty(_timesheets.GetLabels());
    }

    [Fact]
    public async Task Each_occasion_of_a_shared_window_is_classified_on_its_own()
    {
        var first = RecordFortnox(0, 5);
        Record(5, 10, "Bageriet bokslut.xlsx - Excel", process: "EXCEL");
        var second = RecordFortnox(15, 5);
        Record(20, 1, "Avtal.docx - Word");
        var jev = new CountingDecisionModel(new Decision("Svensson Bygg AB", 0.9));

        await Run(Setup(jev));

        var occasions = _timesheets.GetOccasionLabels(first.Id, second.Id);
        Assert.Equal([first.Id, second.Id], occasions.Keys.Order());
        Assert.DoesNotContain(WindowSignature.Of(first), _timesheets.GetLabels().Keys);
    }

    [Fact]
    public async Task The_occasion_still_in_front_is_left_until_it_is_over()
    {
        var current = RecordFortnox(0, 5);
        var jev = new CountingDecisionModel(new Decision("Svensson Bygg AB", 0.9));

        await Run(Setup(jev));

        Assert.Empty(_timesheets.GetOccasionLabels(current.Id, current.Id));
    }

    [Fact]
    public async Task Unsure_window_is_asked_about_with_the_best_guess()
    {
        var segment = Record(0, 5, "Avtal.docx - Word");

        var question = await Run(Setup(new CountingDecisionModel(new Decision("Svensson Bygg AB", 0.4))));

        Assert.NotNull(question);
        Assert.Equal(WindowSignature.Of(segment), question.Key);
        Assert.Null(question.SegmentId);
        Assert.Equal("Svensson Bygg AB", question.Suggestion);
        Assert.Equal(["Bageriet i Lund AB", "Svensson Bygg AB"], question.Clients);
    }

    [Fact]
    public async Task Answer_about_a_shared_window_only_applies_to_that_occasion()
    {
        var first = RecordFortnox(0, 5);
        var second = RecordFortnox(10, 5);
        Record(15, 1, "Avtal.docx - Word");

        var question = await Run(Setup(new CountingDecisionModel(new Decision("Svensson Bygg AB", 0.4))));
        _live.Answer(question!, "Bageriet i Lund AB");

        Assert.Equal(second.Id, question.SegmentId);
        var occasions = _timesheets.GetOccasionLabels(first.Id, second.Id);
        Assert.Equal(LabelSource.User, occasions[second.Id].Source);
        Assert.Equal(LabelSource.Model, occasions[first.Id].Source);
    }

    [Fact]
    public async Task Nothing_is_asked_when_questions_are_turned_off()
    {
        Record(0, 5, "Avtal.docx - Word");

        var question = await Run(Setup(new CountingDecisionModel(new Decision("Svensson Bygg AB", 0.4)), ask: false));

        Assert.Null(question);
    }

    [Fact]
    public async Task Confident_and_short_windows_are_not_asked_about()
    {
        Record(0, 5, "Säker.docx - Word");
        Record(10, 1.5, "Kort.docx - Word");
        var jev = new CountingDecisionModel(new Decision("Svensson Bygg AB", 0.9));

        Assert.Null(await Run(Setup(jev)));
    }

    [Fact]
    public async Task A_window_is_asked_about_once_and_questions_are_spaced_out()
    {
        Record(0, 5, "Första.docx - Word");
        Record(10, 5, "Andra.docx - Word");
        var setup = Setup(new CountingDecisionModel(new Decision("Svensson Bygg AB", 0.4)));

        var first = await Run(setup, minutesAfterStart: 60);
        _live.MarkAsked(first!.Key, T0.AddMinutes(60));
        var tooSoon = await Run(setup, minutesAfterStart: 62);
        var later = await Run(setup, minutesAfterStart: 66);

        Assert.Null(tooSoon);
        Assert.NotNull(later);
        Assert.NotEqual(first.Key, later.Key);
    }

    [Fact]
    public async Task Answer_is_remembered_and_not_asked_about_again()
    {
        var segment = Record(0, 5, "Avtal.docx - Word");
        _live.Answer(WindowSignature.Of(segment), "Bageriet i Lund AB");

        var question = await Run(Setup(new CountingDecisionModel(new Decision("Svensson Bygg AB", 0.4))));

        Assert.Null(question);
        Assert.Equal(LabelSource.User, _timesheets.GetLabels()[WindowSignature.Of(segment)].Source);
    }
}
