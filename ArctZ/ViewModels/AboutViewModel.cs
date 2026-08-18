using System.Collections.Generic;
using ArctZ.Services.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ArctZ.ViewModels;

/// <summary>
/// The "О программе" dialog. Holds a finished report rather than live view models:
/// it is rebuilt on every open, so what the user reads and what they copy are the
/// same snapshot, taken at the same instant.
/// </summary>
public partial class AboutViewModel : ViewModelBase
{
    private readonly DiagnosticsReport _report;

    public AboutViewModel(DiagnosticsReport report)
    {
        _report = report;
        ReportText = report.ToText();
    }

    public string AppName => BuildInfo.AppName;

    public IReadOnlyList<DiagnosticsSection> Sections => _report.Sections;

    /// <summary>The whole report as plain text — exactly what the copy button puts on the clipboard.</summary>
    public string ReportText { get; }

    /// <summary>Set once the report has been copied. Not reset on a timer: the dialog is
    /// rebuilt on every open, so the confirmation naturally lasts exactly as long as this view.</summary>
    [ObservableProperty]
    private bool _isCopied;

    public void MarkCopied() => IsCopied = true;
}
