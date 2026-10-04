namespace Timekeeper.Core.Timesheets;

/// <summary>Finds clients whose name or keywords appear in a piece of text.</summary>
public sealed class ClientMatcher(IReadOnlyList<Client> clients)
{
    // Legal-form suffixes say nothing about which client it is and appear in every name.
    private static readonly string[] CompanySuffixes = [" aktiebolag", " ab", " hb", " kb", " ek. för.", " ekonomisk förening"];

    private readonly (Client Client, string[] Terms)[] _terms = clients
        .Select(client => (client, Terms(client)))
        .ToArray();

    public IReadOnlyList<Client> FindIn(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        return _terms
            .Where(entry => entry.Terms.Any(term => text.Contains(term, StringComparison.OrdinalIgnoreCase)))
            .Select(entry => entry.Client)
            .ToList();
    }

    private static string[] Terms(Client client) =>
        new[] { client.Name, StripSuffix(client.Name) }
            .Concat(client.Keywords)
            .Select(term => term.Trim())
            .Where(term => term.Length >= 3)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static string StripSuffix(string name)
    {
        foreach (var suffix in CompanySuffixes)
        {
            if (name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                return name[..^suffix.Length];
            }
        }

        return name;
    }
}
