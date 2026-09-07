using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;

namespace Mehrak.AppHost.Tests;

[TestFixture]
[Parallelizable(ParallelScope.Self)]
internal sealed class MigrationDependencyTests
{
    [Test]
    public void RuntimeServices_WaitForSuccessfulMigrationCompletion()
    {
        var builder = AppHostProgram.CreateBuilder([]);

        var migrationService = GetResource(builder, "migration-service");
        foreach (var resourceName in new[] { "application", "bot", "dashboard" })
        {
            var resource = GetResource(builder, resourceName);
            var migrationWait = resource.Annotations
                .OfType<WaitAnnotation>()
                .Single(wait => ReferenceEquals(wait.Resource, migrationService));

            Assert.Multiple(() =>
            {
                Assert.That(migrationWait.WaitType, Is.EqualTo(WaitType.WaitForCompletion));
                Assert.That(migrationWait.ExitCode, Is.EqualTo(0));
            });
        }
    }

    private static IResource GetResource(IDistributedApplicationBuilder builder, string name) =>
        builder.Resources.Single(resource => resource.Name == name);
}
