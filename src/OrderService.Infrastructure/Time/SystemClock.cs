using OrderService.Application.Abstractions;

namespace OrderService.Infrastructure.Time;

public sealed class SystemClock : IClock
{
    public DateTime UtcNow => DateTime.UtcNow;
}
