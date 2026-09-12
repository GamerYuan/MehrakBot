using System.Diagnostics;
using Mehrak.MigrationService;

namespace Mehrak.MigrationService.Tests;

[TestFixture]
[NonParallelizable]
internal sealed class MigrationProcessExitTests
{
    [Test]
    public async Task MigrationFailure_ExitsWithFailureCode()
    {
        var migrationAssembly = typeof(Worker).Assembly.Location;
        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            WorkingDirectory = Path.GetDirectoryName(migrationAssembly)!,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        startInfo.ArgumentList.Add(migrationAssembly);
        startInfo.Environment["DOTNET_ENVIRONMENT"] = "Test";
        startInfo.Environment["ConnectionStrings__migrationdb"] =
            "Host=127.0.0.1;Port=1;Database=missing;Username=missing;Password=missing;Timeout=1;Command Timeout=1";
        startInfo.Environment.Remove("ConnectionStrings__mehrakdb");
        startInfo.Environment.Remove("ConnectionStrings__redis");
        startInfo.Environment.Remove("OTEL_EXPORTER_OTLP_ENDPOINT");

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Could not start the migration process.");
        var standardOutput = process.StandardOutput.ReadToEndAsync();
        var standardError = process.StandardError.ReadToEndAsync();

        if (!process.WaitForExit(TimeSpan.FromSeconds(30)))
        {
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
            Assert.Fail("Migration process did not exit after a failed connection attempt.");
        }

        var output = await Task.WhenAll(standardOutput, standardError);
        var combinedOutput = string.Join(Environment.NewLine, output);
        Assert.That(combinedOutput, Does.Contain("Database migration failed."));
        Assert.That(process.ExitCode, Is.EqualTo(Worker.FailureExitCode), combinedOutput);
    }
}
