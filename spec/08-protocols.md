# Junexis Lang — Protokoły i adaptery

> **Status:** EXPERIMENTAL  
> **Wersja:** 0.2.0  
> **Data:** 2026-06-23

---

## Filozofia protokołów (D7)

W Junexis Lang **task nie wie nic o protokołach**. Task opisuje logikę domenową. Protokół to osobna deklaracja top-level, która binduje taski z powierzchnią komunikacyjną.

```junexis
// Task — czysta logika domenowa
task ListUsers(limit: u32) @server -> List<User> { ... }
task CreateUser(user: UserInput) @server -> User { ... }

// Protocol binding — osobna deklaracja
protocol rest {
    GET("/users",      ListUsers(query limit: u32))
    POST("/users",     CreateUser(body user: UserInput))
}

protocol grpc {
    ListUsers
    CreateUser
}
```

Jeden task może być zbindowany do wielu protokołów bez jednej zmiany w tasku. Middleware działa per `protocol { }`, nie per task.

**Składnia:**

```
protocol <nazwa> {
    <opaque body — interpretowany przez IR module>
}
```

Gdzie `<nazwa>` to `IDENTIFIER`. Parser nie waliduje nazwy — walidacja należy do IR stage. Wbudowane IR modules: `rest`, `grpc`, `ws`, `mqtt`, `coap`, `amqp`. Własny protokół wymaga user-defined IR module.

### Wykonywalny podzbiór 0.2

Structural IR nadal zachowuje ciało bezstratnie jako tekst. Następny etap,
`DistributedTopologyLowerer`, interpretuje bindingi do zadeklarowanych tasków.
W baseline M4 wykonywalny jest `protocol rest`: powstają handlery Go `net/http`
oraz `openapi.json`. Referencja do nieistniejącego tasku daje `D6001`.
Pozostałe nazwy protokołów są rozpoznawane w planie, lecz ich adaptery nadal
mają status eksperymentalny.

---

## Domyślne protokoły — automatyczny dobór

Gdy taski są połączone przez `pipeline`, a binding `protocol` nie jest zadeklarowany, kompilator dobiera protokół automatycznie:

| Nadawca | Odbiorca | Typ | Domyślny protokół |
|---------|----------|-----|-------------------|
| `@embedded` | `@server` | `chan<T>` | MQTT over TLS |
| `@embedded` | `@server` | `queue<T>` | MQTT QoS 1 |
| `@embedded` | `@server` | `stream<T>` | MQTT retain |
| `@server` | `@server` | `chan<T>` | gRPC |
| `@server` | `@server` | `queue<T>` | AMQP |
| `@server` | `@server` | `stream<T>` | gRPC streaming |
| `@server` | `@report` | `chan<T>` | REST HTTP |
| `@server` | `@report` | `stream<T>` | WebSocket |
| `@server` | `@report` | `queue<T>` | WebSocket + ACK |
| `@embedded` | `@embedded` | dowolny | In-process channel |
| `@server` | `@server` (ten sam proces) | dowolny | In-process channel |

Kompilator emituje `I6002` (info) gdy automatycznie dobiera protokół.

---

## Obsługiwane protokoły (wbudowane IR modules)

### `protocol rest`

```junexis
task ListUsers(limit: u32) @server -> List<User> { ... }
task CreateUser(user: UserInput) @server -> User { ... }
task GetUser(id: u32) @server -> User { ... }
task UpdateUser(id: u32, user: UserInput) @server -> User { ... }
task DeleteUser(id: u32) @server -> void { ... }

protocol rest {
    GET("/users",           ListUsers(query limit: u32))
    POST("/users",          CreateUser(body user: UserInput))
    GET("/users/{id}",      GetUser(path id: u32))
    PUT("/users/{id}",      UpdateUser(path id: u32, body user: UserInput))
    DELETE("/users/{id}",   DeleteUser(path id: u32))
}
```

**Parametry:**

| Typ | Składnia | Opis |
|-----|----------|------|
| Path | `path name: Type` | Z URL `{id}` |
| Query | `query name: Type` | Query string `?limit=10` |
| Body | `body name: Type` | Request body |
| Header | `header name: Type` | HTTP header |

Kompilator generuje:
- Go: `net/http` handlers
- C#: ASP.NET Core Minimal API
- TypeScript (`@report`): `fetch` client z typowanymi metodami
- OpenAPI 3.0 spec

---

### `protocol grpc`

```junexis
task ListUsers(limit: u32) @server -> List<User> { ... }
task GetUser(id: u32) @server -> User { ... }

protocol grpc {
    ListUsers
    GetUser
}
```

Kompilator generuje `.proto` file i kod stub/server dla Go i C#. Ostrzeżenie `W5001` przy próbie użycia `stream<T>` z gRPC na wielu odbiorców (1:1 semantics).

---

### `protocol ws`

```junexis
task MetricsHub() @server -> stream<Metrics> { ... }

protocol ws {
    stream MetricsHub
}
```

Kompilator generuje:
- Server: Go `gorilla/websocket` lub C# `System.Net.WebSockets`
- Client (`@report`): TypeScript `WebSocket` z automatycznym reconnect i decode MessagePack

Domyślny format: MessagePack. Parametr `format: json` nadpisuje.

---

### `protocol mqtt`

```junexis
task TempSensor() @embedded(esp32c3) -> queue<TempReading> { ... }

protocol mqtt {
    TempSensor(broker: "mqtt://192.168.1.1:1883", qos: 1)
}
```

**QoS levels:**

| QoS | Semantyka | Junexis typ |
|-----|-----------|-------------|
| 0 | At most once | `stream<T>` |
| 1 | At least once | `queue<T>` |

Topic generowany automatycznie: `junexis/{project}/{device_id}/{task_name}`. Parametr `topic:` nadpisuje.

---

### `protocol coap`

```junexis
task MicroSensor() @embedded(esp32c3) -> chan<Reading> { ... }

protocol coap {
    MicroSensor(confirmable: true)
}
```

Kompilator generuje CoAP client dla embedded (libcoap / CoAP.NET).

---

### `protocol amqp`

```junexis
task OrderProcessor(orders: queue<Order>) @server -> void { ... }

protocol amqp {
    OrderProcessor(broker: "amqp://rabbitmq:5672", queue: "orders")
}
```

Kompilator generuje Go `amqp091-go` lub C# `RabbitMQ.Client`. AMQP DLQ mapuje automatycznie na Junexis DLQ semantics.

---

## Middleware — wspólne dla protokołu

```junexis
protocol rest {
    GET("/users",  ListUsers(query limit: u32))
    POST("/users", CreateUser(body user: UserInput))

    middleware {
        auth: RequireJwt
        rateLimit: 100/min
        cors: "*"
    }
}
```

Middleware zadeklarowany raz — dotyczy wszystkich endpointów w bloku.

---

## Własny protokół (user-defined IR module)

```junexis
// projekt definiuje IR module dla kafki
protocol kafka {
    OrderCreated(topic: "orders.created", partitionKey: order.id)
    OrderShipped(topic: "orders.shipped", partitionKey: order.id)
}
```

IR module musi być zadeklarowany w `junexis.toml`:

```toml
[protocols]
kafka = "./ir/kafka-module"
```

Brak modułu → błąd kompilacji `E: unknown protocol 'kafka'`.

---

## Adapter protokołu — jak to działa

Developer pisze:

```junexis
task TempSensor() @embedded -> chan<TempData> { ... }
task DataHub(input: chan<TempData>) @server -> chan<Alert> { ... }

pipeline Home {
    use TempSensor -> DataHub
}
```

Kompilator wykrywa parę `@embedded → @server`, dobiera MQTT automatycznie (I6002), generuje:
1. `TempSensor/main.cpp` — ESP32 C++ z MQTT publish
2. `DataHub/main.go` — Go serwer z MQTT subscribe
3. Automatyczna serializacja/deserializacja `TempData` (MessagePack)
4. `deployment/docker-compose.yml` z MQTT brokerem

Deweloper nie pisze ani jednej linii kodu sieciowego.

---

## Diagnostyki protokołów

| Kod | Poziom | Kiedy |
|-----|--------|-------|
| `E4203` | Error | `chan<T>` fanout bez broadcast bridge |
| `W4201` | Warning | `stream<T>` → zero-loss task bez adaptera |
| `W5001` | Warning | gRPC + `stream<T>` fanout (1:1 semantics) |
| `E5003` | Error | Platform nie obsługuje wymaganego protokołu |
| `W6001` | Warning | REST + `queue<T>` — polling overhead |
| `I6002` | Info | Auto-selected protocol |

---

## Serializacja

Kompilator dobiera format automatycznie:

| Protokół | Domyślny format | Override |
|----------|-----------------|---------|
| gRPC | Protobuf | — (wymagany) |
| MQTT | MessagePack | `format: json` |
| WebSocket | MessagePack | `format: json` |
| REST | JSON | `format: msgpack` |
| CoAP | CBOR | `format: json` |
| AMQP | MessagePack | `format: json` |
