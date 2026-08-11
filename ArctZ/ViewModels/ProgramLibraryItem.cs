using System;
using ArctZ.Services.Program;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ArctZ.ViewModels;

public partial class ProgramLibraryItem : ObservableObject
{
    public Guid Id { get; }

    public string Name { get; }

    public DateTimeOffset CreatedAt { get; }

    public DateTimeOffset ModifiedAt { get; }

    public bool ShowModifiedAt { get; }

    [ObservableProperty]
    private bool _isLoaded;

    public ProgramLibraryItem(ProgramSummary summary, bool isLoaded)
    {
        Id = summary.Id;
        Name = summary.Name;
        CreatedAt = summary.CreatedAt;
        ModifiedAt = summary.ModifiedAt;
        ShowModifiedAt = CreatedAt.ToString("dd.MM.yyyy") != ModifiedAt.ToString("dd.MM.yyyy");
        _isLoaded = isLoaded;
    }
}
