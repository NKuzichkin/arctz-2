using ArctZ.Services.Diagnostics;

namespace ArctZ.Tests.Services.Diagnostics;

public class DiagnosticsReportTests
{
    [Fact]
    public void ToText_RendersASectionAsAHeadingFollowedByItsLines()
    {
        var report = new DiagnosticsReport(new[]
        {
            new DiagnosticsSection("Приложение", new[] { "Название: ArctZ", "Версия: 7bf6f5f" }),
        });

        Assert.Equal(
            "=== Приложение ===\nНазвание: ArctZ\nВерсия: 7bf6f5f",
            report.ToText());
    }

    [Fact]
    public void ToText_SeparatesSectionsWithABlankLine()
    {
        var report = new DiagnosticsReport(new[]
        {
            new DiagnosticsSection("Приложение", new[] { "Версия: 7bf6f5f" }),
            new DiagnosticsSection("Платформа", new[] { "ОС: Windows" }),
        });

        Assert.Equal(
            "=== Приложение ===\nВерсия: 7bf6f5f\n\n=== Платформа ===\nОС: Windows",
            report.ToText());
    }

    [Fact]
    public void ToText_MarksAnEmptySectionAsEmptyRatherThanDroppingIt()
    {
        var report = new DiagnosticsReport(new[]
        {
            new DiagnosticsSection("Последние ошибки", new string[0]),
        });

        Assert.Equal("=== Последние ошибки ===\n(пусто)", report.ToText());
    }

    [Fact]
    public void DisplayLines_SubstituteThePlaceholderForAnEmptySection()
    {
        var section = new DiagnosticsSection("Последние ошибки", new string[0]);

        Assert.Equal(new[] { "(пусто)" }, section.DisplayLines);
    }

    [Fact]
    public void DisplayLines_AreTheSectionsOwnLinesWhenItHasAny()
    {
        var section = new DiagnosticsSection("Приложение", new[] { "Версия: 7bf6f5f" });

        Assert.Equal(new[] { "Версия: 7bf6f5f" }, section.DisplayLines);
    }

    [Fact]
    public void ToText_IsEmptyForAReportWithoutSections()
    {
        var report = new DiagnosticsReport(new DiagnosticsSection[0]);

        Assert.Equal(string.Empty, report.ToText());
    }

    [Fact]
    public void ToText_UsesLineFeedsRegardlessOfThePlatformItRunsOn()
    {
        var report = new DiagnosticsReport(new[]
        {
            new DiagnosticsSection("Приложение", new[] { "Версия: 7bf6f5f" }),
        });

        Assert.DoesNotContain("\r", report.ToText());
    }
}
