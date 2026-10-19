# Roteiro de teste via Swagger — OsService + BillingService

Com os dois serviços dá para testar quase o Saga inteiro. O que falta é só a execução real: depois da aprovação, a OS já entra em `InExecution` sozinha — o checkout e o restante você dispara na mão.

**Não chame** `PATCH /finish` antes do pagamento. O `PaymentConfirmedHandler` ainda faz `Finish()` + `Deliver()`. Se a OS já estiver `Finished`, o webhook quebra.

- **OsService:** http://localhost:5274/swagger
- **BillingService:** http://localhost:5011/swagger

Antes: RabbitMQ + Postgres + LocalDB ligados, e as duas APIs rodando em Development (`Messaging:UseStub = false`).

Guarde duas coisas no bloco de notas: `osId` e `budgetId`.

---

## Fluxo feliz

### 1. Criar a OS

**OsService** → `POST /api/ServiceOrder` → **Try it out**

```json
{
  "customerName": "Maria Silva",
  "customerPhone": "11988887777",
  "vehiclePlate": "ABC1D23",
  "vehicleModel": "Honda Civic",
  "description": "Barulho no motor"
}
```

**Execute.** Copie o `id` da resposta (`osId`). Status deve ser `Received`.

Espere ~2 segundos (evento ir para o Billing).

---

### 2. Conferir o Budget Draft

**BillingService** → `GET /api/Budget/os/{osId}`

Cole o `osId` → **Execute**.

Esperado: `status = Draft`, `items = []`, `total = 0`. Copie o `id` do budget (`budgetId`).

Se der 404, o `OsCreatedEvent` não chegou. Reinicie o BillingService com RabbitMQ no ar.

---

### 3. Iniciar diagnóstico

**OsService** → `PATCH /api/ServiceOrder/{id}/start-diagnosis`

Cole o `osId` → **Execute**. Status: `InDiagnosis`.

---

### 4. Adicionar itens **só na OS** (não no Budget)

**OsService** → `POST /api/ServiceOrder/{id}/items`

Item 1:

```json
{
  "serviceName": "Troca de oleo",
  "price": 150,
  "quantity": 1
}
```

Item 2 (mesmo endpoint, de novo):

```json
{
  "serviceName": "Filtro de ar",
  "price": 80,
  "quantity": 2
}
```

Esperado: `total = 310`, dois itens. **Não use** `POST /api/Budget/{id}/items`.

---

### 5. Enviar OS para aprovação

**OsService** → `PATCH /api/ServiceOrder/{id}/send-for-approval`

Status da OS: `WaitingApproval`.

Espere ~2 segundos. Volte no Billing → `GET /api/Budget/os/{osId}`.

Esperado: Budget ainda `Draft`, mas agora com os 2 itens e `total = 310`.

---

### 6. Enviar o orçamento ao cliente

**BillingService** → `POST /api/Budget/{id}/send`

Cole o `budgetId`. Status: `Sent`.

---

### 7. Cliente aprova

**BillingService** → `POST /api/Budget/{id}/approve`

Espere ~2 segundos. Volte no OsService → `GET /api/ServiceOrder/{id}`.

Esperado: `status = InExecution` (o `BudgetApprovedHandler` já chamou `StartExecution`).

---

### 8. Checkout (substitui o ExecutionService)

Ainda não existe `ExecutionCompletedEvent`, então o checkout é manual.

**BillingService** → `POST /api/Payment/checkout/{osId}`

Copie o `paymentId` da resposta. A `checkoutUrl` é stub — não precisa abrir no browser.

---

### 9. Simular pagamento aprovado (webhook stub)

**BillingService** → `POST /api/Payment/webhook`

- `topic`: `payment`
- `id`: `approved:{paymentId}`  
  Exemplo: se o paymentId for `aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee`, o `id` fica  
  `approved:aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee`
- Body: deixe vazio ou `{}`

**Execute.** Resposta: `"processed": true`.

Espere ~2 segundos.

---

### 10. Conferir o fim

**BillingService** → `GET /api/Payment/os/{osId}`  
Esperado: `status = Confirmed`

**OsService** → `GET /api/ServiceOrder/{id}`  
Esperado: `status = Delivered`  
No `history`: Received → InDiagnosis → WaitingApproval → InExecution → Finished → Delivered

**Não chame** `PATCH /finish` nem `PATCH /start-execution` neste fluxo. O approve já coloca em `InExecution`, e o webhook já faz `Finish` + `Deliver`.

---

## Fluxo de rejeição (outra OS)

Repita os passos 1 a 6 com uma OS nova.

No lugar do approve: **BillingService** → `POST /api/Budget/{id}/reject`

```json
{
  "reason": "Valor muito alto"
}
```

Espere ~2 s. `GET` da OS deve vir `Canceled`.

---

## Ordem dos endpoints no Swagger

| # | API | Endpoint |
|---|---|---|
| 1 | Os | `POST /api/ServiceOrder` |
| 2 | Billing | `GET /api/Budget/os/{osId}` |
| 3 | Os | `PATCH .../start-diagnosis` |
| 4 | Os | `POST .../items` (2x) |
| 5 | Os | `PATCH .../send-for-approval` |
| 6 | Billing | `GET /api/Budget/os/{osId}` (itens preenchidos) |
| 7 | Billing | `POST /api/Budget/{id}/send` |
| 8 | Billing | `POST /api/Budget/{id}/approve` |
| 9 | Os | `GET /api/ServiceOrder/{id}` → `InExecution` |
| 10 | Billing | `POST /api/Payment/checkout/{osId}` |
| 11 | Billing | `POST /api/Payment/webhook` (`id=approved:{paymentId}`) |
| 12 | Os | `GET /api/ServiceOrder/{id}` → `Delivered` |
