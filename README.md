# AutoRepairShop — OsService

Microsserviço responsável pelo ciclo de vida das **Ordens de Serviço (OS)**.  
Parte da arquitetura distribuída do **Tech Challenge Fase 4 · 12SOAT**.

## Arquitetura

```
src/
├── OsService.Api            → Controllers REST, Swagger, Program.cs
├── OsService.Application    → Application Services, DTOs, Event Handlers (Saga consumers)
├── OsService.Domain         → Entidades, Enums, Domain Events, Interfaces
└── OsService.Infrastructure → EF Core (SQL Server), RabbitMQ Publisher, Repository
tests/
└── OsService.Tests
    ├── Unit/                → Testes unitários do domínio (xUnit + FluentAssertions)
    └── BDD/                 → Testes de comportamento (SpecFlow/Reqnroll)
k8s/                         → Manifestos Kubernetes (Deployment, Service, HPA, ConfigMap)
.github/workflows/ci.yml     → Pipeline CI/CD (Build → Testes → SonarQube → Docker → K8s)
```

## Banco de dados

**SQL Server 2022** — banco próprio e isolado. Nenhum outro serviço acessa este banco diretamente.

## Fluxo de Status da OS

```
Received → InDiagnosis → WaitingApproval → InExecution → Finished → Delivered
                                                                          ↑
                                                               (qualquer estado) → Canceled
```

## Saga Pattern — Coreografado

### Eventos publicados (RabbitMQ · exchange: `os-service-events`)

| Evento | Quando | Consumidor |
|---|---|---|
| `OsSentForApprovalEvent` | Diagnóstico ok + itens + send-for-approval | BillingService (cria e popula o Budget) |
| `OsExecutionStartedEvent` | Após aprovação do orçamento | ExecutionService |
| `OsCancelledEvent` | OS cancelada | BillingService, ExecutionService |
| `OsClosedEvent` | OS entregue | — |

`OsCreatedEvent` continua no domínio, mas não envia a OS ao Billing.

### Eventos consumidos

| Evento | Origem | Ação |
|---|---|---|
| `BudgetApproved` | BillingService | Inicia a execução e publica `OsExecutionStartedEvent` |
| `ExecutionCompleted` | ExecutionService | Finaliza a OS (`Finish`) |
| `PaymentConfirmed` | BillingService | Entrega a OS (`Deliver`) |
| `BudgetRejected` | BillingService | Cancela a OS (compensação) |

## Endpoints REST

| Método | Rota | Descrição |
|---|---|---|
| `POST` | `/api/serviceorder` | Cria nova OS |
| `GET` | `/api/serviceorder/{id}` | Busca OS por ID |
| `GET` | `/api/serviceorder?status=X` | Lista por status |
| `POST` | `/api/serviceorder/{id}/items` | Adiciona item à OS |
| `PATCH` | `/api/serviceorder/{id}/start-diagnosis` | Inicia diagnóstico |
| `PATCH` | `/api/serviceorder/{id}/send-for-approval` | Envia para aprovação |
| `PATCH` | `/api/serviceorder/{id}/start-execution` | Inicia execução |
| `PATCH` | `/api/serviceorder/{id}/finish` | Finaliza serviço |
| `PATCH` | `/api/serviceorder/{id}/cancel` | Cancela OS |
| `GET` | `/health` | Health check |

Swagger disponível em: `http://localhost:5010/swagger`

## Executando com Docker

```bash
# Copie e configure as variáveis
cp .env.example .env

# Suba tudo (API + SQL Server + RabbitMQ)
docker compose up -d

# Migrations (aplicadas automaticamente no startup)
# Para rodar manualmente:
dotnet ef database update --project src/OsService.Infrastructure --startup-project src/OsService.Api
```

## Testes

```bash
# Rodar todos os testes
dotnet test

# Com cobertura
dotnet test --collect:"XPlat Code Coverage"
```

Cobertura mínima exigida: **80%**

## Tech Stack

- .NET 8 / ASP.NET Core
- Entity Framework Core + SQL Server 2022
- RabbitMQ (mensageria — Saga coreografado)
- xUnit + FluentAssertions + Moq (testes unitários)
- SpecFlow/Reqnroll (BDD)
- Docker + Kubernetes (Minikube / produção)
- GitHub Actions (CI/CD)
- SonarQube (qualidade de código)

## Autores

Projeto desenvolvido por **Dhiulia da Silva** e **Mateus Pinheiro** · 12SOAT Fase 4
