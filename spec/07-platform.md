# Junexis Lang — System platform

> **Status:** EXPERIMENTAL  
> **Wersja:** 0.2.0  
> **Data:** 2026-06-23

---

## Platformy jako pierwszoklasowy koncept

W Junexis Lang każdy `task` nosi adnotację `@platform`, która mówi kompilatorowi *gdzie* kod będzie wykonywany. Na podstawie tej adnotacji kompilator:

1. **Generuje kod** w docelowym języku (C++, Go, TypeScript)
2. **Waliduje możliwości** — czy platforma obsługuje np. threading, WiFi, disk I/O
3. **Generuje adapter protokołu** pomiędzy taskami na różnych platformach
4. **Emituje diagnostyki** gdy task używa funkcji nieobsługiwanej przez platformę

---

## Trzy platformy PRIMARY (CORE)

### `@embedded`

**Target:** Mikrokontrolery, urządzenia IoT (ESP32, STM32, Arduino, RP2040, ...)  
**Język generacji:** C/C++ (PlatformIO / bare-metal)

```junexis
task TempSensor() @embedded -> chan<TempData>
task Led(cmd: chan<LedCommand>) @embedded(esp32c3)
```

**Charakterystyka:**
- Ograniczona pamięć RAM (dziesiątki KB — kilka MB)
- Brak systemu plików (lub ograniczony)
- Brak pełnego OS — typowo FreeRTOS lub bare-metal
- Hardware interrupts, GPIO, ADC, I2C/SPI/UART
- `queue<T>` wymaga `storage_budget` (→ `E4210`)
- `chan<T>` i `stream<T>` implementowane przez ring bufory

**Warianty (argumenty):**

```junexis
@embedded                      // generyczny embedded
@embedded(esp32c3)             // konkretny chip → profilowany kod
@embedded(esp32s3)
@embedded(stm32f103)
@embedded(arduino_uno)
@embedded(rp2040)
```

Wariant wpływa na:
- Rozmiar stosu zadań
- Dostępność WiFi / Bluetooth
- Maksymalna liczba równoległych tasków
- Generowane `#include` i platformio.ini dependencies

**Ograniczenia (D5 - kompilator emituje błędy lub ostrzeżenia):**

| Cecha | Obsługa | Diagnostika |
|-------|---------|-------------|
| Threading | Zależy od wariantu | `W5002` jeśli wariant nie wspiera |
| WiFi/Networking | Zależy od wariantu | `E5003` jeśli wariant nie ma WiFi |
| File I/O | Zależy od wariantu | `E5003` jeśli wariant nie ma SD/Flash |
| Dynamic allocation | Ograniczone | `W5002` przy przekroczeniu memory budget |
| `queue` bez budżetu | Niedozwolone | `E4210` |

---

### `@server`

**Target:** Serwer backendowy, cloud, kontener  
**Język generacji:** Go (domyślnie), C# (opcjonalnie przez `@protocol`)

```junexis
task DataProcessor(input: chan<SensorData>) @server -> chan<Alert>
task ApiGateway() @server(cloud)
```

**Charakterystyka:**
- Pełen system operacyjny (Linux/Windows)
- Nieograniczona (praktycznie) pamięć i storage
- Networking: HTTP, TCP, WebSocket, gRPC
- Goroutines / async/await — pełny concurrency
- Deployment: Docker, Kubernetes, bare-metal

**Warianty:**

```junexis
@server                  // generyczny server
@server(cloud)           // hint: cloud-native (auto-scaling, health probes)
@server(edge)            // edge computing — ograniczone zasoby
```

---

### `@report`

**Target:** Frontend, dashboard, wizualizacja  
**Język generacji:** TypeScript / React

```junexis
task Dashboard(metrics: stream<SystemMetrics>) @report
task UserInterface(events: chan<UIEvent>) @report(browser)
```

**Charakterystyka:**
- Przeglądarka lub React Native
- WebSocket do odbierania `stream<T>`
- REST/gRPC do `chan<T>` (request-response)
- MQTT do `queue<T>` przez MQTT.js
- TypeScript z pełną inferencją typów

**Warianty:**

```junexis
@report              // generyczny frontend
@report(browser)     // explicit browser target
@report(mobile)      // React Native
```

---

## Platformy EXPERIMENTAL (rozszerzone)

Kompilator rozpoznaje adnotacje, ale generacja kodu jest best-effort lub niezaimplementowana w 0.2:

| Adnotacja | Opis |
|-----------|------|
| `@edge` | Edge computing node (między embedded a server) |
| `@cloud_server` | Alias dla `@server(cloud)` |
| `@mobile` | Alias dla `@report(mobile)` |
| `@desktop` | Aplikacja desktop (planowane Avalonia/Electron) |
| `@web` | Alias dla `@report(browser)` |
| `@microcontroller` | Alias dla `@embedded` |
| `@fog_node` | Fog computing (EXPERIMENTAL) |
| `@data_center` | Data center rack — duże serwery |

---

## `device` — deklaracja sprzętu

`device` łączy `task @embedded` ze specyfikacją hardware:

```junexis
device esp32_sensor @embedded(esp32c3) {
    hardware {
        dht22_pin: u8 : 4
        led_pin:   u8 : 2
        uart_baud: u32 : 115200
    }

    network {
        ssid:     Text : "MyHomeNet"
        ip:       Text : "192.168.1.22"
        port:     u16  : 8883
    }

    reference {
        @c/c++ {
            import: "dht22_adafruit"
            include: "WiFi.h"
        }
    }
}
```

Sekcje `device`:

| Sekcja | Typ | Opis |
|--------|-----|------|
| `hardware {}` | Typed fields | GPIO piny, baud rate, ADC channels |
| `network {}` | Typed fields | IP, port, WiFi SSID |
| `timing {}` | Typed fields | Timeouty, interwały |
| `sensors {}` | Typed fields | Metadane sensorów |
| `reference {}` | Language blocks | Snippety bibliotek do importu |

**Odwoływanie się do device z task:**

```junexis
task SensorRead() @embedded(esp32_sensor) -> chan<TempData> {
    every 5s {
        let pin   = device.hardware.dht22_pin
        let temp  = dht_read(pin)
        send TempData { celsius: temp }
    }
}
```

`device.hardware.*`, `device.network.*`, `device.timing.*` — referencje rozwiązywane przez kompilator w czasie kompilacji.

**Multi-device deployment:**

```junexis
device sensor_kitchen @embedded(esp32c3) {
    hardware { dht22_pin: u8 : 4 }
    network  { ip: Text : "192.168.1.22" }
}

device sensor_bedroom @embedded(esp32c3) {
    hardware { dht22_pin: u8 : 5 }
    network  { ip: Text : "192.168.1.23" }
}

task TempSensor() @embedded -> chan<TempData>

deployment SmartHome {
    TempSensor@sensor_kitchen -> DataHub
    TempSensor@sensor_bedroom -> DataHub
}
```

Kompilator generuje dwie osobne binaries — dla `sensor_kitchen` i `sensor_bedroom`.

---

## `config` — konfiguracja runtime

Sekcja `config` wewnątrz `task` lub `device` deklaruje parametry dostępne przez `config.*`:

```junexis
task DataProcessor(input: chan<SensorData>) @server {
    config {
        threshold:    Float    : 35.0
        window_size:  Int      : 60
        alert_topic:  Text     : "alerts/critical"
    }

    for {
        let reading = recv input
        if reading.celsius > config.threshold {
            send Alert { msg: "HIGH TEMP", value: reading.celsius }
        }
    }
}
```

- Typy: dowolny typ Junexis (primitives, struct, `Duration`, `Memory`)
- Domyślna wartość po `:` jest `optional` — bez niej pole jest wymagane w deployment config
- Można nadpisać przez YAML deployment config (bez rekompilacji)

---

## `reference` — snippety bibliotek

Sekcja `reference` w `device` mapuje Junexis device params na kod biblioteki:

```junexis
device my_sensor @embedded(esp32c3) {
    reference {
        @c/c++ {
            import:  "dht22_adafruit"   // ładuje snippet z biblioteki
            include: "Wire.h"
            library: "adafruit/DHT-sensor-library@^1.4.6"
        }
        @micropython {
            import: "dht22_micropython"
        }
    }
}
```

Dostępne pola:
| Pole | Opis |
|------|------|
| `import` | Załaduj snippet z biblioteki (predefiniowane) |
| `include` | Bezpośrednie `#include` w C/C++ |
| `library` | Zależność platformio.ini |
| `use` | Deklaracja global (C: extern, C#: using) |
| `init` | Kod do wstrzyknięcia w `setup()` |

---

## Walidacja platform w kompilatorze

Kompilator ładuje profil platformy (YAML lub hardcoded) i sprawdza:

1. **Możliwości** — czy platforma obsługuje networking, threading, file I/O
2. **Limity pamięci** — `MaxMemoryBytes` vs. suma buforów i stanu tasków
3. **Limity równoległości** — `MaxConcurrentTasks` vs. liczba tasków
4. **Kompatybilność protokołu** — czy platforma obsługuje dany `@protocol`

```
Platforma: esp32c3
- MaxMemoryBytes: 409600 (400KB)
- SupportsNetworking: true
- SupportsThreading: false
- MaxConcurrentTasks: 8
```

Kompilator emituje:
- `E5001` — task przekracza limit pamięci
- `W5002` — użycie >80% limitu pamięci
- `E5003` — task wymaga cechy nieobsługiwanej przez platformę
- `E5004` — runtime memory usage exceeds limit (walidacja runtime)
- `W5008` — wysoki poziom concurrency (>80% max)

---

## Generacja kodu per platforma

Kompilator generuje osobny artefakt dla każdej platformy:

| Platform | Język | Toolchain |
|----------|-------|-----------|
| `@embedded(esp32c3)` | C++17 | PlatformIO / ESP-IDF |
| `@embedded(stm32*)` | C++17 | PlatformIO / STM32CubeIDE |
| `@embedded(arduino*)` | C++11 | Arduino CLI |
| `@server` | Go 1.21+ | go build |
| `@server` (C#) | C# .NET 9 | dotnet build |
| `@report` | TypeScript 5+ | npm / Vite |

Adaptery protokołów (mosty między platformami) generowane automatycznie — nie trzeba pisać kodu serializacji/deserializacji.
