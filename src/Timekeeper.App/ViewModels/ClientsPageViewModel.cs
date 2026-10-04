using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Timekeeper.Core.Timesheets;

namespace Timekeeper.App.ViewModels;

public sealed partial class ClientRowViewModel : ObservableObject
{
    private readonly Action<ClientRowViewModel> _save;

    [ObservableProperty]
    private string _name;

    [ObservableProperty]
    private string _keywords;

    public ClientRowViewModel(Client client, Action<ClientRowViewModel> save)
    {
        Client = client;
        _save = save;
        _name = client.Name;
        _keywords = string.Join(", ", client.Keywords);
    }

    public Client Client { get; set; }

    partial void OnNameChanged(string value) => _save(this);

    partial void OnKeywordsChanged(string value) => _save(this);

    public Client ToClient() => Client with
    {
        Name = Name.Trim(),
        Keywords = Keywords.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
    };
}

/// <summary>The clients time can be logged against, and the words that identify them.</summary>
public sealed partial class ClientsPageViewModel : PageViewModel
{
    private readonly ITimesheetStore _store;
    private readonly List<ClientRowViewModel> _all = [];

    [ObservableProperty]
    private string _filter = "";

    [ObservableProperty]
    private bool _isImportOpen;

    [ObservableProperty]
    private string _importText = "";

    public ClientsPageViewModel(ITimesheetStore store)
        : base("Kunder", "")
    {
        _store = store;
        foreach (var client in store.GetClients())
        {
            _all.Add(new ClientRowViewModel(client, Save));
        }

        ApplyFilter();
    }

    public ObservableCollection<ClientRowViewModel> Visible { get; } = [];

    public string CountText => _all.Count == 1 ? "1 kund" : $"{_all.Count} kunder";

    public bool IsEmpty => _all.Count == 0;

    [RelayCommand]
    private void AddClient()
    {
        var row = new ClientRowViewModel(_store.SaveClient(new Client(0, "Ny kund", [])), Save);
        _all.Insert(0, row);
        Filter = "";
        ApplyFilter();
    }

    [RelayCommand]
    private void DeleteClient(ClientRowViewModel row)
    {
        _store.DeleteClient(row.Client.Id);
        _all.Remove(row);
        ApplyFilter();
    }

    [RelayCommand]
    private void ToggleImport() => IsImportOpen = !IsImportOpen;

    /// <summary>Adds one client per line, skipping names that already exist.</summary>
    [RelayCommand]
    private void Import()
    {
        var existing = _all.Select(r => r.Name.Trim()).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var names = ImportText
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(line => line.Split('\t')[0].Trim())
            .Where(name => name.Length > 0 && existing.Add(name));

        foreach (var name in names)
        {
            _all.Add(new ClientRowViewModel(_store.SaveClient(new Client(0, name, [])), Save));
        }

        _all.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase));
        ImportText = "";
        IsImportOpen = false;
        ApplyFilter();
    }

    partial void OnFilterChanged(string value) => ApplyFilter();

    private void Save(ClientRowViewModel row)
    {
        var client = row.ToClient();
        if (client.Name.Length > 0)
        {
            row.Client = _store.SaveClient(client);
        }
    }

    private void ApplyFilter()
    {
        Visible.Clear();
        foreach (var row in _all.Where(r =>
                     r.Name.Contains(Filter, StringComparison.CurrentCultureIgnoreCase)
                     || r.Keywords.Contains(Filter, StringComparison.CurrentCultureIgnoreCase)))
        {
            Visible.Add(row);
        }

        OnPropertyChanged(nameof(CountText));
        OnPropertyChanged(nameof(IsEmpty));
    }
}
