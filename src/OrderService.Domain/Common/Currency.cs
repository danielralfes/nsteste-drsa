namespace OrderService.Domain.Common;

/// <summary>
/// Currencies suportadas. Fechado de propósito: sem conversão de câmbio nem
/// currencies dinâmicas (ver decisions.md seção 2).
/// </summary>
public enum Currency
{
    BRL,
    USD,
    EUR,
}
