using OrderService.Domain.Common;

namespace OrderService.Domain.Orders;

/// <summary>
/// Aggregate root de pedido. Cuida das invariantes internas: itens não
/// vazios, quantidades positivas, currency consistente entre pedido e itens,
/// cálculo de total, transições de estado.
/// </summary>
/// <remarks>
/// O que depende de dados fora do agregado (produto existe, tem estoque)
/// fica com a Application layer, que coordena <see cref="Order"/> com
/// <see cref="Products.Stock"/> — decisions.md seções 1 e 2.
/// </remarks>
public sealed class Order
{
    private readonly List<OrderItem> _items;

    /// <summary>
    /// Construtor vazio só pro EF Core materializar via reflection (Fatia 3).
    /// EF não consegue vincular parâmetros de construtor a navigation de
    /// coleção (<c>items</c>) nem regenerar o <c>Id</c> a cada leitura — ele
    /// preenche tudo depois, inclusive via campo privado pra <see cref="Items"/>.
    /// Ninguém no domínio/aplicação chama isso diretamente; use <see cref="Place"/>.
    /// </summary>
    private Order()
    {
        _items = new List<OrderItem>();
    }

    private Order(Guid customerId, Currency currency, IReadOnlyCollection<OrderItem> items, DateTime createdAtUtc)
    {
        Id = Guid.CreateVersion7();
        CustomerId = customerId;
        Currency = currency;
        Status = OrderStatus.Placed;
        CreatedAt = createdAtUtc;
        _items = new List<OrderItem>(items);
    }

    public Guid Id { get; }

    public Guid CustomerId { get; }

    public Currency Currency { get; }

    public OrderStatus Status { get; private set; }

    public DateTime CreatedAt { get; }

    public IReadOnlyCollection<OrderItem> Items => _items.AsReadOnly();

    /// <summary>Soma de <see cref="OrderItem.LineTotal"/> de todos os itens.</summary>
    public Money Total => _items
        .Select(item => item.LineTotal)
        .Aggregate(Money.Zero(Currency), (total, lineTotal) => total.Add(lineTotal));

    /// <summary>
    /// Cria o pedido já em <see cref="OrderStatus.Placed"/> — não existe fluxo
    /// Draft -> Placed nesta v1 (decisions.md seção 13).
    /// </summary>
    /// <exception cref="EmptyOrderException"><paramref name="items"/> está vazia.</exception>
    /// <exception cref="CurrencyMismatchException">
    /// A currency de algum item diverge de <paramref name="currency"/>.
    /// </exception>
    /// <exception cref="DuplicateProductInOrderException">
    /// Dois ou mais itens de <paramref name="items"/> têm o mesmo
    /// <see cref="OrderItem.ProductId"/> (decisions.md seção 16).
    /// </exception>
    /// <param name="createdAtUtc">
    /// Instante UTC pra gravar em <see cref="CreatedAt"/>, já resolvido pela
    /// Application layer via <c>IClock</c> — o Domain não fala com relógio.
    /// </param>
    public static Order Place(Guid customerId, Currency currency, IReadOnlyCollection<OrderItem> items, DateTime createdAtUtc)
    {
        ArgumentNullException.ThrowIfNull(items);

        if (items.Count == 0)
        {
            throw new EmptyOrderException();
        }

        var seenProductIds = new HashSet<Guid>();

        foreach (var item in items)
        {
            if (item.UnitPrice.Currency != currency)
            {
                throw new CurrencyMismatchException(currency, item.UnitPrice.Currency);
            }

            if (!seenProductIds.Add(item.ProductId))
            {
                throw new DuplicateProductInOrderException(item.ProductId);
            }
        }

        return new Order(customerId, currency, items, createdAtUtc);
    }

    /// <summary>
    /// Transiciona o pedido de <see cref="OrderStatus.Placed"/> para
    /// <see cref="OrderStatus.Confirmed"/>. Idempotente: se já está
    /// <see cref="OrderStatus.Confirmed"/>, não faz nada.
    /// </summary>
    /// <exception cref="InvalidOrderStateTransitionException">
    /// O pedido está em <see cref="OrderStatus.Draft"/> ou
    /// <see cref="OrderStatus.Canceled"/>.
    /// </exception>
    public void Confirm()
    {
        if (Status == OrderStatus.Confirmed)
        {
            return;
        }

        if (Status != OrderStatus.Placed)
        {
            throw new InvalidOrderStateTransitionException(Status, OrderStatus.Confirmed);
        }

        Status = OrderStatus.Confirmed;
    }

    /// <summary>
    /// Transiciona de <see cref="OrderStatus.Placed"/> ou
    /// <see cref="OrderStatus.Confirmed"/> para <see cref="OrderStatus.Canceled"/>.
    /// Idempotente. Depois do cancelamento, cabe à Application layer iterar
    /// <see cref="Items"/> e liberar a quantidade reservada em
    /// <see cref="Products.Stock"/> de cada produto.
    /// </summary>
    /// <returns>
    /// <c>true</c> se o estado realmente mudou; <c>false</c> se o pedido já
    /// estava cancelado (chamada idempotente). A Fatia 2 usa esse retorno pra
    /// só liberar estoque quando o cancelamento é "de verdade" — evita
    /// liberar a mesma reserva duas vezes numa chamada duplicada. O retorno
    /// passou de <c>void</c> pra <c>bool</c> nesta fatia, decisão do plano da Fatia 2.
    /// </returns>
    /// <exception cref="InvalidOrderStateTransitionException">
    /// O pedido está em <see cref="OrderStatus.Draft"/>. <c>Draft</c> não é
    /// alcançável na v1, mas cancelar algo que nunca foi de fato "colocado"
    /// (e nunca reservou estoque) não faz sentido de negócio — rejeitar
    /// mantém a máquina de estados coerente caso <c>Draft</c> seja exposto no futuro.
    /// </exception>
    public bool Cancel()
    {
        if (Status == OrderStatus.Canceled)
        {
            return false;
        }

        if (Status != OrderStatus.Placed && Status != OrderStatus.Confirmed)
        {
            throw new InvalidOrderStateTransitionException(Status, OrderStatus.Canceled);
        }

        Status = OrderStatus.Canceled;
        return true;
    }
}
