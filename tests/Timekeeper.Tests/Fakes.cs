using Microsoft.Data.Sqlite;
using Timekeeper.Core.Timesheets;

namespace Timekeeper.Tests;

/// <summary>A decision model that always gives the same answer and counts how often it is asked.</summary>
internal sealed class CountingDecisionModel(Decision answer) : IDecisionModel
{
    public int Calls { get; private set; }

    /// <summary>Calls asking which client a window is for, as opposed to which activity.</summary>
    public int ClientQuestions { get; private set; }

    public Task<Decision> ChooseAsync(string question, string context, IReadOnlyList<string> options, CancellationToken cancellationToken)
    {
        Calls++;
        ClientQuestions += question.Contains("client company") ? 1 : 0;
        return Task.FromResult(answer);
    }
}

/// <summary>A language model that always gives the same answer and counts how often it is asked.</summary>
internal sealed class CountingLanguageModel(Decision answer) : ILanguageModel
{
    public int Calls { get; private set; }

    public Task<Decision> ChooseAsync(
        string question,
        string context,
        IReadOnlyList<string> options,
        string? imagePath,
        CancellationToken cancellationToken)
    {
        Calls++;
        return Task.FromResult(answer);
    }

    public Task<string> WriteCommentAsync(string context, CancellationToken cancellationToken) => Task.FromResult("Kommentar");
}

/// <summary>A database file in the temp folder, removed when the test is done.</summary>
internal sealed class TempDatabase : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"timekeeper-{Guid.NewGuid()}.db");

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        File.Delete(Path);
    }
}
