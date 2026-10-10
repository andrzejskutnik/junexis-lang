# Dom, który zauważa i reaguje

Pierwszy wykonywalny fundament Laboratorium Junexis. Projekt nie wymaga
sprzętu: zadanie `HomeScenario` tworzy kuchnię, sypialnię i salon, a następnie
generuje deterministyczną sekwencję normalnego pomiaru, pogorszenia jakości
powietrza, utraty czujnika salonu i jego powrotu.

Tryb wykonania jest jawny. `laboratory.json` deklaruje `execution.mode` jako
`simulation`, wyłącza automatyczny powrót do sprzętu i przypina identyfikator
scenariusza. Działające API potwierdza ten wybór polami
`execution_mode: "simulation"` oraz `hardware_connected: false`. Laboratorium
nie udaje połączenia z czujnikiem i nigdy nie przełącza się po cichu między
symulacją a urządzeniem.

Publiczna ścieżka użytkownika korzysta z nadrzędnego polecenia:

```powershell
junexis check
junexis analyze
junexis build
```

Autor wydania tworzy samodzielną paczkę dla wybranego systemu:

```powershell
junexis lab package --runtime win-x64
junexis lab package --runtime linux-x64
junexis lab package --runtime osx-arm64
```

Brak `--runtime` oznacza bieżący system. Kompilacja krzyżowa potwierdza budowę
i integralność paczki, natomiast dowód wykonania powstaje dopiero po jej
uruchomieniu na właściwym systemie.

Uczestnik laboratorium nie potrzebuje źródeł ani kompilatora. Sprawdza pobrany
plik i uruchamia gotową aplikację:

```powershell
junexis lab verify smart-home-preview-1-win-x64.junexis-lab.zip
junexis lab run smart-home-preview-1-win-x64.junexis-lab.zip
```

Aktualny zakres obejmuje kod serwerowy, jawne decyzje, interaktywny panel,
sterowaną awarię czujnika, reset, punkty kontrolne oraz oś przyczyn. Po uruchomieniu serwera
panel jest dostępny pod adresem `http://127.0.0.1:8080/laboratory/`. Pokazuje
rzeczywisty stan wykonywanego programu i komunikuje się z zadaniami Junexis
przez wygenerowany protokół REST. `laboratory.json` jest roboczym opisem
pierwszego laboratorium; schemat nie jest jeszcze stabilnym kontraktem
platformy.

Oś przyczyn pokazuje ciąg `obserwacja → reguła → decyzja → skutek` dla
rzeczywistych zdarzeń tego wykonania. Gdy uruchomienie otrzyma
`JUNEXIS_TRACE_URL`, `JUNEXIS_BUILD_ID` i `JUNEXIS_BUNDLE_ID`, panel dołącza
również dowód z prawdziwego TraceCollectora: identyfikator zdarzenia, głębokość
`/why` i zadania uczestniczące w rekonstrukcji. Bez collectora laboratorium
pozostaje działające i jawnie pokazuje tryb fail-open.

Oczekiwana sekwencja decyzji:

1. `air-quality-normal`,
2. `air-quality-high` i `enable-ventilation`,
3. `sensor-unavailable` i `keep-ventilation-safe`,
4. `air-quality-normal` po odzyskaniu czujnika.

Pierwsza weryfikacja parsera, analizy, generowania, natywnej kompilacji i
uruchomienia znajduje się w `evidence/verification-2026-08-11.md`. Sprawdzono
również łagodne zatrzymanie wszystkich zadań, kontrakt API, panel i przejście
wszystkich punktów kontrolnych. Powtarzalny odbiór uruchomionego systemu wykonuje
`verification/verify-running-laboratory.ps1`; skrypt kończy się wynikiem
`PASS` wyłącznie po przejściu scenariusza automatycznego, wymuszonej awarii i
resetu.

## Etap Eksperymentuj

Panel pozwala teraz zmienić trzy parametry działającego systemu:

- próg jakości powietrza od `600` do `2000` ppm,
- opóźnienie komunikacji od `0` do `3000` ms,
- politykę utraty czujnika: bezpieczną wentylację albo wstrzymanie automatyki.

Zastosowanie konfiguracji przełącza etap na `eksperymentuj`, zwiększa numer
rewizji i dopisuje zmianę do osi przyczyn. Reset usuwa wymuszenia oraz przywraca
próg `1000` ppm, brak opóźnienia i bezpieczną wentylację. Automatyczny odbiór
sprawdza również, że przy progu `1400` ppm pomiar `1250` ppm nie włącza
wentylacji, a alternatywna polityka rzeczywiście wstrzymuje automatykę po
utracie czujnika.

## Etap Przejmij kontrolę

Ekspercki panel publikuje pełną efektywną konfigurację i dodaje trzy decyzje
operacyjne:

- recovery `automatic` albo `manual-approval`,
- opóźnienie recovery od `250` do `10000` ms,
- limit zachowywanych zdarzeń dowodowych od `6` do `60`.

Dodaje też kontrolowaną próbę przeciążenia. Producent wysyła serię zdarzeń do
rzeczywiście ograniczonego kanału Go, a panel pokazuje liczbę przyjętych,
odrzuconych i przetworzonych elementów. Dom nie ukrywa utraty: po opróżnieniu
bufora zapisuje osobne zdarzenie recovery.

Automatyczne recovery usuwa wymuszenie awarii dopiero po skonfigurowanym czasie.
Tryb ręczny zachowuje stan ograniczony po odrzuceniu i wymaga jawnego
zatwierdzenia operatora. Zmiana pełnego profilu, odrzucenie, zatwierdzenie i
ukończenie recovery są zapisywane na tej samej osi przyczyn. Reset unieważnia
oczekujące recovery i przywraca tryb `manual-approval`, `2000` ms oraz limit
`18` zdarzeń.

`junexis lab run` przekazuje aplikacji trwały katalog
`%LOCALAPPDATA%\Junexis\Laboratories\smart-home`. Konfiguracja, wymuszona awaria
i ograniczona oś dowodowa są zapisywane atomowo. Kolejne uruchomienie tej samej
paczki odtwarza je, dopisuje zdarzenie `durable-state-loaded` i pozwala wykonać
recovery po prawdziwym restarcie procesu. Powtarzalny test tej granicy znajduje
się w `verification/verify-restart-recovery.ps1`.

## Dom 10: trwałość procesu i ograniczone przeciążenie

Ten etap nie traktuje restartu jako recovery. Przed zatrzymaniem procesu zapisuje
pełną konfigurację, aktywną awarię, oczekujące ręczne recovery i oś dowodową.
Po uruchomieniu nowego procesu odtwarza ten kontekst, lecz nadal wymaga decyzji
człowieka.

Dom 10 odróżnia również stan trwały od pracy w toku. Seria `24` zdarzeń trafia
do rzeczywistego kanału o pojemności `8`: osiem elementów zostaje przyjętych,
a szesnaście jawnie odrzuconych. Jeżeli restart przerwie opróżnianie kolejki,
nowy proces nie odtwarza nietrwałych elementów z pamięci. Zamyka ich kontekst
zdarzeniem `overload-interrupted`, zachowuje liczniki przyjęcia i odrzucenia oraz
nie udaje, że konsument zakończył pracę.

Odbiór etapu sprawdza osobno:

- opróżnienie ograniczonego bufora bez restartu,
- odtworzenie konfiguracji, awarii i osi po restarcie,
- jawne zamknięcie przerwanej kolejki,
- brak automatycznego recovery w trybie ręcznym,
- zatwierdzenie recovery dopiero w nowym procesie.
