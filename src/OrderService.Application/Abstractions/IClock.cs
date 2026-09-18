namespace OrderService.Application.Abstractions;

/// <summary>Abstração do relógio do sistema, para dar de mockar "agora" nos testes.</summary>
/// <remarks>
/// Quem usa é o <c>CreateOrderHandler</c>, que resolve <see cref="UtcNow"/> e passa
/// já pronto para <c>Order.Place</c> — o Domain não depende diretamente dessa interface.
/// </remarks>
public interface IClock
{
    DateTime UtcNow { get; }
}
