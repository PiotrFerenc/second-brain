using Microsoft.Extensions.DependencyInjection;
using SecondBrain.Core;
using SecondBrain.Infrastructure;

namespace SecondBrain.Tests.Infrastructure;

public class EventBusTests
{
    private record Ping(int N);
    private record Pong;

    private sealed class Recorder(List<string> log, string name, Exception? throws = null) : IEventHandler<Ping>
    {
        public Task HandleAsync(Ping e, CancellationToken ct = default)
        {
            log.Add($"{name}:{e.N}");
            return throws is null ? Task.CompletedTask : Task.FromException(throws);
        }
    }

    private static EventBus Bus(Action<IServiceCollection> configure)
    {
        var services = new ServiceCollection();
        configure(services);
        return new EventBus(services.BuildServiceProvider());
    }

    [Fact]
    public async Task NoHandlers_IsNoOp() =>
        await Bus(_ => { }).PublishAsync(new Ping(1));

    [Fact]
    public async Task Handlers_RunInRegistrationOrder()
    {
        var log = new List<string>();
        var bus = Bus(s =>
        {
            s.AddSingleton<IEventHandler<Ping>>(new Recorder(log, "a"));
            s.AddSingleton<IEventHandler<Ping>>(new Recorder(log, "b"));
            s.AddSingleton<IEventHandler<Ping>>(new Recorder(log, "c"));
        });

        await bus.PublishAsync(new Ping(5));

        Assert.Equal(["a:5", "b:5", "c:5"], log);
    }

    [Fact]
    public async Task ThrowingHandler_DoesNotStopOthers()
    {
        var log = new List<string>();
        var bus = Bus(s =>
        {
            s.AddSingleton<IEventHandler<Ping>>(new Recorder(log, "a", new InvalidOperationException("boom")));
            s.AddSingleton<IEventHandler<Ping>>(new Recorder(log, "b"));
        });

        await bus.PublishAsync(new Ping(1));

        Assert.Equal(["a:1", "b:1"], log);
    }

    [Fact]
    public async Task OnlyHandlersOfThatEventType_Run()
    {
        var log = new List<string>();
        var bus = Bus(s => s.AddSingleton<IEventHandler<Ping>>(new Recorder(log, "a")));

        await bus.PublishAsync(new Pong());

        Assert.Empty(log);
    }

    [Fact]
    public async Task CancelledToken_ExceptionPropagates()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var bus = Bus(s => s.AddSingleton<IEventHandler<Ping>>(new Recorder([], "a", new OperationCanceledException())));

        await Assert.ThrowsAsync<OperationCanceledException>(() => bus.PublishAsync(new Ping(1), cts.Token));
    }
}
