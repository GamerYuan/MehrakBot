using Microsoft.EntityFrameworkCore;

using System.Data;
using System.Data.Common;

namespace Mehrak.Infrastructure.Character.Services;

internal static class CharacterDbLock
{
    public static Task AcquireAsync(DbContext context, string resource, CancellationToken cancellationToken = default)
    {
        if (!string.Equals(context.Database.ProviderName, "Npgsql.EntityFrameworkCore.PostgreSQL", StringComparison.Ordinal))
            return Task.CompletedTask;

        return context.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(hashtextextended({resource}, 0))", cancellationToken);
    }

    public static async Task<IAsyncDisposable> AcquireSessionAsync(
        DbContext context, string resource, CancellationToken cancellationToken = default)
    {
        if (!string.Equals(context.Database.ProviderName, "Npgsql.EntityFrameworkCore.PostgreSQL", StringComparison.Ordinal))
            return NoopSessionLock.Instance;

        var connection = context.Database.GetDbConnection();
        var openedByLock = connection.State != ConnectionState.Open;
        if (openedByLock)
            await connection.OpenAsync(cancellationToken);

        await using var command = CreateCommand(connection,
            "SELECT pg_advisory_lock(hashtextextended(@resource, 0))", resource);
        await command.ExecuteNonQueryAsync(cancellationToken);
        return new SessionLock(connection, resource, openedByLock);
    }

    private static DbCommand CreateCommand(DbConnection connection, string commandText, string resource)
    {
        var command = connection.CreateCommand();
        command.CommandText = commandText;
        var parameter = command.CreateParameter();
        parameter.ParameterName = "resource";
        parameter.Value = resource;
        command.Parameters.Add(parameter);
        return command;
    }

    private sealed class SessionLock(DbConnection connection, string resource, bool closeConnection) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            try
            {
                if (connection.State == ConnectionState.Open)
                {
                    await using var command = CreateCommand(connection,
                        "SELECT pg_advisory_unlock(hashtextextended(@resource, 0))", resource);
                    await command.ExecuteNonQueryAsync();
                }
            }
            finally
            {
                if (closeConnection)
                    await connection.CloseAsync();
            }
        }
    }

    private sealed class NoopSessionLock : IAsyncDisposable
    {
        public static readonly NoopSessionLock Instance = new();

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
