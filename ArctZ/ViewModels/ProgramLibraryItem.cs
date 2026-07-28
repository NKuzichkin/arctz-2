using System;
using ArctZ.Services.Program;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ArctZ.ViewModels;

public partial class ProgramLibraryItem : ObservableObject
{
    public Guid Id { get; }

    public string Name { get; }

    public DateTimeOffset ModifiedAt { get; }

    [ObservableProperty]
    private bool _isLoaded;

    public ProgramLibraryItem(ProgramSummary summary, bool isLoaded)
    {
        Id = summary.Id;
        Name = summary.Name;
        ModifiedAt = summary.ModifiedAt;
        _isLoaded = isLoaded;
    }
}
