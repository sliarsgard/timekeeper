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
        _timesheets.SaveClient(new Client(0, "Bageriet i Lund AB", []));
        _timesheets.SaveClient(new Client(0, "Svensson Bygg AB", []));
        _live = new LiveClassifier(_activity, _timesheets, _timesheets);
    }

    public void Dispose() => _database.Dispose();

    private ActivitySegment Record(int startMinute, double minutes, string title)
    {
        var segment = new ActivitySegment
        {
            StartUtc = T0.AddMinutes(startMinute),
            EndUtc = T0.AddMinutes(startMinute + minutes),
            State = ActivityState.Active,
            ProcessName = "msedge",
            WindowTitle = title,
        };
        _activity.InsertSegment(segment);
        return segment;
    }

    private static LiveClassifierSetup Setup(IDecisionModel? jev = null, bool ask = true) => new(Options, jev, null, ask);

    private Task<WindowQuestion?> Run(LiveClassifierSetup setup, int minutesAfterStart = 60) =>
        _live.RunOnceAsync(setup, DayStart, DayEnd, T0.AddMinutes(minutesAfterStart));

    [Fact]
    public async Task Windows_used_long_enough_are_classified_once_and_remembered()
    {
        var segment = Record(0, 5, "Leverantörsfakturor - Fortnox");
        var jev = new CountingDecisionModel(new Decision("Svensson Bygg AB", 0.9));

        await Run(Setup(jev));
        await Run(Setup(jev));

        var label = _timesheets.GetLabels()[WindowSignature.Of(segment)];
        Assert.Equal("Svensson Bygg AB", label.Client);
        Assert.Equal(LabelSource.Model, label.Source);
        Assert.Equal(1, jev.Calls);
    }

    [Fact]
    public async Task Windows_that_only_flash_by_are_not_sent_to_a_model()
    {
        Record(0, 0.5, "Fortnox");
        var jev = new CountingDecisionModel(new Decision("Svensson Bygg AB", 0.9));

        await Run(Setup(jev));

        Assert.Equal(0, jev.Calls);
        Assert.Empty(_timesheets.GetLabels());
    }

    [Fact]
    public async Task Unsure_window_is_asked_about_with_the_best_guess()
    {
        var segment = Record(0, 5, "Fortnox");

        var question = await Run(Setup(new CountingDecisionModel(new Decision("Svensson Bygg AB", 0.4))));

        Assert.NotNull(question);
        Assert.Equal(WindowSignature.Of(segment), question.Signature);
        Assert.Equal("Svensson Bygg AB", question.Suggestion);
        Assert.Equal(["Bageriet i Lund AB", "Svensson Bygg AB"], question.Clients);
    }

    [Fact]
    public async Task Nothing_is_asked_when_questions_are_turned_off()
    {
        Record(0, 5, "Fortnox");

        var question = await Run(Setup(new CountingDecisionModel(new Decision("Svensson Bygg AB", 0.4)), ask: false));

        Assert.Null(question);
    }

    [Fact]
    public async Task Confident_and_short_windows_are_not_asked_about()
    {
        Record(0, 5, "Säker - Fortnox");
        Record(10, 1.5, "Kort - Fortnox");
        var jev = new CountingDecisionModel(new Decision("Svensson Bygg AB", 0.9));

        Assert.Null(await Run(Setup(jev)));
    }

    [Fact]
    public async Task A_window_is_asked_about_once_and_questions_are_spaced_out()
    {
        Record(0, 5, "Första - Fortnox");
        Record(10, 5, "Andra - Fortnox");
        var setup = Setup(new CountingDecisionModel(new Decision("Svensson Bygg AB", 0.4)));

        var first = await Run(setup, minutesAfterStart: 60);
        _live.MarkAsked(first!.Signature, T0.AddMinutes(60));
        var tooSoon = await Run(setup, minutesAfterStart: 62);
        var later = await Run(setup, minutesAfterStart: 66);

        Assert.Null(tooSoon);
        Assert.NotNull(later);
        Assert.NotEqual(first.Signature, later.Signature);
    }

    [Fact]
    public async Task Answer_is_remembered_and_not_asked_about_again()
    {
        var segment = Record(0, 5, "Fortnox");
        _live.Answer(WindowSignature.Of(segment), "Bageriet i Lund AB");

        var question = await Run(Setup(new CountingDecisionModel(new Decision("Svensson Bygg AB", 0.4))));

        Assert.Null(question);
        Assert.Equal(LabelSource.User, _timesheets.GetLabels()[WindowSignature.Of(segment)].Source);
    }

    [Fact]
    public async Task Guesses_made_without_models_are_revisited_once_a_model_is_set_up()
    {
        var segment = Record(0, 5, "Fortnox");
        await Run(Setup());
        Assert.Equal(LabelSource.Guess, _timesheets.GetLabels()[WindowSignature.Of(segment)].Source);

        await Run(Setup(new CountingDecisionModel(new Decision("Bageriet i Lund AB", 0.9))));

        var label = _timesheets.GetLabels()[WindowSignature.Of(segment)];
        Assert.Equal(("Bageriet i Lund AB", LabelSource.Model), (label.Client, label.Source));
    }
}
