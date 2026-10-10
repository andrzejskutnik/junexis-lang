# Dom w trzech środowiskach

Dom 11 przenosi znany system przez pierwsze rzeczywiste granice wykonania:

```text
@embedded HomeDeviceNode
    → ramkowany TCP + JSON
@server HomeCoordinator
    → WebSocket + MessagePack
@report HomeReport
```

Artefakt `@report` nie kończy się już na procesie konsolowym. Generator tworzy
również samodzielną aplikację WWW: `index.html`, bundle przeglądarkowy,
bezpieczny renderer automatyczny, ograniczoną historię, stan połączenia i
lokalny serwer `serve.mjs`. Proces Node pozostaje częścią automatycznego dowodu
transportu, a aplikacja WWW jest powierzchnią dla człowieka.

Projekt ma również własny deklaratywny widok
`user/report/view.json`. Nie zastępuje transportu ani semantyki `@report`;
nazywa panel i wybiera sześć pól ważnych dla człowieka: urządzenie, stan,
powód decyzji, generację połączenia, numer obserwacji i stan bezpieczny.
Kolejny build waliduje ten opis i nigdy go nie nadpisuje.

Najpierw laboratorium dowodzi zwykłego działania. Urządzenie wysyła dwa
heartbeaty z tym samym `boot_id`, koordynator klasyfikuje je jako `online`, a
raport odbiera obie obserwacje. Pierwszy kontakt ma generację `0`: nie nazywamy
poprawnego uruchomienia recovery.

Dopiero potem scenariusz zatrzymuje proces serwera. Próba dostarczenia
heartbeat zostaje odrzucona, raport pozostaje uruchomiony i czeka na ponowne
połączenie. Po starcie koordynator odzyskuje własny stan z dysku. Nowy
`boot_id` urządzenia zostaje sklasyfikowany jako `rejoined`, a licznik
`connection_generation` rośnie do `1`.

## Weryfikacja bez sprzętu

Z katalogu projektu uruchom:

```powershell
.\verification\run-laboratory.ps1
```

Skrypt:

1. sprawdza projekt poleceniem `junexis check`,
2. buduje trzy targety poleceniem `junexis build`,
3. buduje aplikację WWW oraz uruchamia wygenerowane procesy serwera i raportu,
4. wysyła heartbeat przez ten sam kontrakt TCP/JSON, którego używa firmware,
5. zatrzymuje i ponownie uruchamia serwer,
6. sprawdza reconnect raportu przez WebSocket/MessagePack oraz zachowany stan,
7. sprawdza manifest i kompletność wygenerowanego panelu M10.1.

## Golden Home Report w przeglądarce

Pełny odbiór M10.10 ma cztery obserwowalne fazy:

1. panel pokazuje `Połączono — dane aktualne`,
2. heartbeat `boot-alpha` staje się widoczną obserwacją `online`,
3. po zatrzymaniu koordynatora panel pokazuje reconnect i oznacza dane jako
   nieaktualne,
4. po ponownym uruchomieniu i heartbeatcie `boot-beta` ten sam panel pokazuje
   `rejoined` oraz generację połączenia `1`.

Z katalogu projektu uruchom trwały odbiór przeglądarkowy:

```powershell
.\verification\run-browser-e2e.ps1
```

Skrypt używa lokalnego Chrome albo Edge. Jawny silnik można wskazać przez
`-BrowserExecutable`.

Ten sam przebieg wykonuje audyt jakości: jasny i ciemny motyw, kontrast WCAG
AA, fokus i skip-link, mały ekran `390 px`, ograniczenie animacji, kolekcji,
długiego tekstu oraz historii. Wynik liczbowy znajduje się w
`evidence/browser-quality-2026-08-14.md`.

Dowód przeglądarkowy uzupełnia, a nie zastępuje
`verification/run-laboratory.ps1`: skrypt nadal weryfikuje trwały stan,
odrzucenie dostarczenia podczas przerwy i oba natywne kontrakty transportu.

Parametry `FirstBootId`, `SecondBootId`, `ExpectedSecondGeneration` i
`ThirdBootId` pozwalają rozszerzyć doświadczenie bez zmiany programu.

## Uczciwa granica dowodu

Weryfikacja uruchamia prawdziwy wygenerowany serwer i raport, ale zastępuje
fizyczne ESP32 kontrolowanym nadawcą ramek. Artefakt `@embedded` przechodzi
osobną bramkę kompilacji. Test urządzenia, sterownika sieciowego i fizycznego
łącza wymaga płytki oraz środowiska sprzętowego.
