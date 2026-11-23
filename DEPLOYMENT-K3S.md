# Deploy FundooMicroService to the Azure K3s VM

These manifests use this repository's YARP gateway and the four public Docker Hub images:

- `kashxsh001/fundoo-gateway:live`
- `kashxsh001/fundoo-user:live`
- `kashxsh001/fundoo-notes:live`
- `kashxsh001/fundoo-notification:live`

The gateway is exposed on VM TCP port `30080`. PostgreSQL uses a 5 GiB K3s `local-path` volume. The Note and User databases are initialized from `k8s/10-data.yaml` on the first PostgreSQL start. The Notification service sends SMTP email and does not use a NotificationDb in this project.

## 1. Allow inbound traffic

In the Azure VM's Network Security Group, allow inbound **TCP 30080** from your client IP. Do not expose PostgreSQL, Redis, or Kafka publicly. The VM firewall must also allow TCP 30080 if UFW is enabled.

## 2. Create the Kubernetes secret

SSH to the VM and run the following, replacing each prompt value with your real setting. Use a JWT signing key of at least 32 characters. The user and notes connection strings must use the same PostgreSQL username/password entered below.

```bash
kubectl create namespace fundoo --dry-run=client -o yaml | kubectl apply -f -
read -rsp 'Postgres password: ' PG_PASSWORD; echo
read -rp 'Postgres username [fundoo]: ' PG_USER; PG_USER=${PG_USER:-fundoo}
read -rsp 'JWT signing key (32+ chars): ' JWT_KEY; echo
read -rsp 'Shared User/Notification signing key (32+ chars): ' SERVICE_KEY; echo
read -rp 'Google OAuth client ID: ' GOOGLE_ID
read -rsp 'Google OAuth client secret: ' GOOGLE_SECRET; echo
read -rp 'SMTP host: ' SMTP_HOST
read -rp 'SMTP port [587]: ' SMTP_PORT; SMTP_PORT=${SMTP_PORT:-587}
read -rp 'SMTP sender email: ' SMTP_SENDER
read -rp 'SMTP username: ' SMTP_USER
read -rsp 'SMTP password: ' SMTP_PASSWORD; echo

kubectl -n fundoo create secret generic fundoo-secrets \
  --from-literal=POSTGRES_USER="$PG_USER" \
  --from-literal=POSTGRES_PASSWORD="$PG_PASSWORD" \
  --from-literal=USER_DB_CONNECTION="Host=postgres;Port=5432;Database=UserDb;Username=$PG_USER;Password=$PG_PASSWORD" \
  --from-literal=NOTES_DB_CONNECTION="Host=postgres;Port=5432;Database=NotesDb;Username=$PG_USER;Password=$PG_PASSWORD" \
  --from-literal=JWT_SIGNING_KEY="$JWT_KEY" \
  --from-literal=SERVICE_AUTH_SIGNING_KEY="$SERVICE_KEY" \
  --from-literal=GOOGLE_CLIENT_ID="$GOOGLE_ID" \
  --from-literal=GOOGLE_CLIENT_SECRET="$GOOGLE_SECRET" \
  --from-literal=SMTP_HOST="$SMTP_HOST" \
  --from-literal=SMTP_PORT="$SMTP_PORT" \
  --from-literal=SMTP_SENDER_EMAIL="$SMTP_SENDER" \
  --from-literal=SMTP_USERNAME="$SMTP_USER" \
  --from-literal=SMTP_PASSWORD="$SMTP_PASSWORD" \
  --dry-run=client -o yaml | kubectl apply -f -
unset PG_PASSWORD JWT_KEY SERVICE_KEY GOOGLE_SECRET SMTP_PASSWORD
```

The Google OAuth client ID/secret are required by the current User API during startup, even when Google login is not used. Use the same service signing key for `SERVICE_AUTH_SIGNING_KEY` in User and Notification.

## 3. Apply the workloads

From the repository root on the VM:

```bash
kubectl apply -k k8s/
kubectl -n fundoo get pods -w
```

Wait until all Pods are `Running` and `postgres-pvc` is `Bound`:

```bash
kubectl -n fundoo get pods,pvc,svc
```

Check startup errors with `kubectl -n fundoo logs deployment/user-api` or the deployment name shown by `get pods`.

## 4. Check the API

On the VM, check the gateway through the NodePort:

```bash
curl -i http://localhost:30080/api/auth/login
```

An HTTP `400` or `405` response confirms that the gateway route is reachable; login itself requires a POST JSON body. From your computer, use:

```text
http://<AZURE_VM_PUBLIC_IP>:30080/api/auth/register
```

Register and login using POST requests with JSON bodies. The login response returns `accessToken`; pass it as `Authorization: Bearer <accessToken>` when calling the Notes and Labels endpoints.

## Database initialization note

PostgreSQL runs the SQL files only when its data directory is empty. If this PVC has already been initialized, the ConfigMap SQL changes do not run automatically. Check the schema before retrying. For an existing database, back it up and apply the required SQL manually; do not delete the PVC to force initialization if it contains data.
