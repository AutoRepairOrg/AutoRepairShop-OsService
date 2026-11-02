# Deploy AWS Academy — EKS (Os + Billing + Execution)

Passo a passo para subir os **três microsserviços**, os **três bancos** e o **RabbitMQ** no **EKS** do AWS Academy Learner Lab, testar o Saga e apagar o cluster.

> Este guia é para o **lab**, não para produção. Bancos e broker ficam **dentro do cluster** (mais barato e sem IAM de RDS/DocumentDB/Amazon MQ). Imagens vão para o **Docker Hub** (já usado pelo CI).

---

## 1. Objetivo e o que sobe

| Componente | Papel | Onde |
|---|---|---|
| **OsService** | Ciclo de vida da OS | Deployment + LoadBalancer |
| **BillingService** | Orçamento + pagamento (stub MP) | Deployment + LoadBalancer |
| **ExecutionService** | Execução (quando o repo existir) | Deployment + LoadBalancer |
| **SQL Server 2022** | Banco do OsService | Deployment + PVC + ClusterIP |
| **PostgreSQL 16** | Banco do BillingService | Deployment + PVC + ClusterIP |
| **MongoDB 7** | Banco do ExecutionService | Deployment + PVC + ClusterIP |
| **RabbitMQ 3** | Mensageria do Saga | Deployment + ClusterIP |

```
Browser (Swagger)
   │
   ├─► ELB OsService ──► os-service ──► sqlserver
   │                         │
   ├─► ELB Billing ──► billing-service ──► postgres
   │                         │
   └─► ELB Execution ──► execution-service ──► mongodb   (opcional)
                             │
                        rabbitmq (shared)
```

### Limites do Learner Lab

- Credencial **temporária** (Access Key + Secret + Session Token). Expira com a sessão (~4 h).
- Crédito limitado — **apague o cluster** ao terminar (seção 12).
- Região usual: **`us-east-1`**.
- No lab: **1 réplica** por API (os manifests pedem 2 + HPA 10 — pesado demais).
- Mercado Pago em **stub** (`MercadoPago__UseStub=true`): a URL do LoadBalancer muda a cada sessão.

### ExecutionService

O repo `AutoRepairShop-ExecutionService` ainda pode não existir. Este guia:

1. Sobe o **MongoDB** já.
2. Deixa o deploy da API marcado como **quando o repo existir**.
3. Com Os + Billing sozinhos você já testa quase o Saga todo (checkout manual), como no [roteiro-teste-swagger.md](../roteiro-teste-swagger.md).

---

## 2. Pré-requisitos locais

Instale e confira:

| Ferramenta | Conferência |
|---|---|
| AWS CLI v2 | `aws --version` |
| kubectl | `kubectl version --client` |
| eksctl | `eksctl version` |
| Docker Desktop | `docker version` |
| Conta Docker Hub | login ok |

Windows (PowerShell como Admin, Chocolatey):

```powershell
choco install awscli kubernetesctl kubernetesctl docker-desktop -y
```

macOS (Homebrew):

```bash
brew install awscli kubectl eksctl
brew install --cask docker
```

Faça login no Docker Hub:

```bash
docker login
```

Guarde o usuário do Docker Hub; nos comandos abaixo use `SEU_DOCKERHUB` no lugar.

---

## 3. Abrir o lab e configurar credenciais

1. Entre em [AWS Academy](https://awsacademy.instructure.com/) → **Learner Lab** → **Start Lab**.
2. Espere o status ficar verde.
3. Abra **AWS Details** e copie:
   - AWS Access Key ID  
   - AWS Secret Access Key  
   - AWS Session Token  
4. No terminal local:

**PowerShell:**

```powershell
$env:AWS_ACCESS_KEY_ID     = "COLE_AQUI"
$env:AWS_SECRET_ACCESS_KEY = "COLE_AQUI"
$env:AWS_SESSION_TOKEN      = "COLE_AQUI"
$env:AWS_DEFAULT_REGION     = "us-east-1"
```

**bash:**

```bash
export AWS_ACCESS_KEY_ID="COLE_AQUI"
export AWS_SECRET_ACCESS_KEY="COLE_AQUI"
export AWS_SESSION_TOKEN="COLE_AQUI"
export AWS_DEFAULT_REGION="us-east-1"
```

5. Confirme a identidade:

```bash
aws sts get-caller-identity
```

Deve retornar `Account`, `Arn` e `UserId` do lab. Se der `ExpiredToken`, reinicie o lab e cole as chaves de novo.

---

## 4. Criar o cluster EKS

~15–20 minutos. **Não feche o terminal.**

```bash
eksctl create cluster \
  --name autorepair-eks \
  --region us-east-1 \
  --version 1.29 \
  --nodegroup-name ng-apps \
  --node-type t3.large \
  --nodes 2 \
  --nodes-min 2 \
  --nodes-max 2 \
  --managed
```

Quando terminar:

```bash
kubectl get nodes
aws eks update-kubeconfig --region us-east-1 --name autorepair-eks
kubectl get ns
```

Os nodes devem estar `Ready`.

---

## 5. Infra compartilhada (namespace, bancos, RabbitMQ)

Salve o bloco abaixo como `infra-lab.yaml` (ou cole direto no `kubectl apply -f -`).

```yaml
apiVersion: v1
kind: Namespace
metadata:
  name: autorepair
---
# ── SQL Server (OsService) ────────────────────────────────────────────────────
apiVersion: v1
kind: PersistentVolumeClaim
metadata:
  name: sqlserver-pvc
  namespace: autorepair
spec:
  accessModes: ["ReadWriteOnce"]
  resources:
    requests:
      storage: 10Gi
---
apiVersion: apps/v1
kind: Deployment
metadata:
  name: sqlserver
  namespace: autorepair
spec:
  replicas: 1
  selector:
    matchLabels:
      app: sqlserver
  template:
    metadata:
      labels:
        app: sqlserver
    spec:
      containers:
        - name: sqlserver
          image: mcr.microsoft.com/mssql/server:2022-latest
          ports:
            - containerPort: 1433
          env:
            - name: ACCEPT_EULA
              value: "Y"
            - name: MSSQL_SA_PASSWORD
              value: "YourStrong@Passw0rd"
          resources:
            requests:
              memory: "1Gi"
              cpu: "250m"
            limits:
              memory: "2Gi"
              cpu: "1000m"
          volumeMounts:
            - name: data
              mountPath: /var/opt/mssql
      volumes:
        - name: data
          persistentVolumeClaim:
            claimName: sqlserver-pvc
---
apiVersion: v1
kind: Service
metadata:
  name: sqlserver
  namespace: autorepair
spec:
  selector:
    app: sqlserver
  ports:
    - port: 1433
      targetPort: 1433
  type: ClusterIP
---
# ── PostgreSQL (BillingService) ───────────────────────────────────────────────
apiVersion: v1
kind: PersistentVolumeClaim
metadata:
  name: postgres-pvc
  namespace: autorepair
spec:
  accessModes: ["ReadWriteOnce"]
  resources:
    requests:
      storage: 5Gi
---
apiVersion: apps/v1
kind: Deployment
metadata:
  name: postgres
  namespace: autorepair
spec:
  replicas: 1
  selector:
    matchLabels:
      app: postgres
  template:
    metadata:
      labels:
        app: postgres
    spec:
      containers:
        - name: postgres
          image: postgres:16-alpine
          ports:
            - containerPort: 5432
          env:
            - name: POSTGRES_DB
              value: BillingServiceDb
            - name: POSTGRES_USER
              value: billing
            - name: POSTGRES_PASSWORD
              value: "Billing123!"
          resources:
            requests:
              memory: "256Mi"
              cpu: "100m"
            limits:
              memory: "512Mi"
              cpu: "500m"
          volumeMounts:
            - name: data
              mountPath: /var/lib/postgresql/data
      volumes:
        - name: data
          persistentVolumeClaim:
            claimName: postgres-pvc
---
apiVersion: v1
kind: Service
metadata:
  name: postgres
  namespace: autorepair
spec:
  selector:
    app: postgres
  ports:
    - port: 5432
      targetPort: 5432
  type: ClusterIP
---
# ── MongoDB (ExecutionService) ────────────────────────────────────────────────
apiVersion: v1
kind: PersistentVolumeClaim
metadata:
  name: mongodb-pvc
  namespace: autorepair
spec:
  accessModes: ["ReadWriteOnce"]
  resources:
    requests:
      storage: 5Gi
---
apiVersion: apps/v1
kind: Deployment
metadata:
  name: mongodb
  namespace: autorepair
spec:
  replicas: 1
  selector:
    matchLabels:
      app: mongodb
  template:
    metadata:
      labels:
        app: mongodb
    spec:
      containers:
        - name: mongodb
          image: mongo:7
          ports:
            - containerPort: 27017
          resources:
            requests:
              memory: "256Mi"
              cpu: "100m"
            limits:
              memory: "512Mi"
              cpu: "500m"
          volumeMounts:
            - name: data
              mountPath: /data/db
      volumes:
        - name: data
          persistentVolumeClaim:
            claimName: mongodb-pvc
---
apiVersion: v1
kind: Service
metadata:
  name: mongodb
  namespace: autorepair
spec:
  selector:
    app: mongodb
  ports:
    - port: 27017
      targetPort: 27017
  type: ClusterIP
---
# ── RabbitMQ (Saga) ───────────────────────────────────────────────────────────
apiVersion: apps/v1
kind: Deployment
metadata:
  name: rabbitmq
  namespace: autorepair
spec:
  replicas: 1
  selector:
    matchLabels:
      app: rabbitmq
  template:
    metadata:
      labels:
        app: rabbitmq
    spec:
      containers:
        - name: rabbitmq
          image: rabbitmq:3-management
          ports:
            - containerPort: 5672
            - containerPort: 15672
          resources:
            requests:
              memory: "256Mi"
              cpu: "100m"
            limits:
              memory: "512Mi"
              cpu: "500m"
---
apiVersion: v1
kind: Service
metadata:
  name: rabbitmq-service
  namespace: autorepair
spec:
  selector:
    app: rabbitmq
  ports:
    - name: amqp
      port: 5672
      targetPort: 5672
    - name: management
      port: 15672
      targetPort: 15672
  type: ClusterIP
```

Aplique e espere ficar Ready:

```bash
kubectl apply -f infra-lab.yaml

kubectl wait --for=condition=available deployment/sqlserver -n autorepair --timeout=300s
kubectl wait --for=condition=available deployment/postgres  -n autorepair --timeout=180s
kubectl wait --for=condition=available deployment/mongodb   -n autorepair --timeout=180s
kubectl wait --for=condition=available deployment/rabbitmq  -n autorepair --timeout=180s

kubectl get pods -n autorepair
```

DNS internos (use nas connection strings):

| Serviço | Host no cluster |
|---|---|
| SQL Server | `sqlserver` |
| Postgres | `postgres` |
| MongoDB | `mongodb` |
| RabbitMQ | `rabbitmq-service` |

> O ConfigMap dos manifests já aponta `rabbitmq-host: rabbitmq-service`.

---

## 6. Secrets das APIs

```bash
kubectl create secret generic os-service-secrets \
  --namespace autorepair \
  --from-literal=db-connection-string='Server=sqlserver,1433;Database=OsServiceDb;User Id=sa;Password=YourStrong@Passw0rd;TrustServerCertificate=True'

kubectl create secret generic billing-service-secrets \
  --namespace autorepair \
  --from-literal=db-connection-string='Host=postgres;Port=5432;Database=BillingServiceDb;Username=billing;Password=Billing123!' \
  --from-literal=mp-access-token='TEST-STUB-TOKEN'
```

Quando o ExecutionService existir:

```bash
kubectl create secret generic execution-service-secrets \
  --namespace autorepair \
  --from-literal=mongo-connection-string='mongodb://mongodb:27017'
```

Conferir:

```bash
kubectl get secrets -n autorepair
```

---

## 7. Build e push das imagens

Troque `SEU_DOCKERHUB` pelo seu usuário.

### OsService

```bash
cd /caminho/para/AutoRepairShop-OsService

docker build -t SEU_DOCKERHUB/os-service:latest .
docker push SEU_DOCKERHUB/os-service:latest
```

### BillingService

```bash
cd /caminho/para/AutoRepairShop-BillingService

docker build -t SEU_DOCKERHUB/billing-service:latest .
docker push SEU_DOCKERHUB/billing-service:latest
```

### ExecutionService (quando o repo existir)

```bash
cd /caminho/para/AutoRepairShop-ExecutionService

docker build -t SEU_DOCKERHUB/execution-service:latest .
docker push SEU_DOCKERHUB/execution-service:latest
```

---

## 8. Deploy das APIs (lab patches)

Os manifests em `k8s/deployment.yaml` usam placeholder `DOCKERHUB_USERNAME`, 2 réplicas e Service `ClusterIP`. No lab fazemos apply + patch.

### 8.1 OsService

```bash
cd /caminho/para/AutoRepairShop-OsService

kubectl apply -f k8s/deployment.yaml

kubectl set image deployment/os-service \
  os-service=SEU_DOCKERHUB/os-service:latest \
  -n autorepair

kubectl scale deployment/os-service --replicas=1 -n autorepair

# Stub OFF — senão a API não fala com RabbitMQ (default appsettings = true)
kubectl set env deployment/os-service \
  Messaging__UseStub=false \
  RabbitMQ__User=guest \
  RabbitMQ__Password=guest \
  -n autorepair

# Expor para o browser
kubectl patch svc os-service -n autorepair \
  -p '{"spec":{"type":"LoadBalancer"}}'
```

### 8.2 BillingService

```bash
cd /caminho/para/AutoRepairShop-BillingService

kubectl apply -f k8s/deployment.yaml

kubectl set image deployment/billing-service \
  billing-service=SEU_DOCKERHUB/billing-service:latest \
  -n autorepair

kubectl scale deployment/billing-service --replicas=1 -n autorepair

kubectl set env deployment/billing-service \
  Messaging__UseStub=false \
  MercadoPago__UseStub=true \
  RabbitMQ__User=guest \
  RabbitMQ__Password=guest \
  -n autorepair

kubectl patch svc billing-service -n autorepair \
  -p '{"spec":{"type":"LoadBalancer"}}'
```

### 8.3 ExecutionService (quando existir)

```bash
cd /caminho/para/AutoRepairShop-ExecutionService

kubectl apply -f k8s/deployment.yaml

kubectl set image deployment/execution-service \
  execution-service=SEU_DOCKERHUB/execution-service:latest \
  -n autorepair

kubectl scale deployment/execution-service --replicas=1 -n autorepair

kubectl set env deployment/execution-service \
  Messaging__UseStub=false \
  MongoDB__ConnectionString=mongodb://mongodb:27017 \
  RabbitMQ__Host=rabbitmq-service \
  RabbitMQ__User=guest \
  RabbitMQ__Password=guest \
  -n autorepair

kubectl patch svc execution-service -n autorepair \
  -p '{"spec":{"type":"LoadBalancer"}}'
```

Espere os pods:

```bash
kubectl get pods -n autorepair -w
# Ctrl+C quando os-service e billing-service estiverem Running / Ready
```

Se algum pod ficar em `CrashLoopBackOff`:

```bash
kubectl logs -n autorepair deploy/os-service --tail=80
kubectl logs -n autorepair deploy/billing-service --tail=80
kubectl describe pod -n autorepair -l app=os-service
```

Causas comuns: secret errado, SQL ainda subindo, `Messaging__UseStub` ainda `true`.

---

## 9. Pegar URLs e validar health

```bash
kubectl get svc -n autorepair
```

Anote o `EXTERNAL-IP` (ELB) de `os-service` e `billing-service`. Pode levar 1–3 minutos até o hostname aparecer.

**PowerShell:**

```powershell
$OS   = "http://$(kubectl get svc os-service -n autorepair -o jsonpath='{.status.loadBalancer.ingress[0].hostname}')"
$BILL = "http://$(kubectl get svc billing-service -n autorepair -o jsonpath='{.status.loadBalancer.ingress[0].hostname}')"

Write-Host "OsService:  $OS"
Write-Host "Billing:    $BILL"
Write-Host "Swagger OS: $OS/swagger"
Write-Host "Swagger Bill: $BILL/swagger"
```

**bash:**

```bash
OS=$(kubectl get svc os-service -n autorepair -o jsonpath='{.status.loadBalancer.ingress[0].hostname}')
BILL=$(kubectl get svc billing-service -n autorepair -o jsonpath='{.status.loadBalancer.ingress[0].hostname}')

echo "OsService:  http://$OS"
echo "Billing:    http://$BILL"
echo "Swagger OS: http://$OS/swagger"
echo "Swagger Bill: http://$BILL/swagger"
```

Health:

```bash
curl -s "http://$OS/health"
curl -s "http://$BILL/health"
```

Abra no browser:

- `http://<ELB-OS>/swagger`
- `http://<ELB-BILL>/swagger`

---

## 10. Testar o Saga na nuvem

Use o mesmo fluxo de [roteiro-teste-swagger.md](../roteiro-teste-swagger.md), trocando `localhost` pelos ELBs.

### Ordem resumida

| # | API | Ação | Esperado |
|---|---|---|---|
| 1 | Os | `POST /api/ServiceOrder` | `Received` — guarde `osId` |
| 2 | Billing | `GET /api/Budget/os/{osId}` | Budget `Draft` (após ~2 s) |
| 3 | Os | `PATCH .../start-diagnosis` | `InDiagnosis` |
| 4 | Os | `POST .../items` (1+) | itens na OS |
| 5 | Os | `PATCH .../send-for-approval` | `WaitingApproval` + itens no Budget |
| 6 | Billing | `POST /api/Budget/{id}/send` | `Sent` |
| 7 | Billing | `POST /api/Budget/{id}/approve` | OS → `InExecution` |
| 8 | Os | `PATCH .../finish` *(sem ExecutionService)* | `Finished` |
| 9 | Billing | `POST /api/Payment/checkout/{osId}` | `checkoutUrl` stub |
| 10 | Billing | `POST /api/Payment/webhook` `id=approved:{paymentId}` | Payment `Confirmed` |
| 11 | Os | `GET /api/ServiceOrder/{id}` | `Delivered` |

> **Atenção:** se o `PaymentConfirmedHandler` do OsService ainda fizer `Finish() + Deliver()`, **não** chame `PATCH /finish` no passo 8 — o webhook quebra se a OS já estiver `Finished`. Siga o aviso do roteiro local.

### Compensação rápida (opcional)

Em outra OS: após `send`, chame `POST /api/Budget/{id}/reject` com `{"reason":"teste lab"}`. A OS deve ir para `Canceled`.

### Conferir RabbitMQ

```bash
kubectl port-forward -n autorepair svc/rabbitmq-service 15672:15672
```

Abra http://localhost:15672 (`guest` / `guest`). Em **Exchanges** devem aparecer `os-service-events` e `billing-service-events`.

---

## 11. (Opcional) Ligar o CI ao cluster

O job `deploy` do [`.github/workflows/ci.yml`](../.github/workflows/ci.yml) espera o secret `KUBECONFIG` em base64.

```bash
# Linux / macOS / Git Bash
cat ~/.kube/config | base64 -w 0 > kubeconfig.b64

# PowerShell
[Convert]::ToBase64String([IO.File]::ReadAllBytes("$env:USERPROFILE\.kube\config")) | Out-File kubeconfig.b64
```

No GitHub → Settings → Secrets de **cada** repo (`OsService`, `BillingService`):

| Secret | Valor |
|---|---|
| `KUBECONFIG` | conteúdo de `kubeconfig.b64` |
| `DOCKERHUB_USERNAME` | seu usuário |
| `DOCKERHUB_TOKEN` | access token do Docker Hub |
| `GH_PAT` | PAT com permissão de admin no repo (branch protection) |

> No Academy o kubeconfig **expira com a sessão**. Renove o secret a cada lab se for usar o deploy automático.

---

## 12. Teardown obrigatório

**Antes da sessão expirar**, apague o cluster. Recursos órfãos consomem crédito e podem travar o próximo lab.

```bash
eksctl delete cluster --name autorepair-eks --region us-east-1
```

Espere a confirmação (~10 min). Confira:

```bash
aws eks list-clusters --region us-east-1
aws ec2 describe-instances --region us-east-1 \
  --filters "Name=tag:eksctl.cluster.k8s.io/v1alpha1/cluster-name,Values=autorepair-eks" \
  --query "Reservations[].Instances[].State.Name"
```

Não deve restar cluster nem nodes `running`.

Se o `eksctl delete` falhar por token expirado: reinicie o lab, cole as chaves de novo e rode o delete outra vez.

---

## Checklist rápido

- [ ] Lab verde + `aws sts get-caller-identity` ok  
- [ ] Cluster `autorepair-eks` com 2 nodes Ready  
- [ ] Pods: `sqlserver`, `postgres`, `mongodb`, `rabbitmq` Ready  
- [ ] Secrets criados  
- [ ] Imagens no Docker Hub  
- [ ] `os-service` e `billing-service` Running, `Messaging__UseStub=false`  
- [ ] Services tipo LoadBalancer com EXTERNAL-IP  
- [ ] `/health` e `/swagger` ok  
- [ ] Fluxo Saga até `Delivered` (ou rejeição → `Canceled`)  
- [ ] `eksctl delete cluster` executado  

---

## Troubleshooting

| Sintoma | O que checar |
|---|---|
| Pod `ImagePullBackOff` | Nome da imagem / `docker login` / imagem pública |
| OsService sobe mas Budget não aparece | `Messaging__UseStub` deve ser `false`; RabbitMQ Ready; logs do consumer |
| SQL `CrashLoop` | Senha SA com complexidade; PVC Bound; memória do node |
| ELB sem EXTERNAL-IP | Espere 2–3 min; no Academy o ELB demora |
| `ExpiredTokenException` | Lab caiu — Start Lab de novo e reexporte as 3 chaves |
| Webhook quebra OS | Não chame `finish` se o handler ainda faz Finish+Deliver |
| Cluster create falha | Região errada, nodes insuficientes, ou lab sem permissão EKS |

---

## Autores

Guia alinhado ao Tech Challenge **12SOAT Fase 4** · repos `AutoRepairShop-OsService` e `AutoRepairShop-BillingService`.
