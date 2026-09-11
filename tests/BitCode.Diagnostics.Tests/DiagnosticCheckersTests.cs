using BitCode.Framework.Tools.Diagnostics.Checkers;
using BitCode.Framework.Tools.Diagnostics.Core;
using FluentAssertions;

namespace BitCode.Framework.Tools.Diagnostics.Tests;

public class DiagnosticCheckersTests
{
    [Fact]
    public async Task ConfigurationChecker_WhenManifestsExist_ShouldReportSuccess()
    {
        // Arrange
        var tempDir = Path.Combine(Path.GetTempPath(), "bitcode_diag_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        Directory.CreateDirectory(Path.Combine(tempDir, "scripts"));

        try
        {
            File.WriteAllText(Path.Combine(tempDir, "docker-compose.yml"), "version: '3.8'");
            File.WriteAllText(Path.Combine(tempDir, "scripts", "dev-env.ps1"), "# powershell");

            var checker = new ConfigurationChecker(tempDir);

            // Act
            var results = await checker.RunChecksAsync();

            // Assert
            results.Should().NotBeEmpty();
            var composeItem = results.FirstOrDefault(r => r.Name == "docker-compose.yml");
            composeItem.Should().NotBeNull();
            composeItem!.Severity.Should().Be(DiagnosticSeverity.Success);

            var scriptItem = results.FirstOrDefault(r => r.Name == "scripts/dev-env.ps1");
            scriptItem.Should().NotBeNull();
            scriptItem!.Severity.Should().Be(DiagnosticSeverity.Success);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
    }

    [Fact]
    public async Task ConfigurationChecker_WhenRequiredManifestMissing_ShouldReportError()
    {
        // Arrange
        var tempDir = Path.Combine(Path.GetTempPath(), "bitcode_diag_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var checker = new ConfigurationChecker(tempDir);

            // Act
            var results = await checker.RunChecksAsync();

            // Assert
            var composeItem = results.FirstOrDefault(r => r.Name == "docker-compose.yml");
            composeItem.Should().NotBeNull();
            composeItem!.Severity.Should().Be(DiagnosticSeverity.Error);
            composeItem.Remediation.Should().NotBeNullOrWhiteSpace();
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
    }

    [Fact]
    public async Task ConnectivityChecker_WhenServiceUnreachable_ShouldReportWarningWithRemediation()
    {
        // Arrange - usar puerto local cerrado e inalcanzable con timeout muy corto (100ms)
        var checker = new ConnectivityChecker(TimeSpan.FromMilliseconds(100));

        // Act
        var result = await checker.CheckTcpServiceAsync(
            "Fake Service",
            "127.0.0.1",
            59999,
            "Servicio ficticio de prueba",
            "Inicie el servicio de prueba.",
            CancellationToken.None);

        // Assert
        result.Severity.Should().Be(DiagnosticSeverity.Warning);
        result.Remediation.Should().Be("Inicie el servicio de prueba.");
        result.Details.Should().Contain("59999");
    }

    [Fact]
    public async Task ToolsChecker_ShouldExecuteWithoutExceptions()
    {
        // Arrange
        var checker = new ToolsChecker();

        // Act
        var results = await checker.RunChecksAsync();

        // Assert
        results.Should().NotBeEmpty();
        results.Should().Contain(i => i.Name == ".NET SDK");
        results.Should().Contain(i => i.Name == "Node.js");
        results.Should().Contain(i => i.Name == "Git");
        results.Should().Contain(i => i.Name == "Docker CLI");
    }
}
