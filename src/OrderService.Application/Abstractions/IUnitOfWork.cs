namespace OrderService.Application.Abstractions;

/// <summary>
/// Orquestra a atomicidade transacional entre múltiplos repositórios: o insert do
/// <c>Order</c>/<c>OrderItem</c> e a reserva de estoque de todos os itens do pedido
/// precisam viver na mesma transação, tudo ou nada.
/// </summary>
/// <remarks>
/// <see cref="ExecuteInTransactionAsync"/> recebe um delegate com toda a orquestração
/// multi-repositório (reservar estoque item a item e depois inserir o <c>Order</c>, por
/// exemplo). A implementação abre a transação, invoca o delegate e faz commit se ele
/// terminar sem lançar; qualquer exceção lá dentro (tipo
/// <see cref="OrderService.Domain.Products.InsufficientStockException"/>) propaga e causa
/// rollback, sem deixar reserva parcial nem insert pela metade. Um simples "SaveChanges no
/// final" não bastaria aqui porque a reserva de estoque é feita chamando o repositório
/// linha a linha, fora do change tracking do EF — só um escopo transacional explícito
/// garante que tudo chega junto ao banco ou nada chega.
/// <para>
/// <see cref="SaveChangesAsync"/> fica separado pra casos que não precisam orquestrar
/// vários repositórios (o <c>ConfirmOrder</c>, que só mexe no próprio <c>Order</c>).
/// </para>
/// </remarks>
public interface IUnitOfWork
{
    Task ExecuteInTransactionAsync(Func<CancellationToken, Task> operation, CancellationToken cancellationToken = default);

    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
