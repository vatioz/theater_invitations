using Microsoft.EntityFrameworkCore;
using TheaterInvitations.Web.Services;

namespace TheaterInvitations.Domain.Tests;

public sealed class TransactionRetryTests
{
    [Fact]
    public async Task Retries_transient_concurrency_failures()
    {
        var attempts = 0;

        var result = await new TransactionRetry().ExecuteAsync<int>(_ =>
        {
            attempts++;
            if (attempts < 3) throw new DbUpdateConcurrencyException();
            return Task.FromResult(42);
        });

        Assert.Equal(42, result);
        Assert.Equal(3, attempts);
    }

    [Fact]
    public async Task Stops_after_three_transient_failures()
    {
        var attempts = 0;

        var exception = await Assert.ThrowsAsync<TransactionConflictException>(() => new TransactionRetry().ExecuteAsync<int>(_ =>
        {
            attempts++;
            throw new DbUpdateConcurrencyException();
        }));

        Assert.Equal(3, attempts);
        Assert.IsType<DbUpdateConcurrencyException>(exception.InnerException);
    }
}
