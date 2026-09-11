using System.Text.Json;
using BitCode.Framework.Tools.Diagnostics.Core;
using BitCode.Framework.Tools.Diagnostics.Formatters;
using FluentAssertions;

namespace BitCode.Framework.Tools.Diagnostics.Tests;

public class DiagnosticReportTests
{
    [Fact]
    public void DiagnosticReport_ShouldCalculateMetricsCorrectly()
    {
        // Arrange
        var report = new DiagnosticReport();
        report.Add(DiagnosticItem.Success(DiagnosticCategory.Tools, "DotNet", "Ok"));
        report.Add(DiagnosticItem.Warning(DiagnosticCategory.Configuration, "RedisEnv", "Not set", remediation: "Set env"));
        report.Add(DiagnosticItem.Error(DiagnosticCategory.Connectivity, "SqlServer", "Cannot connect", remediation: "Start docker"));

        // Act & Assert
        report.TotalChecks.Should().Be(3);
        report.Passed.Should().Be(1);
        report.Warnings.Should().Be(1);
        report.Failures.Should().Be(1);
        report.HasErrors.Should().BeTrue();
        report.HasWarnings.Should().BeTrue();
        report.IsHealthy.Should().BeFalse();
    }

    [Fact]
    public void DiagnosticReport_WhenAllPassed_ShouldBeHealthy()
    {
        // Arrange
        var report = new DiagnosticReport();
        report.Add(DiagnosticItem.Success(DiagnosticCategory.Tools, "DotNet", "Ok"));
        report.Add(DiagnosticItem.Success(DiagnosticCategory.Configuration, "DockerCompose", "Found"));

        // Act & Assert
        report.TotalChecks.Should().Be(2);
        report.Passed.Should().Be(2);
        report.HasErrors.Should().BeFalse();
        report.HasWarnings.Should().BeFalse();
        report.IsHealthy.Should().BeTrue();
    }

    [Fact]
    public void JsonReportFormatter_ShouldSerializeValidJson()
    {
        // Arrange
        var report = new DiagnosticReport
        {
            ElapsedMilliseconds = 120
        };
        report.Add(DiagnosticItem.Success(DiagnosticCategory.Tools, ".NET SDK", "Installed", "10.0.100"));
        report.Add(DiagnosticItem.Warning(DiagnosticCategory.Connectivity, "SQL Server", "Unreachable", "Timeout", "Run dev-env.ps1 up"));

        // Act
        var json = JsonReportFormatter.Serialize(report);

        // Assert
        json.Should().NotBeNullOrWhiteSpace();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        root.GetProperty("totalChecks").GetInt32().Should().Be(2);
        root.GetProperty("passed").GetInt32().Should().Be(1);
        root.GetProperty("warnings").GetInt32().Should().Be(1);
        root.GetProperty("failures").GetInt32().Should().Be(0);
        root.GetProperty("items").GetArrayLength().Should().Be(2);
    }
}
