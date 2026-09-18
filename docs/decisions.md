# Decisões técnicas — Order Service

Este documento registra decisões tomadas para resolver ambiguidades do
`README.desafio.md`. Cada item indica a decisão, o motivo e, quando aplicável,
o trade-off ou alternativa descartada.

---

## 1. Estoque — modelo de reserva

`Stock` tem dois contadores: `AvailableQuantity` e `ReservedQuantity`.

- **`POST /orders` (nasce `Placed`)**: move a quantidade pedida de
  `Available` → `Reserved`, de forma atômica (mesma transação do insert do
  `Order`). Fecha a janela de corrida entre "pedido aceito" e "pedido
  confirmado": dois pedidos concorrentes sobre o mesmo estoque escasso não
  podem ambos ser aceitos como válidos se só um tem estoque.
- **`POST /orders/{id}/confirm`**: **não decrementa `ReservedQuantity`**.
  Apenas transiciona o estado `Placed` → `Confirmed`. A baixa definitiva do
  estoque (ex.: em um fulfillment/shipment) está fora do escopo desta v1 —
  o estoque "reservado" permanece reservado até cancelamento ou até um
  processo de fulfillment futuro (não implementado) o consumir de fato.
  Isso é intencional: não é um bug o estoque "nunca sair" de `Reserved`
  neste sistema — é o limite explícito do escopo v1.
- **`POST /orders/{id}/cancel`** (de `Placed` ou `Confirmed`): devolve a
  quantidade de `Reserved` → `Available`.

O README sugere reservar/baixar estoque só no `Confirm`, mas segui outro
caminho: isso permitiria um pedido "criado com sucesso" que falha depois ao
confirmar por falta de estoque, o que é ruim para quem consome a API e é um
bug clássico de concorrência em order services. Reservar já no `Placed`
fecha essa janela de corrida logo no ponto de criação.

### Atomicidade da reserva

A reserva usa um `UPDATE` condicional:

```sql
UPDATE stock SET available_quantity = available_quantity - @qty,
                  reserved_quantity = reserved_quantity + @qty
WHERE product_id = @productId AND available_quantity >= @qty;
```

O código **verifica `rowsAffected`**: se vier `0`, a reserva falhou (estoque
insuficiente) — lança erro de domínio (`InsufficientStockException`) e
**aborta a transação inteira** (o insert do `Order`/`OrderItem` e a reserva de
estoque de todos os itens do pedido vivem na mesma transação: tudo ou nada).

---

## 2. Currency — coerência entre pedido e catálogo

- `Product.UnitPrice` é armazenado com sua própria `Currency`.
- Na criação do pedido, **se a currency do pedido divergir da currency de
  qualquer produto do carrinho**, a criação é rejeitada (400/422) com
  mensagem clara identificando o produto e as currencies em conflito.
- **Sem conversão de câmbio** — fora de escopo.

Isso fecha o buraco de um pedido em `USD` somar silenciosamente preços de
produtos cadastrados em `BRL`, o que corromperia o `Total`.

`Currency` é um enum fechado (`BRL`, `USD`, `EUR`), serializado como string no
JSON. Todos os itens de um mesmo pedido devem ter a mesma currency do pedido.

---

## 3. Autenticação (`POST /auth/token`)

Usuário único semeado via configuração/variável de ambiente (não há cadastro
de usuário no domínio). O endpoint recebe `username`/`password` e retorna um
JWT com claim `sub`.

O README trata segurança como "mínimo" — só exige JWT funcionando. Simular
um único usuário fixo representa bem a fronteira de um Identity Provider
externo (OAuth2/OIDC) sem gastar o escopo do teste implementando gestão de
identidade, que não é o domínio que está sendo avaliado aqui.

**Simplificação assumida**: não há vínculo usuário↔`customerId` — qualquer
usuário autenticado pode operar qualquer `customerId`. Em produção, isso
seria resolvido por claims no token vindas de um IdP real (ex.: `customerId`
ou `role` no JWT) e autorização a nível de recurso.

---

## 4. Autorização

`[Authorize]` global em todos os endpoints de `orders` — sem papéis (RBAC) e
sem isolamento por dono do recurso (`customerId`).

Isso é consistente com a decisão de Auth (usuário único, sem vínculo a
`customerId`), e o README pede explicitamente só "autorização básica", não
RBAC. Isolamento por dono do recurso exigiria modelar uma relação
usuário-customerId que simplesmente não existe no domínio descrito.

**Consequência de segurança aceita**: ver seção 11 (enumeração de IDs) — o
uso de Guid como chave pública mitiga, mas não substitui, autorização real.

---

## 5. Testes de integração — Testcontainers

Testes de integração usam Testcontainers: `dotnet test` sobe um Postgres
descartável automaticamente, sem depender de `docker compose up` já estar de
pé.

É o padrão de mercado para ter reprodutibilidade idêntica entre local e CI,
e como o README já assume Docker como dependência do projeto (`docker
compose up` sobe API + banco), usar Docker também nos testes não introduz
nada novo. Com isso, `dotnet test` fica autossuficiente, como o checklist
pede.

---

## 6. Seed de dados

**Não usar `HasData`** para `Stock`. `HasData` é adequado para dados
imutáveis, mas quantidade de estoque é dado mutável em runtime (é
decrementado/incrementado pelas operações de pedido) — se fosse `HasData`,
o EF Core tentaria "resetar" o valor a cada nova migration que tocasse a
entidade, sobrescrevendo estoque real.

Em vez disso: um **seeder idempotente** executado no startup da API, **após**
`Database.Migrate()`. O seeder verifica se os produtos/estoque já existem
(por `ProductId` conhecido) e só insere o que faltar — seguro para rodar em
todo restart sem duplicar ou resetar dados. Produtos fixos (catálogo) também
são inseridos por este seeder, não por `HasData`.

---

## 7. Auto-migrate no startup (MUST do checklist)

A API aplica `Database.Migrate()` automaticamente no startup, antes de
aceitar requisições, com log explícito do início/fim da aplicação de
migrations (quantas pendentes, quais foram aplicadas). Isso é item **MUST**
do checklist do README ("Migrations aplicadas automaticamente") — não é
opcional nem delegado a um container de init separado.

---

## 8. Total do pedido — coluna persistida

`Order.Total` é calculado na criação do pedido e **persistido como coluna**,
não recomputado em runtime a cada leitura.

`GET /orders` é um NFR de performance explícito do README, e computar o
total somando itens a cada linha da listagem obrigaria a trazer/agrupar
`OrderItem` só para exibir um número. Persistir evita esse custo e mantém a
listagem como uma query direta e fácil de projetar.

---

## 9. Paginação e filtros

- `pageSize`: default `20`, máximo `100`. Valores acima de 100 são
  rejeitados (400), não truncados silenciosamente.
- `from`/`to`: tratados em **UTC**, **inclusivos**, aplicados sobre
  `CreatedAt` armazenado como `timestamptz` no Postgres.
- `status`: aceito como string do enum (`Draft`, `Placed`, `Confirmed`,
  `Canceled`); valor fora do enum retorna 400.
- Envelope de resposta: `{ items, page, pageSize, totalCount, totalPages }`.

---

## 10. Arredondamento monetário

Cálculo de `Total` usa `MidpointRounding.AwayFromZero` explicitamente — não o
banker's rounding (`ToEven`) que é o default implícito em vários contextos
do .NET. Escolha explícita e documentada para evitar ambiguidade sobre qual
regra de arredondamento está em vigor. Precisão: `decimal(18,2)`.

---

## 11. Chave primária — Guid v7 (não sequencial, não v4)

PK das entidades (`Order`, `OrderItem`, `Product`, `Stock`) é `Guid`, gerado
com `Guid.CreateVersion7()` (nativo a partir do .NET 9/10 — projeto targeta
**.NET 10**, dentro do que o README permite: ".NET 8+"). Caso o ambiente de
build fique restrito a .NET 8, usar uma implementação mínima própria de
UUIDv7 no `Domain` (sem trazer biblioteca externa só para isso).

Por que v7 e não v4: UUIDv7 é *time-ordered*, então a inserção no índice
B-tree da PK fica quase sequencial. Isso elimina a perda de localidade e o
bloat de índice que um UUIDv4 aleatório causaria em alto volume de inserção.

Por que não um `bigint` sequencial: como a decisão de autorização (seção 4)
é `[Authorize]` global sem isolamento por `customerId`, um ID sequencial
exposto em `GET /orders/{id}` permitiria enumerar trivialmente todos os
pedidos do sistema. O Guid não substitui autorização adequada, mas ao menos
remove esse vetor de enumeração.

**Nota sobre Postgres**: o problema clássico de *page split* de PK aleatória
é um problema de índice clustered (ex.: SQL Server, onde a PK organiza
fisicamente a tabela). No Postgres a tabela é *heap* — a PK não é clustered
por padrão — então esse problema específico não se aplica aqui; o ganho de
UUIDv7 sobre v4 no Postgres é sobre a **localidade do próprio índice da PK**
(menos fragmentação/bloat do índice B-tree), não sobre a tabela.

**Geração no Domain, não no banco**: os IDs são gerados no construtor da
entidade (camada `Domain`), com `ValueGeneratedNever()` configurado no EF
Core (Fluent API) — a entidade já nasce válida e identificável em memória,
sem depender de round-trip ao banco nem de `DEFAULT` do Postgres.

**Mapeamento no banco**: a migration gerada deve mapear `Guid` para o tipo
nativo `uuid` do Postgres (16 bytes) via Npgsql — não `text`/`varchar`. Isso
é validado ao revisar a migration na Fatia 3.

### Alternativa avaliada e descartada

PK `bigint` interna + Guid público separado (chave dupla: uma sequencial
para performance de índice interno, outra Guid exposta na API). Resolveria
tanto performance quanto enumeração, mas dobra as chaves em todas as
entidades e relacionamentos, adicionando complexidade desproporcional ao
escopo de 3 dias deste teste. Descartada em favor de Guid v7 único.

---

## 12. O que de fato move o ponteiro de performance (NFR)

O tipo da PK (Guid v7 vs sequencial) tem impacto real, mas secundário. O
ganho relevante para o NFR de performance do README está em (a implementar
na Fatia 6):

- Índice composto em `(CustomerId, Status, CreatedAt)` cobrindo os filtros de
  `GET /orders`.
- Índice na FK `OrderItem.OrderId`.
- `AsNoTracking()` em todas as queries de leitura.
- Projeção direta para DTO na listagem (sem materializar a entidade completa
  e seus itens).
- Ausência de N+1 ao carregar itens do pedido (query única ou
  `AsSplitQuery()` deliberado, medido).

---

## 13. Draft (estado não alcançável na v1)

O status `Draft` existe no enum `OrderStatus` mas não é exposto por nenhum
endpoint na v1 — todo pedido nasce diretamente como `Placed`. Mantido no
enum por fidelidade ao modelo sugerido no README e para não fechar a porta a
um fluxo de rascunho futuro.

---

## 15. Escopo explicitamente fora do MUST

- Versionamento de API (`/v1` no path) — não implementado, sinalizado como
  próximo passo.
- Endpoint de cadastro de Product/Stock — dados semeados (seção 6), sem
  endpoint de escrita dedicado (fora da lista de endpoints MUST do README).
- `Idempotency-Key` de header — a idempotência de `confirm`/`cancel` é
  natural da máquina de estados (checa estado atual antes de transicionar;
  se já está no estado alvo, retorna 200 com o estado atual sem
  reprocessar), sem suporte a chave de idempotência de requisição.
- Isolamento multi-tenant / entidade `Customer` própria — `customerId` é
  campo livre (`Guid`) informado pelo cliente da API.

---

## 16. Itens duplicados (mesmo ProductId) em um pedido — rejeitar

`Order.Place` **rejeita** (erro de domínio) se dois ou mais itens do payload
tiverem o mesmo `ProductId`. Não consolida silenciosamente as quantidades e
não permite as linhas como entradas independentes.

`ProductId` é a identidade natural da linha dentro do agregado `Order`, então
duas linhas para o mesmo produto representam o mesmo fato de domínio em dois
estados diferentes — o que quebra a coesão do agregado. Consolidar
silenciosamente esconderia um provável bug do chamador (duplo submit,
carrinho mal formado), fazendo o pedido "dar certo" com uma quantidade que o
cliente talvez não tenha pedido de fato. Permitir como linhas independentes
seria pior ainda: empurraria a garantia da invariante para a `Infrastructure`
(Fatia 3), vazando regra de domínio para fora do domínio, e deixaria o
`UPDATE` condicional de reserva de estoque (seção 1) frágil, já que duas
reservas separadas do mesmo produto poderiam passar individualmente e
estourar o total realmente disponível.

**Na API** (Fatia 4): retorna 400/422 via `ProblemDetails`, com mensagem
clara indicando o `productId` duplicado — o cliente deve reenviar com a
quantidade já consolidada em uma única linha.

**Não há ambiguidade com "atualizar quantidade"**: a v1 não tem mutação de
itens de um pedido já criado — não existe endpoint de add/update/remove de
item. O pedido nasce `Placed` com a lista completa e, depois, só sofre
transição de estado (`confirm`/`cancel`). O payload de `POST /orders` é um
**snapshot**, não uma sequência de operações — logo, duplicata nesse payload
é payload malformado, não intenção de atualizar; `quantity` já deve expressar
a quantidade consolidada pelo cliente. Se um escopo futuro introduzisse
mutação de itens, a semântica deveria ser explícita por endpoint (ex.:
`PUT /orders/{id}/items/{productId}` substituindo a quantidade, ou
`POST /orders/{id}/items` adicionando e retornando conflito se o produto já
existir) — nunca inferida a partir de uma linha duplicada no payload de
criação.

---

## 17. Invariantes de não-negatividade — defesa em profundidade

Guardas explícitas nos construtores, independentes de quem constrói a
entidade (seeder, teste, ou futura Application layer):

- `Product.UnitPrice.Amount` — deve ser `>= 0`. Ainda que hoje `Product` só
  seja criado via seeder (seção 6, sem endpoint de escrita), a invariante de
  domínio protege a entidade independentemente da origem da construção — o
  README avalia "invariantes" explicitamente como critério, e a defesa não
  deveria depender só do seeder estar correto.
- `OrderItem.Quantity` — já exigido pelo README: `> 0`.
- `Stock.AvailableQuantity` e `Stock.ReservedQuantity` — `>= 0` (já
  garantido pelo construtor desde a Fatia 1; reafirmado aqui para registro).

`Money` como value object genérico permanece permissivo a valores negativos
(pode representar deltas em contextos futuros); a restrição de
não-negatividade é responsabilidade de quem usa `Money` num contexto que a
exige (ex.: `Product.UnitPrice`), não do tipo `Money` em si.

---

## 18. Hierarquia única de exceções de domínio (sem BCL para regra de negócio)

Toda violação de **regra de negócio/invariante** no `Domain` lança uma
subclasse de `DomainException` — nunca `ArgumentException`/
`ArgumentOutOfRangeException` do BCL. Isso vale para `Product`, `Stock`,
`Money`, `OrderItem` e `Order`.

A razão é que `ArgumentOutOfRangeException` é uma exceção de contrato de API
do BCL: ela comunica "argumento errado do chamador", não "invariante de
negócio violada". Misturar as duas famílias significa que um middleware de
erro fazendo `catch (DomainException)` deixaria passar violações de
invariante de construtor para o handler genérico, retornando 500 (erro de
servidor) onde deveria ser 422 (erro do cliente). Isso é um bug real de
classificação de erro, não uma questão de estilo, e contraria os critérios
de "invariantes" e "validação de input" que o README avalia.

**Exceção à regra — guarda de nulidade**: `ArgumentNullException` continua
legítima para referência nula. É contrato de programação (uma dependência
obrigatória não foi fornecida), não regra de negócio, então não é convertida
para `DomainException`.

### Hierarquia (todas derivam de `DomainException`, abstrata, no `Domain`)

- `InvalidQuantityException` — quantidade inválida (`OrderItem.Quantity`,
  parâmetros de `Stock.TryReserve`/`Release`).
- `InvalidPriceException` — `Product.UnitPrice.Amount` negativo.
- `DuplicateProductInOrderException` — `ProductId` duplicado em `Order.Place`
  (seção 16).
- `InsufficientStockException` — reservado para uso pela Application na
  Fatia 2/3 (ver nota abaixo; hoje não é lançada por nenhum código do
  Domain).
- `InvalidOrderStateTransitionException` — transição de estado inválida em
  `Order.Confirm`/`Cancel`.
- `InvalidProductNameException` — `Product.Name` vazio/em branco. Adicionada
  para fechar 100% a regra desta seção: nome vazio é também violação de
  regra de negócio (um produto sem nome não é um `Product` válido), não erro
  de contrato de chamador — mesma lógica aplicada a `InvalidPriceException`.

### Dados estruturados nas exceções

Cada exceção carrega os dados relevantes como propriedades tipadas (não só
mensagem textual), para o middleware da Fatia 4 popular o `extensions` do
`ProblemDetails` sem parsear string:
- `DuplicateProductInOrderException.ProductId`.
- `InsufficientStockException.ProductId`, `Requested`, `Available`.
- Demais exceções carregam os valores inválidos relevantes ao contexto.

### Mapeamento HTTP (a implementar na Fatia 4, registrado aqui para não ser decidido de improviso depois)

- `DomainException` (genérico, qualquer subtipo não listado abaixo) → `422
  Unprocessable Entity`.
- `InvalidOrderStateTransitionException` → `409 Conflict` (transição de
  estado inválida é um conflito com o estado atual do recurso, não apenas
  "entidade semanticamente inválida" — mais preciso que 422 nesse caso
  específico).
- Exceção não tratada/não mapeada → `500 Internal Server Error`.

### Nota sobre `InsufficientStockException` — possível código morto

`Stock.TryReserve` retorna `bool` e **não lança** essa exceção — mantém o
padrão `Try*` idiomático; a conversão de `false` em erro é responsabilidade
da Application layer (Fatia 2/3), que decide o que fazer com a falha de
reserva. Se, ao final da Fatia 2/3, nenhum código realmente lançar
`InsufficientStockException` (por exemplo, se a Application optar por outro
mecanismo de sinalização), ela deve ser removida do Domain como código morto
— revisitar nesse ponto.

---

## 19. Exceções de Application — mapeamento HTTP

A Fatia 2 introduziu uma segunda família de exceções, em
`OrderService.Application.Common`, **paralela e não-sobreposta** a
`DomainException` (seção 18): representam ausência de recurso ou entrada
inválida no nível de orquestração do caso de uso, não violação de invariante
do agregado.

- `NotFoundException` (abstrata) → subclasses:
  - `OrderNotFoundException` — `orderId` não existe → **404 Not Found**
    (identificador de recurso na própria URL, ex.: `GET /orders/{id}`,
    `POST /orders/{id}/confirm`).
  - `ProductNotFoundException` — `productId` referenciado no corpo de
    `POST /orders` não existe → **422 Unprocessable Entity** (mesma família
    de rejeição das demais validações de conteúdo do payload de criação —
    `productId` não é o recurso da URL, é um dado referenciado dentro do
    corpo, então semanticamente é "conteúdo do pedido inválido", não "URL
    aponta para nada").
- `ValidationException` — entrada malformada nos casos de uso (ex.:
  `page`/`pageSize` fora dos limites da seção 9) → **400 Bad Request**.

Resumo do mapeamento completo que o middleware da Fatia 4 deve implementar:

| Exceção | Status HTTP |
|---|---|
| `ValidationException` | 400 |
| `OrderNotFoundException` | 404 |
| `InvalidOrderStateTransitionException` (Domain, seção 18) | 409 |
| `ProductNotFoundException` | 422 |
| `DomainException` (demais subtipos, Domain, seção 18) | 422 |
| Não tratada | 500 |

---

## 20. Busca de produtos em lote (evita N+1 em CreateOrder)

`IProductRepository` ganhou `GetByIdsAsync(IReadOnlyCollection<Guid> productIds)`,
retornando os produtos encontrados em uma única chamada. `CreateOrderHandler`
usa esse método (uma query) em vez de chamar `GetByIdAsync` item a item em
loop (N queries para um pedido com N itens).

Com `GetByIdAsync` por item, um pedido com vários itens gerava uma consulta
por produto. Não é um gargalo real no volume deste teste, mas contraria o
NFR de performance que o README pede. A implementação EF Core da Fatia 3
resolve `GetByIdsAsync` com `WHERE ProductId IN (...)` numa única query, com
`AsNoTracking()` (consistente com a seção 12). `GetByIdAsync` (singular)
continua na interface para os casos que precisam buscar um único produto
isoladamente.

---

## 21. Ajustes pós-review da Fatia 3 (Infrastructure)

Quatro pontos levantados na revisão da Fatia 3, todos corrigidos antes do
commit:

**21.1 — Justificativa de `AsSplitQuery()` corrigida.** O comentário original
em `OrderRepository.ListAsync` e o nome do teste de integração
correspondente afirmavam que `AsSplitQuery()` evita "duplicação de linhas
Order" (produto cartesiano com `Items`). Isso é impreciso: o EF Core já
resolve a materialização de uma única instância de `Order` por resultado via
*entity fixup*/identity resolution, mesmo com `JOIN` único (confirmado
empiricamente pelo QA removendo `AsSplitQuery()` e vendo o teste continuar
passando). O ganho real de `AsSplitQuery()` aqui é **performance/tráfego de
rede** — evita reenviar as colunas de `Order` repetidas uma vez por item
filho — não correção de contagem de linhas materializadas. Comentário no
código e nome do teste corrigidos para refletir o motivo real.

**21.2 — Versões de EF Core/Npgsql alinhadas.** Havia um warning `MSB3277`
por descompasso entre `Microsoft.EntityFrameworkCore`/`.Design` (referência
direta) e `Microsoft.EntityFrameworkCore.Relational` (trazido
transitivamente por `Npgsql.EntityFrameworkCore.PostgreSQL`). Não havia
quebra funcional (build e os 77 testes passavam mesmo com o warning), mas
numa entrega que se propõe "pronta para produção" um warning de
descompasso de versão entre pacotes do mesmo ecossistema não deveria ficar
sem decisão registrada. Resolvido fixando as versões de
`Microsoft.EntityFrameworkCore`/`.Design` na mesma linha suportada pela
versão atual de `Npgsql.EntityFrameworkCore.PostgreSQL` usada no projeto,
eliminando o warning.

**21.2.1 — Atualização (pós-entrega): causa-raiz diferente reapareceu ao
atualizar pacotes.** Ao atualizar `Microsoft.EntityFrameworkCore`/`.Design`
para `10.0.12` (a mais recente estável na época), o `MSB3277` voltou — mas
por um motivo diferente do original. `Microsoft.EntityFrameworkCore.Design
10.0.12` fixa sua dependência de `Microsoft.EntityFrameworkCore.Relational`
exatamente em `10.0.12`, porém marcada `PrivateAssets=all` — ou seja, essa
versão não se propaga via `ProjectReference` para os projetos consumidores
(`Api`, `IntegrationTests`). Esses projetos, sem uma referência direta,
resolviam o piso `10.0.4` aceito pelo range de
`Npgsql.EntityFrameworkCore.PostgreSQL 10.0.3` (`[10.0.4, 11.0.0)`),
enquanto `OrderService.Infrastructure.dll` já estava compilado contra
`Relational 10.0.12` — o mesmo tipo de descompasso de assembly, origem
nova. **Corrigido** adicionando uma referência direta e **não-privada** a
`Microsoft.EntityFrameworkCore.Relational 10.0.12` em
`OrderService.Infrastructure.csproj`, forçando essa versão a se propagar
para `Api`/`IntegrationTests` via `ProjectReference`. O pin continua
necessário — reavaliar quando o provider `Npgsql.EntityFrameworkCore.PostgreSQL`
lançar uma versão estável dependendo de EF Core 11.x.

**21.3 — Shadow property `Order.Total` — contrato documentado.**
`Order.Total` é sincronizado como shadow property (`TotalAmount`) apenas
dentro de `OrderServiceDbContext.SaveChanges`/`SaveChangesAsync` (seção 8).
**Risco identificado**: qualquer escrita futura em `Order` que não passe por
esse `SaveChanges` (ex.: `ExecuteUpdateAsync` em massa sobre a tabela
`orders`, ou acesso via outro `DbContext`/SQL raw) dessincronizaria o `total`
persistido silenciosamente, sem erro de compilação nem teste automático
pegando isso hoje. **Mitigação**: comentário reforçado no `DbContext` e
nesta seção deixando explícito que **todo write de `Order` deve passar pelo
`SaveChanges`/`SaveChangesAsync` deste `DbContext`** — nenhum código deve
gravar a tabela `orders` por fora dele (bulk update, SQL raw, outro
`DbContext`). Enquanto essa regra for respeitada (é o caso de todo o código
atual do projeto), a sincronização é garantida. Se uma fatia futura
introduzir um caminho de escrita alternativo para `Order`, esse código
precisa recalcular/gravar `TotalAmount` explicitamente ou passar pelo mesmo
`DbContext`.

**21.4 — Seeder: limitação conhecida de concorrência entre instâncias.**
`DatabaseSeeder.SeedAsync` é idempotente para chamadas sequenciais (`SELECT`
dos IDs fixos conhecidos, insere só o que falta), mas **não é seguro contra
duas instâncias da API subindo simultaneamente** e chamando o seeder ao
mesmo tempo: ambas poderiam fazer o `SELECT` "não existe" antes de qualquer
`INSERT`, e a segunda colidiria na PK ao tentar inserir os mesmos IDs fixos.
Considerei isso aceitável para o escopo deste teste técnico (`docker
compose up` sobe uma única instância da API), mas mitiguei tornando a falha
seguramente recuperável: o `INSERT` que colide em PK lança uma exceção de
violação de unicidade do Postgres, que é capturada e tratada como sinal de
"outra instância já semeou" — idempotência via captura de conflito — em vez
de derrubar o startup da aplicação. Não é um lock distribuído completo, mas
evita que a segunda instância trave ou corrompa o estado, o que já é
proporcional ao volume de "concorrência entre instâncias" que este teste
técnico precisa cobrir de fato.

---

## 22. Ajustes pós-review da Fatia 4 (API)

Quatro pontos levantados na revisão da Fatia 4, corrigidos antes do commit:

**22.1 — Swagger gateado por ambiente.** `UseSwagger()`/`UseSwaggerUI()`
eram registrados incondicionalmente, inclusive fora de `Development`
(confirmado rodando a API localmente com `ASPNETCORE_ENVIRONMENT` default —
Swagger respondia 200 mesmo assim). Corrigido para só registrar dentro de
`if (app.Environment.IsDevelopment())`. Motivo: nesta fatia os endpoints
ainda são anônimos por design (JWT é a Fatia 5) — expor a superfície
completa da API via Swagger em qualquer ambiente, inclusive um futuro
ambiente de produção, é o tipo de exposição fácil de esquecer de revisitar
depois que a autenticação existir.

**22.2 — `traceId` de correlação no `ProblemDetails`.** O corpo de erro (em
qualquer status, não só 500) agora inclui
`problemDetails.Extensions["traceId"] = httpContext.TraceIdentifier`,
permitindo correlacionar o relato de um cliente com a linha de log
estruturado correspondente (`_logger.LogError`/`LogWarning` já usa o mesmo
`TraceIdentifier` implicitamente via o pipeline de logging do ASP.NET Core).

**22.3 — `OperationCanceledException` tratada separadamente do 500
genérico.** Cancelamento de cliente (conexão fechada, timeout) caía no
branch de exceção não mapeada, gerando `LogError` + 500 "unexpected error"
para um evento que não é bug. Adicionado um caso específico no `switch` do
`GlobalExceptionHandler`: `OperationCanceledException` é logada em nível
`Information`/`Debug` (não `Error`) e não gera resposta HTTP de erro
completa (a conexão já foi encerrada pelo cliente) — evita poluir os logs
de erro com eventos operacionais normais.

**22.4 — `ProblemDetails.Type` usa URI própria, não um domínio de
terceiros.** O campo `type` apontava para `httpstatuses.io`. Trocado para
uma URI sob controle do próprio projeto (ex.:
`https://orderservice.local/errors/{status}` ou equivalente usado no
código), evitando que o contrato público de erro dependa de um serviço
externo fora de controle do time.

**Item não corrigido nesta fatia (adiamento consciente registrado, não
esquecimento):** `docker-compose.yml` hoje sobe apenas o Postgres — não há
`Dockerfile` da API nem serviço `api` no compose. O checklist MUST do
README ("`docker compose up` deve subir a API + banco") só é atendido
integralmente na **Fatia 7** (Docker compose fim-a-fim), conforme o plano de
fatias original. Até lá, rodar a API localmente requer `dotnet run` com o
Postgres do compose já de pé — documentado aqui para deixar claro que é
sequenciamento de entrega, não uma lacuna não percebida.

**Atualização (Fatia 7): resolvido.** `src/OrderService.Api/Dockerfile`
(build multi-stage SDK/runtime .NET 10) e o serviço `api` no
`docker-compose.yml` (`depends_on: db: condition: service_healthy`) foram
adicionados — ver seção 27. `docker compose up` agora sobe API + banco,
aplica migrations e semeia produtos automaticamente, sem passo manual.

---

## 23. Fatia 5 — Autenticação e autorização JWT

Implementa as seções 3 e 4 (já decididas, registradas aqui apenas a
implementação concreta):

- **`POST /auth/token`**: valida `username`/`password` contra o usuário
  único configurado (`Auth:Username`/`Auth:PasswordHash`) e retorna
  `{ accessToken, expiresAt }`. Único endpoint anônimo (`.AllowAnonymous()`);
  todos os 5 endpoints de `/orders/*` usam `.RequireAuthorization()`, sem
  policy/role — apenas "usuário autenticado" (autorização básica, conforme o
  README).
- **Hash de senha**: PBKDF2 (`Rfc2898DeriveBytes.Pbkdf2`, HMACSHA256,
  210.000 iterações) via `System.Security.Cryptography` — não foi adicionada
  dependência externa (ex.: BCrypt.Net) para isso, já que o BCL cobre o
  algoritmo adequadamente para o escopo deste teste. Formato persistido:
  `{iterations}.{saltBase64}.{hashBase64}` (parâmetros viajam com o hash,
  permitindo aumentar `Iterations` no futuro sem invalidar hashes antigos).
  `Auth:PasswordHash` nunca é a senha em texto plano.
- **Credenciais de desenvolvimento** (`appsettings.Development.json`, só
  para rodar/testar localmente): `username = admin`,
  `password = Dev@123456` (documentado em comentário no próprio arquivo de
  configuração). Em produção, `Auth:Username`/`Auth:PasswordHash`/`Jwt:Key`
  viriam de um cofre de segredos (variáveis de ambiente injetadas pelo
  orquestrador), nunca de um `appsettings.*.json` versionado —
  `appsettings.json` (base) só tem placeholders vazios, propositalmente sem
  segredo de produção.
- **JWT**: `Microsoft.AspNetCore.Authentication.JwtBearer` (pacote padrão do
  ASP.NET Core para JWT Bearer, versão `10.0.4` para alinhar com a linha do
  EF Core já fixada — seção 21.2), chave simétrica HMAC-SHA256 via
  `Jwt:Key`/`Jwt:Issuer`/`Jwt:Audience`, expiração de **60 minutos** (sem
  refresh token nesta v1 — token expirado exige nova chamada a
  `/auth/token`). Claim `sub` carrega o `username` (não há `customerId` a
  vincular, seção 3).
- **401/403 em formato `ProblemDetails` sem código extra**: como
  `AddProblemDetails()` já estava registrado desde a Fatia 4, o próprio
  middleware de autenticação/autorização do ASP.NET Core (desde .NET 8)
  detecta o serviço registrado e escreve automaticamente um `ProblemDetails`
  para 401 (token ausente/inválido/expirado) e 403, sem precisar de
  `UseStatusCodePages()` nem de tratamento manual no `GlobalExceptionHandler`.
  Credenciais inválidas em `POST /auth/token` são tratadas diretamente no
  endpoint (`Results.Problem(...)`, 401) — é um resultado esperado do fluxo,
  não uma exceção, então não passa pelo `GlobalExceptionHandler`, mas mantém
  o mesmo formato `ProblemDetails` (incluindo `traceId`) para consistência.
- **Testes de integração** (`tests/OrderService.IntegrationTests/Api`):
  `AuthEndpointsTests` cobre emissão de token válido (JWT parseável, claim
  `sub` correta), credenciais inválidas (401), `/orders/*` sem header
  `Authorization` (401), token malformado (401), e o fluxo completo
  obter-token → criar-pedido. `ApiFactory.CreateAuthenticatedClientAsync()`
  é o helper reaproveitado por `OrdersEndpointsTests` (ajustado para exigir
  token, já que os endpoints deixaram de ser anônimos).

---

## 24. Ajustes pós-review da Fatia 5 (JWT/autorização)

Dois pontos levantados na revisão de segurança da Fatia 5, corrigidos antes
do commit:

**24.1 — Timing side-channel de enumeração de username eliminado.**
`AuthEndpoints.cs` fazia `string.Equals(username, ...) && PasswordHasher.Verify(...)`
— o `&&` faz *short-circuit*: se o username não bate, `Verify` (PBKDF2, 210k
iterações, dezenas de ms) nunca roda, criando uma diferença de latência
facilmente observável entre "username errado" (resposta rápida) e "username
certo, senha errada" (resposta lenta) — um oráculo de enumeração de usuário.
**Corrigido**: `PasswordHasher.Verify` é chamado **sempre**, mesmo quando o
username não confere — nesse caso, contra um hash PBKDF2 dummy (gerado uma
vez, com o mesmo custo computacional de um hash real) em vez de pular a
chamada. O tempo de resposta deixa de depender de o username existir ou não.

**Por que isso ficava só 🟡 e não 🔴 antes desta correção**: o sistema tem
um único usuário fixo e publicamente documentado (`admin`, seção 23) — não
há usuário a enumerar. A correção é feita mesmo assim porque é barata e
porque, se uma fatia futura introduzir múltiplos usuários, o mesmo código
sem essa correção passaria a ser uma vulnerabilidade real (🔴), não apenas
teórica.

**24.2 — `ValidateOnStart()` com validação real.** `AddOptions<AuthOptions>()`/
`AddOptions<JwtOptions>()` chamavam `.ValidateOnStart()` sem nenhum
`.Validate(...)` associado — nome de método que sugere uma garantia
("valida na inicialização") que não existia de fato: `Auth:Username`/
`Auth:PasswordHash` vazios não impediam o app de subir (login sempre
falharia com 401 depois, silenciosamente — falha fechada, mas por acidente,
não por design). Adicionado `.Validate(...)` explícito para `AuthOptions`
(`Username`/`PasswordHash` não podem ser vazios) e `JwtOptions`
(`Issuer`/`Audience`/`Key` não podem ser vazios), cada um com mensagem de
erro clara. Agora o app falha no startup de forma explícita e diagnosticável
se a configuração de auth/JWT estiver incompleta, em vez de subir "quebrado
por baixo".

---

## 25. Fatia 6 — Performance de `GET /orders` (itens adiados da seção 12)

Implementa os itens explicitamente adiados na seção 12 para esta fatia.

**25.1 — Índice composto `(CustomerId, Status, CreatedAt)`.** Adicionado via
Fluent API em `OrderConfiguration.Configure`
(`builder.HasIndex(o => new { o.CustomerId, o.Status, o.CreatedAt })`,
`IX_orders_CustomerId_Status_CreatedAt`), migration
`AddOrderListingIndexes`. Cobre exatamente os 3 filtros de `GET /orders`
(`customerId`, `status`, intervalo sobre `CreatedAt`).

**25.2 — Índice na FK `OrderItem.OrderId`.** Já existia desde a
`InitialCreate` (`IX_order_items_OrderId`) — é o comportamento padrão do EF
Core para propriedades de FK de navegação. Confirmado lendo a migration
gerada; nenhuma alteração necessária.

**25.3 — Projeção direta para DTO na listagem: ⚠️ MUDANÇA DE CONTRATO HTTP DE
`GET /orders`.** Decisão tomada: a listagem paginada **deixa de retornar os
itens de cada pedido**. `OrderRepository.ListAsync` foi reescrito para
projetar direto no banco (`.Select(o => new OrderSummaryDto(...))`, lendo o
total já persistido via `EF.Property<decimal>(o, "TotalAmount")`), sem
`.Include(o => o.Items)` nem `.AsSplitQuery()` (que deixou de ser necessário
— não há mais JOIN com tabela filha nesta query).

- Novo tipo `OrderSummaryDto` (`Id`, `CustomerId`, `Currency`, `Status`,
  `Total`, `CreatedAt` — **sem** `Items`), em
  `OrderService.Application.Orders`, usado exclusivamente por
  `ListOrdersHandler`/`IOrderRepository.ListAsync`.
- `OrderDto` (com `Items`) continua sendo o retorno de `GET /orders/{id}`
  (`GetOrderByIdHandler`), que segue usando `GetByIdAsync` com
  `Include(Items)` — **não mudou**.
- **Antes**: `GET /orders` retornava, por pedido, um objeto com
  `items: [{ productId, unitPrice, currency, quantity, lineTotal }]`.
- **Depois**: `GET /orders` retorna, por pedido, um objeto **sem** a chave
  `items` — apenas `id`, `customerId`, `currency`, `status`, `total`,
  `createdAt`. O envelope de paginação (`{ items, page, pageSize,
  totalCount, totalPages }` — onde este `items` externo é a lista de
  pedidos, não os itens de um pedido) não mudou.

O README já trata `GET /orders/{id}` e `GET /orders` como endpoints
distintos (seção "Requisitos funcionais", itens 4 e 5), e é comum — e mais
performático — a listagem devolver um resumo enquanto o detalhe devolve
tudo. Trazer `OrderItem` por página só para descartá-lo no mapeamento
custava I/O e memória à toa, exatamente o tipo de coisa que a meta "sem
materializar entidade completa e seus itens" da seção 12 já apontava.

**Se este contrato de resposta não for aceitável** (ex.: um consumidor já
depende de `items` na listagem), a reversão é local: religar
`OrderSummaryDto.Items` ou voltar a `OrderDto` em `ListAsync`, restaurando
`Include(Items)`/`AsSplitQuery()`. Sinalizado aqui com destaque máximo para
revisão explícita, conforme solicitado.

**25.4 — Ausência de N+1 confirmada por teste.**
`OrderRepositoryTests.ListAsync_WithFivePagedOrders_ExecutesExactlyTwoQueries`
usa um `QueryCountingInterceptor` (interceptor de diagnóstico do EF Core,
`tests/OrderService.IntegrationTests/TestSupport/PostgresFixture.cs`,
usado só em teste) para contar comandos SQL disparados ao listar 5 pedidos
com 2 itens cada — confirma exatamente **2** queries (`COUNT` +
`SELECT` paginado), não 5+ nem proporcional ao número de pedidos.

**25.5 — `pageSize` default/máximo.** Já implementado desde a Fatia 2
(`ListOrdersHandler.DefaultPageSize = 20`, `MaxPageSize = 100`), com
cobertura de teste pré-existente
(`ListOrdersHandlerTests.HandleAsync_WithPageSizeAboveMaximum_ThrowsValidationException`
e o teste de defaults). Nenhuma mudança necessária.

---

## 26. Limitação conhecida do índice composto `(CustomerId, Status, CreatedAt)`

Débito técnico registrado na seção 12/25.1, tornado explícito aqui (não
apenas em comentário de código) por ser um trade-off de performance real que
qualquer revisor deveria conseguir achar sem ler Fluent API.

O índice B-tree `IX_orders_CustomerId_Status_CreatedAt` segue a regra de
**leftmost-prefix**: o Postgres só consegue usar o índice (via *index scan*
ou *index-only scan*) para consultas cujo filtro inclui `CustomerId` — a
primeira coluna do índice — sozinha ou combinada com `Status`/`CreatedAt` nessa
ordem. Combinações que ele cobre eficientemente:

- `WHERE CustomerId = @x`
- `WHERE CustomerId = @x AND Status = @y`
- `WHERE CustomerId = @x AND Status = @y AND CreatedAt BETWEEN @from AND @to`
- `WHERE CustomerId = @x AND CreatedAt BETWEEN @from AND @to` (pula `Status`,
  ainda aproveita o prefixo `CustomerId`, com menor seletividade)

**Consultas que o índice não ajuda** (o planner cai para *sequential scan* na
tabela `orders` inteira): `GET /orders` chamado **sem** `customerId` — por
exemplo, só com `status=Placed`, ou só com `from`/`to`, ou sem filtro nenhum
(listagem "todos os pedidos"). Nesses casos o Postgres não tem como pular
direto para as linhas relevantes usando este índice, porque `CustomerId` (a
coluna mais à esquerda) não está no predicado.

**Por que essa ordem de colunas foi escolhida mesmo assim**: o uso mais
esperado da API (seção 12) é um cliente/consumidor consultando os próprios
pedidos — sempre informando `customerId` — o que é o caso otimizado. Um
índice separado começando por `Status` ou por `CreatedAt` (ou índices
adicionais para cobrir todas as combinações) reduziria esse ponto cego, mas
ao custo de mais índices para o Postgres manter a cada `INSERT`/`UPDATE` de
`orders` — trade-off de escrita vs. leitura não justificado para o volume
deste teste técnico.

**Se um caso de uso real exigir listagem eficiente sem `customerId`** (ex.:
um painel administrativo que lista todos os pedidos por status,
independente de cliente), a correção é adicionar um índice dedicado — por
exemplo `(Status, CreatedAt)` — e não apenas reordenar o índice existente
(que degradaria o caso hoje otimizado). Não implementado nesta v1 por não
haver, no momento, um endpoint/consumidor que precise dessa consulta sem
`customerId`.

---

## 27. Fatia 7 — Docker compose fim-a-fim

Fecha o item MUST do checklist ("`docker compose up` deve subir a API +
banco") deixado pendente desde a seção 22.

**27.1 — Local do `Dockerfile`.** `src/OrderService.Api/Dockerfile`, ao lado
do `.csproj` da API — convenção comum em soluções .NET com múltiplos
projetos (facilita achar "o Dockerfile do serviço X" quando a solução tiver
mais de uma imagem publicável no futuro). O *build context* usado pelo
`docker-compose.yml`, porém, é a **raiz do repositório**
(`context: .`, `dockerfile: src/OrderService.Api/Dockerfile`), não a pasta
da API — necessário porque o `dotnet publish` da API precisa enxergar os
`ProjectReference` de `Application`/`Infrastructure`/`Domain`, que vivem fora
da pasta `src/OrderService.Api`.

**27.2 — Build multi-stage.** Stage `build` usa
`mcr.microsoft.com/dotnet/sdk:10.0` (restore + publish, com `COPY` dos
`.csproj` antes do restante do código-fonte para aproveitar cache de camada
do Docker). Stage final usa `mcr.microsoft.com/dotnet/aspnet:10.0` (runtime
apenas, sem SDK) e copia só a saída de `dotnet publish` — imagem final
menor, sem toolchain de build.

**27.3 — Porta 8080.** `ASPNETCORE_HTTP_PORTS=8080` e `EXPOSE 8080` — porta
padrão para containers Linux no ASP.NET Core 8+ (substituindo a antiga
convenção de 80/443 com certificado). Mapeada no compose como
`"8080:8080"` (host:container).

**27.4 — Variáveis de ambiente do serviço `api` no compose.** A connection
string aponta para `Host=db` (nome do serviço no compose), não `localhost` —
dentro da rede interna do Docker Compose, os serviços se resolvem pelo nome
do serviço via DNS interno. `Auth:Username`/`Auth:PasswordHash`/`Jwt:*`
reaproveitam exatamente os valores de desenvolvimento já usados em
`appsettings.Development.json` (seção 23) — não é segredo de produção real
(já documentado como tal desde a Fatia 5); o objetivo aqui é
`docker compose up` funcionar out-of-the-box para quem for rodar/avaliar o
projeto localmente, sem passo manual de configurar credenciais antes.
`ASPNETCORE_ENVIRONMENT=Production` é usado no serviço `api` do compose
(coerente com a seção 22.1 — Swagger só habilitado em `Development`; quem
quiser explorar a API via Swagger localmente deve rodar `dotnet run` com
`ASPNETCORE_ENVIRONMENT=Development`, não via `docker compose up`).

**27.5 — Ordem de inicialização.** `depends_on: db: condition: service_healthy`
garante que o container `api` só inicia depois que o healthcheck do
Postgres (`pg_isready`, existente desde a Fatia 0) reportar saudável. A API
então aplica `Database.Migrate()` e o seeder (seções 6/7) automaticamente no
próprio startup, antes de aceitar requisições — `docker compose up` sobe
tudo com um único comando, sem passo manual. Validado executando
`docker compose down -v && docker compose up --build -d` do zero e
confirmando via `docker compose logs api` que as migrations foram aplicadas
e os produtos semeados, seguido de chamadas HTTP reais
(`/auth/token` → `POST /orders` → `GET /orders`) contra `localhost:8080`.

**27.6 — Constante `TotalAmountShadowPropertyName` (débito da seção 21.3).**
A string mágica `"TotalAmount"`, antes repetida em
`OrderServiceDbContext.SaveChanges(Async)`, `OrderConfiguration.Configure` e
`OrderRepository.ListAsync`, foi extraída para
`OrderPersistenceConstants.TotalAmountShadowPropertyName`
(`src/OrderService.Infrastructure/Persistence/OrderPersistenceConstants.cs`,
`internal`, acessível aos três pontos de uso dentro do assembly
`OrderService.Infrastructure`). Elimina o risco de um dos três pontos
divergir por erro de digitação ao renomear a shadow property no futuro.

---

## 28. Ajustes pós-review da Fatia 7 (Docker fim-a-fim)

Dois pontos levantados na revisão final, corrigidos antes do commit:

**28.1 — `libgssapi-krb5-2` instalada na imagem runtime.** Em todo
`docker compose up` a partir de um container novo, os primeiros logs da API
mostravam um erro de nível `fail:` do EF Core
(`Failed executing DbCommand ... Cannot load library
libgssapi_krb5.so.2`), antes das migrations serem aplicadas com sucesso.
Causa: a imagem `mcr.microsoft.com/dotnet/aspnet:10.0` (Debian slim) não tem
a biblioteca `libgssapi-krb5-2`; o Npgsql tenta negociação GSSAPI na
primeira conexão, falha, e cai para o fallback de autenticação
usuário/senha (que funciona — por isso migrations/seed tinham sucesso logo
em seguida). Funcionalmente inofensivo, mas é um log de nível erro que
aparece a cada start de container fresco — ruído que poderia disparar
alerta falso-positivo num monitoramento de produção. **Corrigido**:
`RUN apt-get update && apt-get install -y --no-install-recommends
libgssapi-krb5-2 && rm -rf /var/lib/apt/lists/*` adicionado ao stage final
do `Dockerfile`, eliminando a tentativa falha de GSSAPI e o log de erro
associado.

**28.2 — `healthcheck` no serviço `api` do `docker-compose.yml`.** Só o
serviço `db` tinha `healthcheck` — se o processo da API travasse (sem
crashar) após o startup, não havia forma automática de detectar isso via
`docker compose ps`/orquestrador. **Corrigido**: adicionado `healthcheck`
ao serviço `api` fazendo uma chamada HTTP simples a um endpoint leve e
sempre disponível (ex.: `GET /swagger` retornando qualquer resposta, ou um
endpoint de health dedicado, se mais apropriado — ver implementação exata
no `docker-compose.yml`), com paridade de robustez com o serviço `db`.

**Item não corrigido nesta fatia (nota para a documentação final):** o
warning `NU1903` (vulnerabilidade conhecida em `SSH.NET`, dependência
transitiva de `Testcontainers.PostgreSql`, usada apenas em
`OrderService.IntegrationTests`) continua aparecendo em `dotnet build`/
`dotnet test`. Não afeta a imagem de produção (`.dockerignore` exclui
`tests/` do contexto de build da imagem da API). Fica registrado aqui para
o README/CHANGELOG final mencionar como limitação conhecida, não como
descuido não percebido.

**Atualização (pós-entrega, seção 29): este débito foi resolvido** — ver
seção 29.1.

---

## 29. Atualização de pacotes NuGet (pós-entrega)

Depois de todas as 8 fatias entregues, os pacotes NuGet do projeto foram
atualizados para as versões estáveis mais recentes disponíveis. Resumo das
mudanças relevantes (não uma lista completa de versões — ver os `.csproj`
para o estado exato):

**29.1 — `NU1903` (vulnerabilidade em `SSH.NET`) resolvido.** O bump de
`Testcontainers.PostgreSql` (`4.1.0` → `4.15.0`) trouxe uma versão do
`SSH.NET` sem a CVE conhecida — confirmado via
`dotnet list package --vulnerable --include-transitive` não reportando mais
nada. Item fechado, não é mais débito técnico.

**29.2 — `Microsoft.EntityFrameworkCore`/`.Design` atualizados para
`10.0.12`.** Ver seção 21.2.1 para a nova causa-raiz do `MSB3277` que
reapareceu e como foi corrigida (referência direta e não-privada a
`Microsoft.EntityFrameworkCore.Relational 10.0.12` em
`OrderService.Infrastructure.csproj`).

**29.3 — Demais pacotes atualizados sem impacto de compatibilidade:**
`Microsoft.AspNetCore.Authentication.JwtBearer`, `Swashbuckle.AspNetCore`
(salto grande, `7.2.0` → `10.2.3`, sem quebra observada), `coverlet.collector`,
`Microsoft.NET.Test.Sdk`, `xunit.runner.visualstudio`, `NSubstitute`.
`Npgsql.EntityFrameworkCore.PostgreSQL` e `xunit` (linha v2) mantidos —
já estavam na versão estável mais recente disponível no momento.

**29.4 — Warnings `CS0618` (API obsoleta do `Testcontainers.PostgreSql`
4.15.0) corrigidos.** O bump para `4.15.0` tornou obsoleto o construtor
sem parâmetros de `PostgreSqlBuilder` usado no fixture de testes de
integração. Substituído pela API não obsoleta recomendada pela própria
biblioteca (ver `tests/OrderService.IntegrationTests/TestSupport/PostgresFixture.cs`).

**Validação**: `dotnet build` (0 erros, 0 warnings `MSB3277`/`NU1903`/
`CS0618`), `dotnet test` (110/110 verdes), e `docker compose up --build -d`
do zero (Postgres/API `healthy`, migrations aplicadas, seed executado,
fluxo HTTP real validado via curl) — todos revalidados após a atualização.

---

## 30. Porta 5100/5101 e HTTPS de conveniência via Docker (pós-entrega)

**30.1 — Mudança de porta HTTP (8080 → 5100).** Solicitado explicitamente
pelo usuário — não há outro motivo técnico por trás da troca, é apenas a
faixa de porta pedida. `ASPNETCORE_HTTP_PORTS`/`EXPOSE` no
`src/OrderService.Api/Dockerfile` e o mapeamento `ports`/healthcheck do
serviço `api` em `docker-compose.yml` foram atualizados de `8080` para
`5100`. Não há impacto em `tests/OrderService.IntegrationTests`: os testes
de integração usam `WebApplicationFactory`/Testcontainers (seção 5), que não
dependem do `Dockerfile`/`docker-compose.yml` de produção nem de porta fixa
— cada execução de teste sobe seu próprio `TestServer`/container Postgres
descartável.

**30.2 — HTTPS adicionado (porta 5101) como conveniência de
avaliação/execução local — NÃO é configuração de produção.**
`ASPNETCORE_HTTPS_PORTS=5101` adicionado ao `Dockerfile` (junto de
`ASPNETCORE_HTTP_PORTS=5100`), e o serviço `api` do compose ganhou o
mapeamento de porta `5101:5101`.

A primeira versão desta decisão montava o certificado de desenvolvimento do
host via `volumes:` no `docker-compose.yml`, exigindo que o usuário rodasse
`dotnet dev-certs https -ep ...` e `--trust` manualmente antes do primeiro
`docker compose up`. Essa abordagem foi descartada: o usuário decidiu que
não queria nenhum passo manual/pré-requisito no host além de ter Docker
instalado. A versão final gera o certificado **automaticamente durante o
build da própria imagem Docker**, sem volume e sem depender de nada
previamente instalado/configurado no host:

- No stage `build` do `src/OrderService.Api/Dockerfile` (imagem
  `mcr.microsoft.com/dotnet/sdk:10.0`, que tem a ferramenta `dotnet
  dev-certs` — ferramenta do SDK, não existe na imagem de runtime
  `aspnet:10.0`), é executado `dotnet dev-certs https -ep
  /https/aspnetapp.pfx -p devcert-password`, gerando um `.pfx` autoassinado
  novo a cada build da imagem.
- O stage `final` (`aspnet:10.0`, sem SDK, mantendo a imagem enxuta) copia
  esse `.pfx` pronto via `COPY --from=build /https/aspnetapp.pfx
  /https/aspnetapp.pfx`.
- `ASPNETCORE_Kestrel__Certificates__Default__Path=/https/aspnetapp.pfx` e
  `ASPNETCORE_Kestrel__Certificates__Default__Password=devcert-password`
  também são definidos diretamente no `Dockerfile` (não mais no
  `docker-compose.yml`, já que agora fazem parte da imagem).

O `docker-compose.yml` não tem mais nenhum `volumes:` nem variável de
ambiente relacionada a certificado para o serviço `api` — tudo vem pronto da
imagem construída pelo próprio `docker compose up --build`.

**Por que isso NÃO é uma configuração de produção**: o certificado é
autoassinado (`dotnet dev-certs`), gerado do zero a cada build da imagem —
não é nem o certificado de desenvolvimento pessoal do usuário, então não faz
sentido (nem seria possível de forma estável) tentar confiar nele
globalmente via `--trust` na máquina host. A "senha" do `.pfx`
(`devcert-password`) é um valor de conveniência hardcoded no `Dockerfile`,
não um segredo gerenciado por nenhum cofre. Nada disso seria aceitável em
produção. Num cenário real, TLS seria terminado por um proxy reverso/load
balancer (ex.: nginx, um Application Load Balancer/API Gateway do provedor
cloud) com um certificado válido emitido por uma CA confiável (Let's
Encrypt, ACM, etc.) e gestão de segredo real (cofre/KMS) — nunca embutido
diretamente na imagem/configuração da aplicação da forma simplificada usada
aqui. Esta configuração existe puramente para permitir que quem avalia este
teste técnico explore a API também via HTTPS localmente, sem fricção extra
e sem nenhum passo manual.

O healthcheck do serviço `api` continua usando apenas o endpoint HTTP
(`http://localhost:5100/health`) — mais simples, evita ter que lidar com
validação de certificado autoassinado dentro do próprio healthcheck.

**Validação**: `docker compose down -v` seguido de `docker compose up
--build -d` do zero, sem nenhum comando prévio no host, `docker compose ps`
confirmando `api` e `db` `healthy`, `curl http://localhost:5100/health` e
fluxo `auth/token` → `POST /orders` via HTTP (5100), e `curl -k
https://localhost:5101/health` via HTTPS (5101, `-k`/`--insecure` esperado
pois o certificado é autoassinado e gerado do zero a cada build, nunca
confiável pela cadeia do sistema). `dotnet test` revalidado (110/110 verdes)
e `dotnet build` sem novos erros/warnings.

---

## 31. Swagger acessível via `docker compose up` (flag `EnableSwagger`, sem trocar `ASPNETCORE_ENVIRONMENT`)

**Problema**: `http://localhost:5100/swagger` não respondia rodando via
`docker compose up`, porque `Program.cs` só registrava
`UseSwagger()`/`UseSwaggerUI()` dentro de `IsDevelopment()` (seção 22.1) e o
serviço `api` do compose sobe com `ASPNETCORE_ENVIRONMENT=Production`
(seção 27.4).

**Alternativa descartada**: trocar `ASPNETCORE_ENVIRONMENT` para
`Development` no `docker-compose.yml`. Rejeitada porque acopla Swagger a
**todo** o resto do comportamento condicionado a ambiente na aplicação — em
particular `ErrorHandling/GlobalExceptionHandler.cs`, que usa
`!_environment.IsDevelopment()` para decidir se esconde o detalhe de
exceções em respostas 500. Habilitar Swagger não deveria, como efeito
colateral, também passar a vazar stack trace/mensagem de exceção interna em
erros 500 — são duas preocupações independentes que não deveriam estar
amarradas à mesma variável.

A solução foi criar uma flag de configuração dedicada, `EnableSwagger`
(bool), independente de `ASPNETCORE_ENVIRONMENT`:

```csharp
var swaggerEnabled = app.Environment.IsDevelopment()
    || app.Configuration.GetValue<bool>("EnableSwagger");

if (swaggerEnabled)
{
    app.UseSwagger();
    app.UseSwaggerUI();
}
```

- Em `Development` (ex.: `dotnet run` local), Swagger continua habilitado
  automaticamente, sem precisar configurar nada — comportamento da seção
  22.1 preservado.
- `docker-compose.yml` define `EnableSwagger: "true"` no serviço `api`,
  mantendo `ASPNETCORE_ENVIRONMENT: Production` inalterado — nenhum outro
  comportamento de ambiente (incluindo o `GlobalExceptionHandler`) muda.
  Documentado no próprio compose, no mesmo tom dos avisos já existentes
  sobre o HTTPS de avaliação (seção 30): isso é conveniência para
  avaliação/exploração da API, não um padrão de como se exporia Swagger
  numa API em produção real.

**Validação**: `docker compose down -v` seguido de `docker compose up
--build -d` do zero, `docker compose ps` confirmando `api`/`db` `healthy`,
`curl -I http://localhost:5100/swagger/index.html` retornando 200. Revisão
do `GlobalExceptionHandler` confirma que a condição de vazamento de detalhe
de exceção permanece exclusivamente `!_environment.IsDevelopment()`, sem
nenhuma referência a `EnableSwagger` — a flag não tem efeito colateral sobre
tratamento de erro. `docker compose down -v` ao final, sem resíduo.
`dotnet test` (110/110 verdes) e `dotnet build` (sem novos erros/warnings)
revalidados.
