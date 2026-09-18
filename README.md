# Order Service

API REST de gestão de pedidos (Order Service), construída como teste técnico
para a vaga de Desenvolvedor(a) .NET Sênior. A especificação original do
desafio está em [`README.desafio.md`](./README.desafio.md); este documento
cobre como rodar, testar e usar a solução entregue.

## Stack

- [.NET 10](https://dotnet.microsoft.com/) / C#
- [ASP.NET Core Minimal API](https://learn.microsoft.com/aspnet/core/fundamentals/minimal-apis)
- [EF Core](https://learn.microsoft.com/ef/core/) + [Npgsql](https://www.npgsql.org/) (PostgreSQL 16)
- [JWT Bearer](https://learn.microsoft.com/aspnet/core/security/authentication/jwtbearer) (`Microsoft.AspNetCore.Authentication.JwtBearer`)
- [xUnit](https://xunit.net/) + [Testcontainers](https://testcontainers.com/) para testes de integração
- Docker / Docker Compose

## Arquitetura

Clean Architecture, com quatro projetos em `src/` refletindo as camadas
`Domain` → `Application` → `Infrastructure` → `Api` (dependências apontam
sempre para dentro, em direção ao `Domain`):

```
src/
  OrderService.Domain/           # entidades, value objects, exceções e invariantes de negócio
  OrderService.Application/      # casos de uso (handlers), DTOs, abstrações (interfaces de repositório)
  OrderService.Infrastructure/   # EF Core, migrations, repositórios, seeder
  OrderService.Api/              # Minimal API, endpoints, auth JWT, tratamento global de erros, Dockerfile
tests/
  OrderService.Domain.Tests/         # testes unitários de domínio
  OrderService.Application.Tests/    # testes unitários de casos de uso (handlers)
  OrderService.IntegrationTests/     # testes de integração (Postgres via Testcontainers)
docs/
  decisions.md                   # registro completo de decisões técnicas
```

## Como rodar via Docker (caminho recomendado)

Pré-requisito: apenas Docker com Docker Compose. Nenhum passo manual no host
é necessário — inclusive o certificado HTTPS é gerado automaticamente
durante o build da imagem (ver aviso abaixo).

```bash
docker compose up --build
```

Isso sobe dois serviços, `db` (Postgres 16) e `api`. A API espera o Postgres
ficar saudável (`depends_on: condition: service_healthy`), aplica as
migrations pendentes (`Database.Migrate()`) e semeia o catálogo de
produtos/estoque no startup, tudo antes de aceitar requisições.

A API fica disponível em `http://localhost:5100` (HTTP) e também em
`https://localhost:5101` (HTTPS). Os exemplos abaixo usam HTTP por
simplicidade.

O Swagger UI fica disponível em `http://localhost:5100/swagger` (ou
`https://localhost:5101/swagger`) também quando rodando via
`docker compose up`.

> ⚠️ Isso foi ligado de propósito para facilitar a avaliação da API, via
> uma flag dedicada (`EnableSwagger`, ativa só no `docker-compose.yml`).
> `ASPNETCORE_ENVIRONMENT` continua `Production` e nenhum outro
> comportamento de ambiente muda — não é assim que Swagger seria exposto
> num ambiente de produção real. Detalhes em `docs/decisions.md`, seção 31.

> ⚠️ **O HTTPS acima também é só conveniência local.** O certificado é
> autoassinado, gerado pelo próprio .NET (`dotnet dev-certs https`) do zero
> a cada `docker compose up --build`, dentro do `Dockerfile` — não depende
> de nada instalado no host. Por não ser um certificado confiável por
> nenhuma cadeia real, chamar `https://localhost:5101` exige `curl -k`/
> `--insecure` ou aceitar o aviso do navegador; isso é esperado, não um
> bug. A senha do `.pfx` (`devcert-password`) é só um valor de conveniência
> local. Em produção o TLS seria terminado por um proxy/load balancer com
> certificado válido (Let's Encrypt, ACM etc.), nunca embutido na imagem
> desta forma — ver `docs/decisions.md`, seção 30.

### Credenciais de desenvolvimento

Não há cadastro de usuários — a autenticação simula um único usuário fixo
(ver [`docs/decisions.md`](./docs/decisions.md), seção 3). As credenciais já
vêm configuradas no `docker-compose.yml` e em
`src/OrderService.Api/appsettings.Development.json`:

- **username**: `admin`
- **password**: `Dev@123456`

### Fluxo de exemplo via curl

```bash
# 1. Obter token
curl -X POST http://localhost:5100/auth/token \
  -H "Content-Type: application/json" \
  -d '{"username":"admin","password":"Dev@123456"}'
# -> { "accessToken": "...", "expiresAt": "..." }

TOKEN="<accessToken retornado acima>"

# 2. Criar pedido (productId abaixo é o "Notebook" semeado por padrão)
curl -X POST http://localhost:5100/orders \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{
        "customerId": "11111111-1111-1111-1111-111111111111",
        "currency": "BRL",
        "items": [
          { "productId": "00000000-0000-7000-8000-000000000001", "quantity": 1 }
        ]
      }'
# -> 201 Created, corpo com o OrderDto (inclui "items")

ORDER_ID="<id retornado acima>"

# 3. Confirmar pedido (idempotente)
curl -X POST http://localhost:5100/orders/$ORDER_ID/confirm \
  -H "Authorization: Bearer $TOKEN"

# 4. Listar pedidos do cliente
curl "http://localhost:5100/orders?customerId=11111111-1111-1111-1111-111111111111" \
  -H "Authorization: Bearer $TOKEN"
```

Produtos semeados por padrão (catálogo fixo, `BRL`, ver
`src/OrderService.Infrastructure/Persistence/Seed/DatabaseSeeder.cs`):

| Produto | ProductId | Preço | Estoque inicial |
|---|---|---|---|
| Notebook | `00000000-0000-7000-8000-000000000001` | 4599.90 | 15 |
| Mouse | `00000000-0000-7000-8000-000000000002` | 89.90 | 200 |
| Teclado | `00000000-0000-7000-8000-000000000003` | 249.90 | 120 |
| Monitor | `00000000-0000-7000-8000-000000000004` | 1299.00 | 40 |

## Como rodar localmente sem Docker completo (para debugar)

Suba só o Postgres via Compose e rode a API com `dotnet run`:

```bash
docker compose up db
dotnet run --project src/OrderService.Api
```

Nesse modo a API usa `appsettings.Development.json` (credenciais acima já
configuradas, connection string apontando para `localhost:5432`) e, com
`ASPNETCORE_ENVIRONMENT=Development`, expõe o Swagger UI automaticamente
(o mesmo Swagger UI também fica disponível via `docker compose up`, ligado
por uma flag dedicada — ver seção anterior).

## Como rodar os testes

```bash
dotnet test
```

O projeto tem 3 baterias de testes, totalizando 110 testes automatizados:

- `tests/OrderService.Domain.Tests` — regras de negócio e invariantes do domínio (unitários, sem I/O).
- `tests/OrderService.Application.Tests` — casos de uso/handlers (unitários, com dublês de repositório).
- `tests/OrderService.IntegrationTests` — repositórios EF Core, migrations, seeder e endpoints HTTP fim-a-fim, contra um Postgres real.

Os testes de integração usam **Testcontainers**, que sobe um container de
Postgres descartável automaticamente a cada execução — não é necessário ter
o `docker compose up` já de pé, mas o **Docker precisa estar disponível** na
máquina/ambiente onde `dotnet test` é executado.

## Endpoints

| Método | Rota | Autenticação | Descrição |
|---|---|---|---|
| `POST` | `/auth/token` | não | Emite um JWT a partir de `username`/`password` do usuário único configurado. |
| `POST` | `/orders` | JWT | Cria um pedido (nasce em `Placed`), reservando estoque dos itens. |
| `POST` | `/orders/{id}/confirm` | JWT | Confirma um pedido `Placed` → `Confirmed`. Idempotente. |
| `POST` | `/orders/{id}/cancel` | JWT | Cancela um pedido `Placed`/`Confirmed` → `Canceled`, liberando estoque reservado. Idempotente. |
| `GET` | `/orders/{id}` | JWT | Retorna o pedido completo, incluindo itens. |
| `GET` | `/orders` | JWT | Lista pedidos paginados, com filtros `customerId`, `status`, `from`, `to`, `page`, `pageSize`. Retorna resumo, **sem** itens. |
| `GET` | `/health` | não | Health check leve (sem tocar o banco), usado pelo `healthcheck` do serviço `api` no Compose. |

Todos os endpoints de `/orders/*` exigem `Authorization: Bearer <token>`
(sem papéis/RBAC, sem isolamento por `customerId` — qualquer usuário
autenticado pode operar qualquer pedido; ver "Decisões técnicas" abaixo).

## Atenção: contrato diferente entre `GET /orders` e `GET /orders/{id}`

A listagem paginada (`GET /orders`) retorna um **resumo** de cada pedido,
**sem** a coleção de itens — decisão de performance tomada para evitar
trazer/descartar `OrderItem` de cada pedido só para montar uma tabela de
resultados (ver [`docs/decisions.md`](./docs/decisions.md), seção 25.3). O
detalhe completo, com itens, só está disponível em `GET /orders/{id}`.

**`GET /orders` — cada item da lista (`OrderSummaryDto`):**

```json
{
  "id": "5f2e...",
  "customerId": "11111111-1111-1111-1111-111111111111",
  "currency": "BRL",
  "status": "Placed",
  "total": 4599.90,
  "createdAt": "2026-09-17T20:10:00Z"
}
```

**`GET /orders/{id}` — pedido completo (`OrderDto`):**

```json
{
  "id": "5f2e...",
  "customerId": "11111111-1111-1111-1111-111111111111",
  "currency": "BRL",
  "status": "Placed",
  "total": 4599.90,
  "createdAt": "2026-09-17T20:10:00Z",
  "items": [
    {
      "productId": "00000000-0000-7000-8000-000000000001",
      "unitPrice": 4599.90,
      "currency": "BRL",
      "quantity": 1,
      "lineTotal": 4599.90
    }
  ]
}
```

O envelope de paginação de `GET /orders` não muda:
`{ "items": [...], "page": 1, "pageSize": 20, "totalCount": 1, "totalPages": 1 }`
(aqui `items` é a lista de pedidos, não os itens de um pedido).

## Decisões técnicas — resumo

O README do desafio deixa algumas escolhas em aberto, e as respostas a elas
estão registradas em detalhe em [`docs/decisions.md`](./docs/decisions.md)
(31 seções). Vale destacar quatro que moldam bastante o comportamento da
API:

Estoque é reservado já no `POST /orders` (`Available` → `Reserved`, na
mesma transação do insert do pedido), não só no `confirm`. Isso fecha a
janela de corrida entre "pedido aceito" e "pedido confirmado" — `confirm`
apenas transiciona o estado, e `cancel` devolve a reserva para
`Available` (seção 1).

Não existe cadastro de usuários no domínio. `POST /auth/token` valida
contra um único usuário semeado por configuração, simulando a fronteira de
um Identity Provider externo sem gastar o escopo do teste em gestão de
identidade (seção 3).

Exceções de domínio e de aplicação são mapeadas para status HTTP de forma
consistente: `ValidationException` vira 400, `OrderNotFoundException` vira
404, `InvalidOrderStateTransitionException` vira 409,
`ProductNotFoundException` e as demais `DomainException` viram 422, e o que
não é tratado cai em 500 (seções 18-19).

A chave primária das entidades é Guid v7 em vez de v4 ou de um `bigint`
sequencial: é time-ordered (evita o bloat de índice que um Guid v4
aleatório causaria) e, como não há isolamento de recurso por `customerId`,
também evita enumeração trivial de IDs (seção 11).

## Limitações conhecidas / débito técnico consciente

Nem tudo o que poderia estar aqui foi implementado, e isso foi uma escolha,
não descuido. Os itens abaixo ficaram de fora ou têm um limite conhecido de
propósito:

- **Sem versionamento de API** (`/v1` no path) — não implementado.
- **Sem `Idempotency-Key` de header** — a idempotência de `confirm`/`cancel`
  vem da própria máquina de estados (checa o estado atual antes de
  transicionar), não de uma chave de idempotência de requisição.
- **Sem endpoint de cadastro de Product/Stock** — catálogo é semeado no
  startup (`DatabaseSeeder`), sem endpoint de escrita dedicado (fora do
  escopo MUST do desafio).
- **Índice composto `(CustomerId, Status, CreatedAt)` só ajuda quando
  `customerId` está no filtro** de `GET /orders` — listagens sem
  `customerId` (por exemplo, só por `status`, ou sem filtro nenhum) caem em
  sequential scan. Isso é aceitável para o caso de uso esperado (cliente
  consultando os próprios pedidos); um painel administrativo sem
  `customerId` exigiria um índice adicional dedicado.
- **Seeder não é 100% à prova de concorrência entre múltiplas instâncias**
  da API subindo simultaneamente — mitigado capturando a violação de
  unicidade do Postgres como sinal de "outra instância já semeou", mas não
  é um lock distribuído completo. Aceitável para o escopo do teste
  (`docker compose up` sobe uma única instância da API).

Cada um desses pontos tem a justificativa completa em
[`docs/decisions.md`](./docs/decisions.md).

## Checklist do desafio

- [x] API roda local e via Docker
- [x] Migrations aplicadas automaticamente
- [x] Endpoints MUST implementados
- [x] JWT + autorização básica funcionando
- [x] `dotnet test` passando
- [x] README com passo a passo
