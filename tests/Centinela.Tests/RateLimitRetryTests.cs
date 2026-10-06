using Centinela.Infrastructure.Foundry;

namespace Centinela.Tests;

public class RateLimitRetryTests
{
    private sealed class Throttled(TimeSpan? after = null) : Exception("429")
    {
        public TimeSpan After { get; } = after ?? TimeSpan.Zero;
    }

    private static TimeSpan? IsThrottle(Exception e) => e is Throttled t ? t.After : null;

    [Fact]
    public async Task Retries_a_rate_limit_and_returns_the_eventual_result()
    {
        var calls = 0;
        var waits = new List<TimeSpan>();

        var result = await RateLimitRetry.ExecuteAsync(
            () => ++calls < 3 ? throw new Throttled() : Task.FromResult("ok"),
            IsThrottle, (d, _) => { waits.Add(d); return Task.CompletedTask; }, default);

        Assert.Equal("ok", result);
        Assert.Equal(3, calls);
        Assert.Equal(2, waits.Count);
    }

    [Fact]
    public async Task Does_not_retry_errors_that_are_not_rate_limits()
    {
        var calls = 0;

        await Assert.ThrowsAsync<InvalidOperationException>(() => RateLimitRetry.ExecuteAsync<string>(
            () => { calls++; throw new InvalidOperationException("otro error"); },
            IsThrottle, (_, _) => Task.CompletedTask, default));

        Assert.Equal(1, calls); // un 400 o un 404 no mejoran esperando
    }

    [Fact]
    public async Task Gives_up_after_the_maximum_attempts_and_surfaces_the_error()
    {
        var calls = 0;

        await Assert.ThrowsAsync<Throttled>(() => RateLimitRetry.ExecuteAsync<string>(
            () => { calls++; throw new Throttled(); },
            IsThrottle, (_, _) => Task.CompletedTask, default, maxAttempts: 4));

        Assert.Equal(4, calls);
    }

    [Fact]
    public void Honors_the_server_suggested_wait_and_otherwise_backs_off_exponentially()
    {
        Assert.Equal(TimeSpan.FromSeconds(11), RateLimitRetry.Backoff(1, TimeSpan.FromSeconds(10)));
        Assert.Equal(TimeSpan.FromSeconds(2), RateLimitRetry.Backoff(1, TimeSpan.Zero));
        Assert.Equal(TimeSpan.FromSeconds(8), RateLimitRetry.Backoff(3, TimeSpan.Zero));
        Assert.Equal(TimeSpan.FromSeconds(60), RateLimitRetry.Backoff(20, TimeSpan.Zero)); // con tope
    }

    [Fact]
    public async Task Cancellation_is_not_swallowed_as_a_retryable_error()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAsync<OperationCanceledException>(() => RateLimitRetry.ExecuteAsync<string>(
            () => throw new OperationCanceledException(),
            IsThrottle, (_, _) => Task.CompletedTask, cts.Token));
    }
}
