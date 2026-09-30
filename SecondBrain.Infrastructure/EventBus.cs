using Microsoft.Extensions.DependencyInjection;
using SecondBrain.Core;

namespace SecondBrain.Infrastructure;

public sealed class EventBus(IServiceProvider services) : IEventBus
{
    public async Task PublishAsync<TEvent>(TEvent e, CancellationToken ct = default)
    {
        foreach (var handler in services.GetServices<IEventHandler<TEvent>>())
        {
            try
            {
                await handler.HandleAsync(e, ct);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                Console.Error.WriteLine($"Handler {handler.GetType().Name} dla {typeof(TEvent).Name} nie powiodl sie: {ex.Message}");
            }
        }
    }
}
