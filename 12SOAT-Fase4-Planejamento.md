# Tech Challenge — Fase 4 · Planejamento

**Curso:** 12SOAT | **Organização GitHub:** [AutoRepairOrg](https://github.com/orgs/AutoRepairOrg/repositories)  
**Objetivo:** Refatoração da oficina mecânica em microsserviços com Saga Pattern, CI/CD e deploy Kubernetes.

---

## Contexto

A aplicação de oficina mecânica atingiu escala nacional e precisa ser refatorada de um monolito para uma **arquitetura de microsserviços distribuídos**, com:

- Microsserviços independentes com banco de dados próprio
- Comunicação via mensageria assíncrona (RabbitMQ / SQS)
- Saga Pattern para coordenação de transações distribuídas
- Pipelines CI/CD independentes com deploy automatizado em Kubernetes
- Cobertura mínima de 80% de testes por serviço

---

## Estrutura Atual dos Repositórios

| Repositório | Linguagem | Papel Atual |
|---|---|---|
| `AutoRepairShop` | C# | Monolito principal / Frontend |
| `AutoRepairShop-Api` | C# | API monolítica |
| `AutoRepairShop-Database` | HCL | Infraestrutura de banco de dados |
| `AutoRepairShop-Kubernetes` | HCL | Orquestração Kubernetes |
| `AutoRepairShop-Lambda` | C# | Funções serverless |

---

## Nova Estrutura de Repositórios

| Repositório | Tecnologia | Papel | Banco de Dados | Situação |
|---|---|---|---|---|
| `AutoRepairShop-OsService` | C# .NET 8 | Microsserviço — Ordem de Serviço | SQL Server | **CRIAR** |
| `AutoRepairShop-BillingService` | C# .NET 8 | Microsserviço — Orçamento & Pagamento | PostgreSQL | **CRIAR** |
| `AutoRepairShop-ExecutionService` | C# .NET 8 | Microsserviço — Execução & Produção | MongoDB | **CRIAR** |
| `AutoRepairShop-Infra` | HCL / Terraform | Infra compartilhada: K8s, RabbitMQ, Prometheus/Grafana | — | **CONSOLIDAR** (Database + Kubernetes) |
| `AutoRepairShop-Gateway` | YARP / Nginx | API Gateway — roteamento e autenticação central | — | **CRIAR** (recomendado) |

> Os repos `AutoRepairShop-Database` e `AutoRepairShop-Kubernetes` serão consolidados em `AutoRepairShop-Infra`.  
> O repo `AutoRepairShop-Lambda` pode ser base para o BillingService (integração Mercado Pago).

---

## Microsserviços

### 1. OS Service — `AutoRepairShop-OsService`

**Banco:** SQL Server (relacional) — consistência transacional forte para OSs.

**Responsabilidades:**
- Abertura da Ordem de Serviço
- Atualização e consulta de status
- Histórico de OSs

**Endpoints REST:**
```
POST   /os
GET    /os/{id}
PATCH  /os/{id}/status
```

**Eventos:**
- Publica: `OsCreated`, `OsCancelled`, `OsClosed`
- Consome: `PaymentConfirmed`, `BudgetRejected`

---

### 2. Billing Service — `AutoRepairShop-BillingService`

**Banco:** PostgreSQL (relacional) — transações financeiras exigem ACID.

**Responsabilidades:**
- Geração e envio de orçamentos para aprovação
- Integração com **Mercado Pago** para pagamentos
- Registro e verificação de pagamentos
- Atualização do status da OS após pagamento

**Endpoints REST:**
```
POST  /budget
GET   /budget/{id}
POST  /payment/webhook
```

**Eventos:**
- Publica: `BudgetGenerated`, `BudgetRejected`, `BudgetApproved`, `PaymentConfirmed`, `PaymentFailed`
- Consome: `OsCreated`, `ExecutionCompleted`

---

### 3. Execution Service — `AutoRepairShop-ExecutionService`

**Banco:** MongoDB (NoSQL) — fila com schema flexível e alta velocidade de escrita.

**Responsabilidades:**
- Gerenciar fila de execução das OSs
- Atualizar status durante diagnóstico e reparos
- Comunicar finalização ao OS Service

**Endpoints REST:**
```
POST  /execution
GET   /execution/{id}
PATCH /execution/{id}/status
```

**Eventos:**
- Publica: `ExecutionStarted`, `ExecutionCompleted`, `ExecutionFailed`
- Consome: `BudgetApproved`

---

## Estratégia de Bancos de Dados

| Serviço | Banco | Tipo | Justificativa |
|---|---|---|---|
| OS Service | SQL Server | Relacional (SQL) | Consistência forte — dados estruturados com relações claras |
| Billing Service | PostgreSQL | Relacional (SQL) | ACID para transações financeiras; suporte nativo a JSON |
| Execution Service | MongoDB | Não-relacional (NoSQL) | Fila de execução com schema flexível e alta escrita |

> **Regra:** Nenhum serviço acessa o banco do outro. Toda comunicação transita via mensageria ou REST.

---

## Saga Pattern — Estratégia Coreografada

### Por que Coreografia?

Com **orquestração**, um serviço central se torna ponto de falha e gargalo.  
Com **coreografia**, cada serviço conhece apenas seu papel e reage a eventos — mais resiliente, desacoplado e alinhado com responsabilidade única.

**Broker escolhido:** RabbitMQ (ou AWS SQS como alternativa)

### Fluxo Completo — Happy Path

| Passo | Ator | Ação | Evento Publicado | Resultado |
|---|---|---|---|---|
| 1 | Cliente | Cria Ordem de Serviço | `OsCreated` | OS aberta |
| 2 | Billing Service | Gera orçamento | `BudgetGenerated` | Aguarda aprovação |
| 3 | Cliente | Aprova orçamento | `BudgetApproved` | Aprovação registrada |
| 4 | Execution Service | Inicia execução | `ExecutionStarted` | Reparo em andamento |
| 5 | Execution Service | Conclui execução | `ExecutionCompleted` | Pronto para cobrança |
| 6 | Billing Service | Gera cobrança via Mercado Pago | `InvoiceGenerated` | Aguarda pagamento |
| 7 | Cliente | Realiza pagamento | `PaymentConfirmed` | Pagamento confirmado |
| 8 | OS Service | Fecha OS | — | OS encerrada |

### Compensações / Rollbacks

| Gatilho de Falha | Ação Compensatória | Serviços Envolvidos |
|---|---|---|
| Cliente rejeita orçamento | `BudgetRejected` → OS Service cancela OS | Billing → OS |
| Pagamento falha | `PaymentFailed` → OS Service marca como pendente | Billing → OS |
| Falha na execução | `ExecutionFailed` → Billing cancela cobrança, OS Service alerta | Execution → Billing / OS |

---

## CI/CD — Pipeline por Microsserviço

Cada microsserviço terá seu próprio pipeline (GitHub Actions), disparado em push e obrigatório em PRs para `main`.

### Etapas do Pipeline

1. Build da aplicação (.NET 8)
2. Testes unitários (xUnit / NUnit)
3. Testes BDD (SpecFlow / Reqnroll)
4. Cobertura mínima 80% (Coverlet)
5. Qualidade de código (SonarQube)
6. Build da imagem Docker
7. Push para registry (ECR / Docker Hub)
8. Deploy em Kubernetes (kubectl / Helm)

### Estrutura de Arquivo por Repositório

```
<repo>/
├── src/
│   └── <Service>/
├── tests/
│   ├── Unit/
│   └── BDD/
├── k8s/
│   ├── deployment.yaml
│   ├── service.yaml
│   ├── configmap.yaml
│   └── hpa.yaml
├── Dockerfile
├── sonar-project.properties
├── .github/
│   └── workflows/
│       └── ci.yml
└── README.md
```

### Proteção de Branch

| Regra | Configuração |
|---|---|
| Branch protegida | `main` em todos os repos |
| PR obrigatório | Nenhum push direto para `main` |
| Status checks obrigatórios | Build + Testes + SonarQube devem passar |
| Aprovação de revisores | Mínimo 1 aprovação (recomendado) |
| Deploy automático | Apenas após merge em `main` com todos os checks verdes |

---

## Checklist de Entregáveis

### Repositórios & Estrutura
- [x] Criar repo `AutoRepairShop-OsService`
- [x] Criar repo `AutoRepairShop-BillingService`
- [ ] Criar repo `AutoRepairShop-ExecutionService`
- [ ] Criar repo `AutoRepairShop-Gateway`

### Código & Microsserviços
- [x] Extrair lógica de OS do monolito → OsService
- [ ] Extrair lógica de billing → BillingService + integração Mercado Pago
- [ ] Criar ExecutionService com fila de execução
- [x] Configurar RabbitMQ / AWS SQS como message broker *(OsService ✓ — parcial; falta BillingService e ExecutionService)*
- [ ] Implementar Saga Coreografada (eventos entre serviços) *(OsService ✓ — publica `OsCreated`/`OsCancelled`, consome `PaymentConfirmed`/`BudgetRejected`; falta BillingService e ExecutionService)*
- [ ] Implementar rollbacks / compensações no Saga *(OsService ✓ — `BudgetRejectedHandler` e `PaymentConfirmedHandler`; falta demais serviços)*

### Testes & Qualidade
- [ ] Testes unitários em todos os 3 microsserviços (≥ 80% de cobertura) *(OsService ✓ — unitários + threshold 80% no CI; BillingService e ExecutionService pendentes)*
- [ ] Ao menos 1 fluxo completo testado com BDD (SpecFlow)
- [x] Configurar SonarQube no CI *(OsService ✓ — parcial; falta demais serviços)*

### Infraestrutura & Deploy
- [ ] `Dockerfile` em cada microsserviço *(OsService ✓)*
- [ ] Manifestos Kubernetes em cada microsserviço (`k8s/`) *(OsService ✓ — Deployment, Service, HPA, ConfigMap)*
- [ ] Pipelines CI/CD independentes (GitHub Actions) *(OsService ✓ — Build, Test, SonarQube, Docker, Deploy K8s)*
- [ ] Proteger branch `main` (PR obrigatório + status checks) *(OsService ✓ — job no pipeline)*

### Documentação & Entrega
- [ ] Swagger / Postman collection por serviço *(OsService — Swagger ✓; Postman pendente)*
- [x] README com arquitetura e justificativa da estratégia Saga *(OsService ✓)*
- [ ] Diagrama geral da arquitetura (microsserviços, bancos e comunicação)
- [ ] Vídeo de demonstração — até 15 min (YouTube ou Vimeo, não listado)
- [ ] PDF final para entrega no portal (nomes, links dos repos, link do vídeo, diagrama, descrição do Saga)

---

## Itens Obrigatórios no Vídeo (até 15 min)

1. Fluxo completo de uma OS passando pelos 3 microsserviços
2. Execução do Saga Pattern e tratamento de falhas (rollback)
3. Deploy automatizado de ao menos 1 microsserviço com validação de testes
4. Monitoramento e rastreamento dos fluxos distribuídos

---

## Referências

- [Documentação Mercado Pago](https://www.mercadopago.com.br/developers/pt/docs)
- [RabbitMQ Docs](https://www.rabbitmq.com/documentation.html)
- [Saga Pattern — Microsoft](https://learn.microsoft.com/en-us/azure/architecture/reference-architectures/saga/saga)
- [SpecFlow / Reqnroll (BDD para .NET)](https://reqnroll.net/)
- [Coverlet (cobertura .NET)](https://github.com/coverlet-coverage/coverlet)
- [SonarQube GitHub Action](https://github.com/SonarSource/sonarqube-scan-action)

---

*Gerado em 06/10/2026 · AutoRepairOrg · 12SOAT Fase 4*

---
Nos projetos iniciais:
- Mandar para a IA o desenho do domain storytelling para revisar o fluxo
- Questionar quanto ao serviço de execução, o que ele fará e como fará
- Integrar com MP