using NSubstitute;
using OrderService.Application.Abstractions;

namespace OrderService.Application.Tests.TestSupport;

/// <summary>
/// Fábrica de um <see cref="IUnitOfWork"/> mockado cujo
/// <see cref="IUnitOfWork.ExecuteInTransactionAsync"/> só invoca o delegate recebido, sem
/// transação real. Serve pra testar a orquestração dos handlers aqui; a atomicidade de
/// banco de verdade é coberta pelos testes de integração.
/// </summary>
internal static class UnitOfWorkSubstitute
{
    public static IUnitOfWork Create()
    {
        var unitOfWork = Substitute.For<IUnitOfWork>();

        unitOfWork
            .ExecuteInTransactionAsync(Arg.Any<Func<CancellationToken, Task>>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var operation = callInfo.ArgAt<Func<CancellationToken, Task>>(0);
                var cancellationToken = callInfo.ArgAt<CancellationToken>(1);
                return operation(cancellationToken);
            });

        return unitOfWork;
    }
}
