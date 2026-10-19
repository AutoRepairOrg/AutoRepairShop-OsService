# AutoRepairShop — OsService

Microsserviço responsável pelo ciclo de vida das **Ordens de Serviço (OS)**.  
Parte da arquitetura distribuída do **Tech Challenge Fase 4 · 12SOAT**.

## Responsabilidade de cada microsserviço

Cada serviço é um bounded context: dados, regras e eventos próprios. Nenhum “faz a OS inteira”.

| Serviço | Banco | Responsabilidade | Não faz |
|---|---|---|---|
| **OsService** | SQL Server | A OS existe, muda de status e guarda o histórico | Orçamento, pagamento, executar o serviço |
| **BillingService** | PostgreSQL | Orçamento e pagamento (Mercado Pago) | Status operacional da OS, chão de oficina |
| **ExecutionService** | MongoDB (não relacional) | Executar o que foi aprovado: iniciar, acompanhar e concluir | Cobrar, entregar o carro, cadastrar cliente |

Regra prática:

- “Em que status está a OS?” → **OsService**
- “Quanto custa / pagou?” → **BillingService**
- “O serviço já foi feito?” → **ExecutionService**

O ExecutionService **é uma API**, no mesmo formato dos outros dois (HTTP + eventos). Não é só worker e não é infraestrutura. O Swagger dele é pequeno: quem dispara “serviço concluído” é o técnico.

### Endpoints previstos do ExecutionService

O início da execução **não** tem `POST`. A execução é criada ao consumir `OsExecutionStartedEvent`.

| Método | Rota | O que faz |
|---|---|---|
| `GET` | `/api/execution/{id}` | Busca pelo id |
| `GET` | `/api/execution/os/{osId}` | Busca a execução da OS |
| `GET` | `/api/execution?status=InProgress` | Fila em andamento |
| `PATCH` | `/api/execution/os/{osId}/complete` | Conclui o serviço e publica `ExecutionCompletedEvent` |
| `GET` | `/health` | Health check |

Opcional: `PATCH .../notes` (observação do técnico) e `PATCH .../cancel` (override; o cancelamento normal vem de `OsCancelledEvent`).

---

## Quando a OS vai para o BillingService

**Só depois do diagnóstico e da inclusão de itens.**

O Billing não recebe a OS na abertura. O admin primeiro registra o que foi identificado na própria OS; só então o OsService publica o payload para o Billing montar o orçamento.

```
1. POST /api/serviceorder              → OS Received
2. PATCH .../start-diagnosis           → InDiagnosis
3. POST  .../items                     → itens só na OS
4. PATCH .../send-for-approval         → WaitingApproval
     └── OsSentForApprovalEvent (OsId + itens)
           └── BillingService popula o Budget
```

Não use `POST /api/Budget/{id}/items` no fluxo normal. Os itens nascem na OS e atravessam o evento.

`send-for-approval` exige ao menos um item. Sem diagnóstico completo, a OS não segue para o Billing.

---

## Arquitetura deste repositório

```
src/
├── OsService.Api            → Controllers REST, Swagger, Program.cs
├── OsService.Application    → Application Services, DTOs, Event Handlers (Saga consumers)
├── OsService.Domain         → Entidades, Enums, Domain Events, Interfaces
└── OsService.Infrastructure → EF Core (SQL Server), RabbitMQ Publisher, Repository
tests/
└── OsService.Tests
k8s/                         → Manifestos Kubernetes
.github/workflows/ci.yml     → Pipeline CI/CD
```

## Banco de dados (OsService)

**SQL Server 2022** — banco próprio e isolado. Nenhum outro serviço acessa este banco diretamente.

Polyglot persistence do sistema:

- OsService → SQL Server
- BillingService → PostgreSQL
- ExecutionService → **MongoDB** (documento simples: OsId, Status, StartedAt, FinishedAt, Notes)

## Fluxo de status da OS

```
Received → InDiagnosis → WaitingApproval → InExecution → Finished → Delivered
                                                                          ↑
                                                               (qualquer estado, menos Delivered) → Canceled
```

## Saga coreografada

### Eventos publicados (exchange: `os-service-events`)

| Evento | Quando | Consumidor |
|---|---|---|
| `OsSentForApprovalEvent` | Diagnóstico ok + itens + send-for-approval | BillingService (cria/popula o Budget) |
| `OsExecutionStartedEvent` | Após aprovação do orçamento (`StartExecution`) | ExecutionService |
| `OsCancelledEvent` | OS cancelada | BillingService (compensa Budget/Payment) |
| `OsClosedEvent` | OS entregue (`Deliver`) | — |

`OsCreatedEvent` continua existindo no domínio, mas **não é o envio da OS ao Billing**. O envio comercial é o `OsSentForApprovalEvent`.

### Eventos consumidos

| Evento | Origem | Ação |
|---|---|---|
| `BudgetApprovedEvent` | BillingService | `StartExecution()` + publica `OsExecutionStartedEvent` |
| `BudgetRejectedEvent` | BillingService | `Cancel()` (compensação) |
| `PaymentConfirmedEvent` | BillingService | Hoje: `Finish()` + `Deliver()`. Com ExecutionService: só `Deliver()` |

## Endpoints REST (OsService)

| Método | Rota | Descrição |
|---|---|---|
| `POST` | `/api/serviceorder` | Cria nova OS |
| `GET` | `/api/serviceorder/{id}` | Busca OS por ID |
| `GET` | `/api/serviceorder?status=X` | Lista por status |
| `POST` | `/api/serviceorder/{id}/items` | Adiciona item à OS (durante o diagnóstico) |
| `PATCH` | `/api/serviceorder/{id}/start-diagnosis` | Inicia diagnóstico |
| `PATCH` | `/api/serviceorder/{id}/send-for-approval` | Envia OS ao Billing (itens no evento) |
| `PATCH` | `/api/serviceorder/{id}/start-execution` | Override; no Saga vem de `BudgetApprovedEvent` |
| `PATCH` | `/api/serviceorder/{id}/finish` | Override; no Saga virá de `ExecutionCompletedEvent` |
| `PATCH` | `/api/serviceorder/{id}/deliver` | Override; no Saga vem de `PaymentConfirmedEvent` |
| `PATCH` | `/api/serviceorder/{id}/cancel` | Cancela OS |
| `GET` | `/health` | Health check |

Swagger local (`dotnet run --launch-profile http`): `http://localhost:5274/swagger`  
Swagger Docker: `http://localhost:5010/swagger`

Roteiro de teste dos dois serviços (Swagger): `C:\fase4\roteiro-teste-swagger.md`

## Próximos passos

1. **Alinhar o Billing ao envio só após diagnóstico**  
   Budget deve nascer/popular em `OsSentForApprovalEvent`, não na criação vazia da OS. Remover dependência de `OsCreatedEvent` como “chegou uma OS”.

2. **Criar `AutoRepairShop-ExecutionService`**  
   Mesma estrutura (Clean Architecture + DDD), API pequena (tabela acima), MongoDB, RabbitMQ (`os-service-events` / `execution-service-events`).

3. **Fechar o Saga da oficina**  
   ExecutionService consome `OsExecutionStartedEvent` e publica `ExecutionCompletedEvent`.  
   Billing já tem handler/binding de checkout.  
   OsService: `PaymentConfirmedHandler` passa a chamar só `Deliver()`; `Finish()` na conclusão da execução.

4. **Cloud / CI-CD (obrigatório na entrega)**  
   Pipelines já têm Docker + kubectl. Faltam cluster, RabbitMQ, bancos (SQL Server, PostgreSQL, MongoDB) e secrets (`KUBECONFIG`, `DOCKERHUB_*`, `GH_PAT`).

5. **Demonstração**  
   LocalStack/local ok para desenvolver. Na gravação do TC, rodar em cloud.

## Executando com Docker

```bash
cp .env.example .env
docker compose up -d
```

## Testes

```bash
dotnet test
dotnet test --collect:"XPlat Code Coverage"
```

Cobertura mínima: **80%**.

## Tech Stack

- .NET 8 / ASP.NET Core
- Entity Framework Core + SQL Server 2022
- RabbitMQ (Saga coreografado)
- xUnit + FluentAssertions + Moq
- Docker + Kubernetes
- GitHub Actions + SonarQube

## Autores

Projeto desenvolvido por **Dhiulia da Silva** e **Mateus Pinheiro** · 12SOAT Fase 4
