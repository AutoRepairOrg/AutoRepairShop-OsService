# Testando com o Mercado Pago Real

Para testar com o MP real você precisa de 3 coisas: conta sandbox, credenciais e uma URL pública para o webhook.

---

## Passo a passo

### 1. Criar conta sandbox no Mercado Pago

1. Acesse [developers.mercadopago.com](https://developers.mercadopago.com)
2. Faça login com sua conta MP (ou crie uma)
3. Vá em **Suas integrações → Criar aplicação**
4. Em **Credenciais de teste**, copie o **Access Token** (começa com `TEST-...`)

---

### 2. Configurar o BillingService

Em `src/BillingService.Api/appsettings.Development.json`:

```json
"MercadoPago": {
  "UseStub": false,
  "AccessToken": "TEST-XXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXX",
  "NotificationUrl": "https://SEU-NGROK.ngrok-free.app/api/payment/webhook"
}
```

---

### 3. Expor o localhost com ngrok

O MP precisa chamar seu webhook — mas seu PC não tem IP público. Use o **ngrok**:

```powershell
# Instalar (se não tiver)
winget install Ngrok.Ngrok

# Expor a porta do BillingService
ngrok http 5011
```

Copie a URL gerada (ex: `https://abc123.ngrok-free.app`) e coloque no `NotificationUrl` acima.

---

### 4. Fluxo real no Postman

| # | O que muda em relação ao stub |
|---|---|
| Passo 5 — checkout | `checkoutUrl` agora é real. Abra no browser, faça login com uma **conta de teste comprador** MP e pague |
| Passo 6 — webhook | **Não precisa mais chamar manualmente.** O MP chama sozinho após o pagamento |
| Passo 7 — validar | Aguarde ~5-10 s e verifique o status da OS |

**Conta de teste comprador:** no portal do MP → *Contas de teste* → crie uma conta "comprador". Use cartão de teste fornecido pelo próprio MP (ex: Mastercard `5031 4332 1540 6351`, CVV `123`, vencimento `11/25`).

---

## Resumo das diferenças: Stub × MP Real

| | Stub (`UseStub: true`) | MP Real (`UseStub: false`) |
|---|---|---|
| `checkoutUrl` | URL falsa (erro no browser) | URL real do Checkout Pro |
| Webhook | Você chama manualmente | MP chama automaticamente |
| Pagamento | Prefixo `approved:` no body | Fluxo real com cartão |
| `external_reference` | Você define | MP retorna o valor enviado na Preference |
| Credenciais | Não precisa | `TEST-...` do sandbox |

> Para o tech challenge, o stub é suficiente para demonstrar o Saga.
> O MP real seria necessário somente em ambiente de homologação/produção.
