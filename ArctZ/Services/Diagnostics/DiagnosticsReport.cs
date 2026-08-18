using System.Collections.Generic;
using System.Text;

namespace ArctZ.Services.Diagnostics;

/// <summary>One titled block of the "О программе" dialog: a heading and its already-formatted lines.</summary>
public sealed record DiagnosticsSection(string Title, IReadOnlyList<string> Lines)
{
    /// <summary>What the dialog renders — an empty section still shows a placeholder, so a reader
    /// can tell "nothing was recorded" from "this part of the report is missing".</summary>
    public IReadOnlyList<string> DisplayLines =>
        Lines.Count == 0 ? new[] { DiagnosticsReport.EmptySectionPlaceholder } : Lines;
}

/// <summary>
/// The whole diagnostic snapshot, in the single shape used both for on-screen display
/// and for the plain text put on the clipboard, so that what a user sends us is exactly
/// what they saw.
/// </summary>
public sealed class DiagnosticsReport
{
    /// <summary>Stands in for a section that has nothing to show, so its absence is not mistaken for a missing feature.</summary>
    public const string EmptySectionPlaceholder = "(пусто)";

    // Deliberately not LineBreak: the same report must read identically whether it
    // was copied from the desktop app, an Android phone or the browser head.
    private const string LineBreak = "\n";

    public DiagnosticsReport(IReadOnlyList<DiagnosticsSection> sections)
    {
        Sections = sections;
    }

    public IReadOnlyList<DiagnosticsSection> Sections { get; }

    public string ToText()
    {
        var builder = new StringBuilder();

        foreach (var section in Sections)
        {
            if (builder.Length > 0)
            {
                builder.Append(LineBreak);
            }

            builder.Append("=== ").Append(section.Title).Append(" ===").Append(LineBreak);

            if (section.Lines.Count == 0)
            {
                builder.Append(EmptySectionPlaceholder).Append(LineBreak);
                continue;
            }

            foreach (var line in section.Lines)
            {
                builder.Append(line).Append(LineBreak);
            }
        }

        return builder.ToString().TrimEnd('\r', '\n');
    }
}
