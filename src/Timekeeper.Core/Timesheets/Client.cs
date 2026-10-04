namespace Timekeeper.Core.Timesheets;

/// <summary>A client company that time is logged against.</summary>
/// <param name="Keywords">Extra words that identify the client, e.g. a short name, org number or SharePoint folder.</param>
public sealed record Client(long Id, string Name, IReadOnlyList<string> Keywords);
