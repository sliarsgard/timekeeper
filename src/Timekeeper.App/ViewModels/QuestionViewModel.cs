using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Timekeeper.Core.Timesheets;

namespace Timekeeper.App.ViewModels;

/// <summary>Asks which client the work in a window is for.</summary>
/// <param name="answer">Called with the chosen client, or null when the question was put off.</param>
public sealed partial class QuestionViewModel(WindowQuestion question, Action<string?> answer, Action stopAsking) : ObservableObject
{
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ChooseOtherCommand))]
    private string? _otherClient;

    public string Program => ProgramNames.Friendly(question.ProcessName);

    public string Title => string.IsNullOrWhiteSpace(question.Title) ? "(fönster utan titel)" : question.Title;

    public string? Suggestion => question.Suggestion;

    public bool HasSuggestion => question.Suggestion is not null;

    public IReadOnlyList<string> Clients => question.Clients;

    [RelayCommand]
    private void ChooseSuggestion() => answer(question.Suggestion);

    [RelayCommand]
    private void ChooseInternal() => answer(WindowClassifier.InternalLabel);

    /// <summary>Picks a client from the register, or a new one by typing its name.</summary>
    [RelayCommand(CanExecute = nameof(CanChooseOther))]
    private void ChooseOther()
    {
        var typed = OtherClient!.Trim();
        answer(Clients.FirstOrDefault(c => string.Equals(c, typed, StringComparison.CurrentCultureIgnoreCase)) ?? typed);
    }

    [RelayCommand]
    private void Later() => answer(null);

    [RelayCommand]
    private void StopAsking()
    {
        stopAsking();
        answer(null);
    }

    private bool CanChooseOther() => !string.IsNullOrWhiteSpace(OtherClient);
}
