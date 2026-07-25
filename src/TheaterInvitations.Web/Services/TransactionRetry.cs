using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace TheaterInvitations.Web.Services;

public interface ITransactionRetry
{
    Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken = default);
}

public sealed class TransactionRetry : ITransactionRetry
{
    private const int MaxAttempts = 3;

    public async Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken = default)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await operation(cancellationToken);
            }
            catch (Exception exception) when (attempt < MaxAttempts && IsTransientConflict(exception))
            {
                await Task.Delay(TimeSpan.FromMilliseconds(20 * attempt + Random.Shared.Next(10, 40)), cancellationToken);
            }
            catch (Exception exception) when (IsTransientConflict(exception))
            {
                throw new TransactionConflictException("The data changed while this action was being saved. Please review the latest values and try again.", exception);
            }
        }
    }

    private static bool IsTransientConflict(Exception exception)
    {
        var postgresException = exception as PostgresException ?? exception.InnerException as PostgresException;
        return exception is DbUpdateConcurrencyException || postgresException?.SqlState is PostgresErrorCodes.SerializationFailure or PostgresErrorCodes.DeadlockDetected;
    }
}

public sealed class TransactionConflictException(string message, Exception innerException) : InvalidOperationException(message, innerException);
