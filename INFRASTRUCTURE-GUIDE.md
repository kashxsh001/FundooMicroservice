# Redis, logging, and Kafka in Fundoo

This project uses the three tools for different jobs:

| Tool | Job here | Data lifetime / delivery |
|---|---|---|
| Redis | Cache per-user note reads to reduce PostgreSQL reads | Temporary, five-minute expiry; invalidated after note writes |
| Structured logging | Emit searchable JSON diagnostics from each API | Operational records; retention is configured by the runtime/platform |
| Kafka | Publish note lifecycle events for future independent consumers | Durable event log, partitioned by user key |

## Redis

Redis is an in-memory key/value data store. It is commonly used for caching, short-lived state, rate limits, and coordination. Here the Notes service uses ASP.NET Core's `IDistributedCache`, so application code talks to a cache abstraction and the Redis provider stores the serialized values.

Read requests first try Redis. A cache miss loads PostgreSQL and stores the result for five minutes. Keys include the user ID and a per-user cache version, so one user's notes cannot be returned as another user's cached result. Note detail is cached separately. A note or label mutation changes the user's cache version; future reads use new keys, and old entries expire after five minutes. This invalidates cached note results that include changed labels without scanning Redis for every note key. If Redis is unavailable, the service logs the failure and falls back to PostgreSQL; cache trouble should not make notes unavailable.

This is a cache-aside pattern: the app checks the cache, loads the source of truth on a miss, then fills the cache. The database remains authoritative. Cache versions are per user, so changing one label invalidates that user's cached note views as well.

## Logging

Logging records what the service did and what failed. Structured logging stores a message template and named values separately. For example, `LogWarning(ex, "Redis read failed for cache key {CacheKey}", key)` emits the exception and a `CacheKey` property rather than an unstructured string. This makes filtering and aggregation possible in tools such as Seq, Grafana Loki, or a cloud log platform.

All four APIs emit JSON console logs. The Notes API also logs HTTP method, path, status, elapsed time, and request ID, and reports Redis/Kafka failures. `ILogger<T>` remains the logging API, so the concrete log destination can be changed later without changing business services. Do not log passwords, JWTs, reset tokens, or 2FA secrets.

## Kafka

Kafka is a distributed event streaming platform. Producers append records to named topics. Topics are split into partitions; records with the same key are routed to the same partition, preserving order for that key. Consumers subscribe independently and track offsets, so one event stream can power multiple consumers without the Notes API knowing who they are.

After a note is written to PostgreSQL, the Notes service publishes a JSON event to `fundoo.note-events`. Events include an event ID, type, note ID, user ID, and UTC timestamp. The user ID is the Kafka message key, which keeps a user's note changes ordered within a partition. Event types currently include created, updated, archived, unarchived, trashed, restored, and deleted. This repository does not yet contain a Kafka consumer; consumers could later update search indexes, send activity notifications, or build analytics.

The current publisher is deliberately best-effort: a broker error is logged and does not fail an API operation after PostgreSQL has already committed. That means a broker outage can lose an event. For production-grade delivery, add a PostgreSQL transactional outbox: save the note change and an outbox row in one database transaction, then a background worker publishes pending rows and marks them sent. Consumers should also deduplicate by event ID because outbox delivery is normally at-least-once.

## Run locally

Start Redis and Kafka from the repository root:

```powershell
docker compose -f docker-compose.infrastructure.yml up -d
```

Defaults are `localhost:6379` for Redis and `localhost:9092` for Kafka. Notes API settings can be overridden with `ConnectionStrings__Redis`, `Kafka__BootstrapServers`, and `Kafka__Topic`. Kafka topics are auto-created in this local single-broker setup. This compose file is for development, not a production cluster; production requires authentication, TLS, replication, monitoring, and managed secrets.

## Key concepts

- **Cache-aside:** application reads cache first and loads the database on a miss.
- **TTL:** time-to-live; entries expire automatically after five minutes to bound stale data.
- **Invalidation:** remove cache entries after writes so the next read refreshes from PostgreSQL.
- **Structured logs:** logs with named fields, useful for querying and correlation.
- **Event:** a fact that already happened, such as `note.archived`.
- **Producer / consumer:** producer writes events; consumer reads and reacts independently.
- **Topic / partition / key / offset:** topic groups related events, partition provides parallel ordered logs, key influences partition selection, and offset is a consumer's position.
- **At-least-once / idempotency:** retries may deliver an event more than once; consumers should make repeated processing safe.
- **Outbox:** durable bridge that stores outgoing events in the same transaction as business data, avoiding the database-plus-broker dual-write gap.
