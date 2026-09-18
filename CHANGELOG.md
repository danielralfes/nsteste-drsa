# Changelog

Formato baseado em [Keep a Changelog](https://keepachangelog.com/pt-BR/1.0.0/).
É uma entrega única para o teste técnico, então organizei as entradas pelas
fatias de implementação, na mesma ordem cronológica dos commits. O porquê
de cada decisão está em [`docs/decisions.md`](./docs/decisions.md).

## [0.1.0] - 2026-09-17

### Added

- **Fatia 0 — Bootstrap.** Estrutura da solução em Clean Architecture
  (`OrderService.Domain`, `.Application`, `.Infrastructure`, `.Api`) e
  projetos de teste (`.Domain.Tests`, `.Application.Tests`,
  `.IntegrationTests`).
- **Fatia 1 — Domínio.** Agregado `Order` (com `OrderItem`), `Product`,
  `Stock`, value object `Money`/`Currency`, hierarquia de exceções de
  domínio (`DomainException`) e invariantes de negócio (quantidade > 0,
  preço não-negativo, sem itens duplicados por produto, transições de
  estado válidas).
- **Fatia 2 — Application.** Casos de uso (`CreateOrder`, `ConfirmOrder`,
  `CancelOrder`, `GetOrderById`, `ListOrders`), DTOs, paginação/filtros de
  `ListOrders` e exceções de aplicação (`NotFoundException`,
  `ValidationException`).
- **Fatia 3 — Infrastructure.** Persistência com EF Core/Npgsql,
  migrations, repositórios (incluindo `GetByIdsAsync` em lote para evitar
  N+1 em `CreateOrder`), seeder idempotente de catálogo/estoque.
- **Fatia 4 — API.** Endpoints REST via Minimal API, `GlobalExceptionHandler`
  mapeando exceções para `ProblemDetails` (com `traceId` de correlação),
  Swagger gateado por ambiente de desenvolvimento.
- **Fatia 5 — Autenticação.** `POST /auth/token` com usuário único (PBKDF2,
  210k iterações), JWT Bearer (expiração de 60 min) e
  `.RequireAuthorization()` em todos os endpoints de `/orders/*`; proteção
  contra timing side-channel de enumeração de usuário.
- **Fatia 6 — Performance de `GET /orders`.** Índice composto
  `(CustomerId, Status, CreatedAt)`, projeção direta para `OrderSummaryDto`
  na listagem (sem `Include`/`AsSplitQuery`) e teste confirmando ausência de
  N+1 (2 queries para qualquer tamanho de página).
- **Fatia 7 — Docker fim-a-fim.** `Dockerfile` multi-stage da API e serviço
  `api` no `docker-compose.yml`, com `depends_on: condition: service_healthy`
  — `docker compose up` sobe API + banco, aplica migrations e semeia dados
  automaticamente.

### Changed

- **Contrato de `GET /orders` (Fatia 6, breaking change).** A listagem
  paginada deixou de retornar os itens de cada pedido — passa a devolver
  `OrderSummaryDto` (`id`, `customerId`, `currency`, `status`, `total`,
  `createdAt`, sem `items`). O detalhe completo com itens continua em
  `GET /orders/{id}` (`OrderDto`), inalterado. Ver README, seção "Atenção:
  contrato diferente entre `GET /orders` e `GET /orders/{id}`".

### Fixed

- Revisão da Fatia 3: corrigido um comentário que explicava errado o motivo
  de usar `AsSplitQuery()`, alinhadas as versões de EF Core/Npgsql (o
  warning `MSB3277` some) e documentado o contrato da shadow property
  `Order.Total`.
- Revisão da Fatia 4: Swagger parou de responder fora de `Development`
  (antes vazava mesmo em produção); `OperationCanceledException` deixou de
  virar 500 com log de erro; `ProblemDetails.Type` passou a apontar para uma
  URI própria em vez de um domínio de terceiros.
- Revisão de segurança da Fatia 5: eliminado um timing side-channel que
  permitia inferir se o username existia (`PasswordHasher.Verify` agora é
  sempre chamado, inclusive contra um hash dummy quando o username não
  confere); `ValidateOnStart()` de `AuthOptions`/`JwtOptions` ganhou
  validação de fato, não só o nome do método sugerindo isso.
- Revisão da Fatia 7: instalada a biblioteca `libgssapi-krb5-2` na imagem
  runtime, o que elimina um log de erro espúrio do Npgsql toda vez que um
  container novo sobe; adicionado `healthcheck` ao serviço `api` no
  `docker-compose.yml`.
