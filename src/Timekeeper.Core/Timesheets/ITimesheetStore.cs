namespace Timekeeper.Core.Timesheets;

public interface ITimesheetStore
{
    IReadOnlyList<Client> GetClients();

    /// <summary>Inserts the client when its id is 0, otherwise updates it. Returns the saved client.</summary>
    Client SaveClient(Client client);

    void DeleteClient(long id);

    IReadOnlyList<TimesheetEntry> GetEntries(DateOnly date);

    /// <summary>Replaces the whole timesheet for a day, e.g. with a newly generated draft.</summary>
    void ReplaceEntries(DateOnly date, IReadOnlyList<TimesheetEntry> entries);

    /// <summary>Inserts the entry when its id is 0, otherwise updates it.</summary>
    void SaveEntry(TimesheetEntry entry);

    void DeleteEntry(long id);
}
