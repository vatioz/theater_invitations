using Microsoft.EntityFrameworkCore;
using Npgsql;
using TheaterInvitations.Web.Services;

namespace TheaterInvitations.Domain.Tests;

public sealed class TransactionRetryTests
{
    [Fact]
    public async Task Retry_recognizes_serialization_failure_nested_in_db_update_exception()
    {
        var attempts = 0;
        var retry = new TransactionRetry();

        var result = await retry.ExecuteAsync<int>(_ =>
        {
            attempts++;
            if (attempts < 2)
            {
                var postgres = PostgresExceptionFor("40001");
                throw new DbUpdateException("wrapped", new InvalidOperationException("inner", postgres));
            }

            return Task.FromResult(42);
        });

        Assert.Equal(42, result);
        Assert.Equal(2, attempts);
    }

    private static PostgresException PostgresExceptionFor(string sqlState) => new(
        "serialization failure",
        "ERROR",
        "ERROR",
        sqlState);
}
