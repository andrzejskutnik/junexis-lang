# Weryfikacja 2026-08-11

Sprawdzony zakres:

- `check` — zaliczony,
- `analyze` — zaliczony; zapisano analizę przed budowaniem,
- `build --target server` — zaliczony; wygenerowano Go, mapę źródeł i manifesty,
- `artifact-gate --target server` — zaliczony wraz z natywnym narzędziem Go,
- uruchomienie wygenerowanego serwera — zaliczone dla pełnego cyklu decyzji,
- łagodne zatrzymanie — zaliczone; zadania zakończyły się razem z serwerem.

Zaobserwowana sekwencja:

```text
HOME 1: living-room -> observe because air-quality-at-or-below-1000
HOME 2: living-room -> enable-ventilation because air-quality-over-1000
HOME 3: living-room -> keep-ventilation-safe because require-fresh-sensor
HOME 4: living-room -> observe because air-quality-at-or-below-1000
```

Końcowy komunikat procesu:

```text
[server] shutdown signal received (persistent mode)
[server] all tasks stopped, exiting
```

Weryfikacja ujawniła i domknęła defekt generatora: zadania okresowe oraz
odbiorcy kanałów obserwują teraz wspólny sygnał zakończenia, a generowane kanały
wyjściowe są zamykane po zakończeniu producenta.

## Panel, API i kontrolowana awaria

Drugi przebieg objął wygenerowany protokół REST, panel działający na tym samym
serwerze oraz implementację jawnego kontraktu `HomeLab` w kodzie użytkownika.

- `GET /laboratory/` odpowiedział statusem `200` i zwrócił panel domu,
- scenariusz automatyczny osiągnął wszystkie pięć punktów kontrolnych,
- `POST /api/laboratory/fault` wymusił niedostępność czujnika,
- system wybrał `sensor-unavailable` oraz `keep-ventilation-safe`,
- `POST /api/laboratory/reset` usunął wymuszenie i odtworzył sekwencję `0`,
- natywna bramka artefaktu serwera zakończyła się wynikiem `OK`.

Próba ujawniła i pozwoliła poprawić dwie ogólne luki generatora Go: konwersję
złożonych wyników z pakietu `user` oraz rozpoznawanie bezargumentowych wywołań
`extern` zaczynających się wielką literą. Zestaw generatora po poprawkach:
247/247 testów.

## Trzy pomieszczenia i oś przyczyn

Trzeci przebieg rozszerzył ten sam projekt, bez tworzenia osobnego przykładu.

- panel otrzymał stan kuchni, sypialni i salonu,
- punkt `home-started` jest osiągany dopiero po poznaniu wszystkich trzech
  pomieszczeń,
- historia zachowuje obserwacje, decyzje, awarie, odtworzenia i działania
  użytkownika,
- każda decyzja wiąże jawną regułę (`cause`) z działaniem (`effect`),
- test potwierdził zdarzenie `air-quality-over-1000 → enable-ventilation`,
- automatyczny odbiór ponownie zakończył się wynikiem `PASS`.

Ta oś jest kontraktem domenowym działającego laboratorium. Pełne połączenie jej
ze śladami wykonania Narrative zostało sprawdzone w następnym przebiegu.

## Narrative i rzeczywisty TraceCollector

Czwarty przebieg uruchomił prawdziwy `Junexis.Debugger.TraceCollector.Server`
oraz wygenerowany serwer z jedną parą tożsamości:

- Build ID: `smart-home-lab-2026-08-11`,
- Bundle ID: `smart-home-lab-preview-1`,
- Narrative ID: `Smart Home Laboratory`.

Generator Go wyemitował pasujące `COMM_SEND` i `COMM_RECV` dla wartości
`SensorReading` oraz `HomeDecision`. Obie strony kanału użyły identycznego
SHA-256 payloadu, bez przekazywania jego treści do collectora. Test panelu
potwierdził:

- `narrative_connected = true`,
- zdarzenie docelowe `77`,
- głębokość `/why` równą `77`,
- obecność `HomeScenario`, `HomeDecisionEngine` i `HomeObserver`,
- zgodność Build ID i Bundle ID,
- końcowy wynik `PASS`.

Po zatrzymaniu TraceCollectora serwer nadal odpowiadał, zachował trzy
pomieszczenia i zgłosił `narrative_connected = false`. Potwierdza to wymagany
tryb fail-open.

## Samodzielna paczka Windows x64

Piąty przebieg sprawdził nową ścieżkę użytkową `junexis lab`:

- `lab package` wykonało analizę, natywną bramkę, związany build oraz System
  Bundle, po czym zbudowało serwer `win-x64`,
- paczka `smart-home-preview-1-win-x64.junexis-lab.zip` zachowała Build ID
  `28bb2f66b392c3a549b99af618bddd5553e0be203970f01afdc8e8ce6391f54a`,
- zachowała Bundle ID
  `97b9fd99e94adbee20b22c6166e05a4a41691f3055ea264ee7b821d12f0d0bc1`,
- `lab verify --json` potwierdziło sumy czterech plików payloadu,
- `lab run` uruchomiło samodzielny plik wykonywalny na portach
  `18080/18081/19000` i otrzymało gotowość panelu,
- w ograniczonym trzysekundowym przebiegu zaobserwowano normalne pomiary,
  włączenie wentylacji oraz bezpieczny stan po utracie czujnika.

Uruchomienie paczki nie korzystało z checkoutu źródeł ani z kompilacji Go.

## Pierwszy przebieg etapu Eksperymentuj

Szósty przebieg zmienił zachowanie tego samego uruchomionego domu przez
typowane API laboratorium:

- próg jakości powietrza zmieniono z `1000` na `1400` ppm,
- do komunikacji dodano `500` ms kontrolowanego opóźnienia,
- politykę utraty czujnika zmieniono z `keep-ventilation-safe` na
  `pause-automation`,
- pomiar `1250` ppm zakończył się obserwacją zamiast włączenia wentylacji,
- po wymuszeniu awarii ostatnią akcją było `pause-automation`,
- panel odpowiedział `200` i zawierał sekcję stawiania hipotezy,
- reset odtworzył etap `poznaj`, próg `1000`, brak opóźnienia i bezpieczną
  politykę domyślną.

Konfiguracja otrzymała rewizję `1` i została zapisana na osi przyczyn jako
zdarzenie `experiment`. Parser, build oraz natywna bramka Go przeszły po
rozszerzeniu kontraktu.

## Pierwszy przebieg etapu Przejmij kontrolę

Siódmy przebieg sprawdził dwa jawne kontrakty recovery:

- profil automatyczny otrzymał opóźnienie `750` ms i limit `6` dowodów,
- po wymuszeniu awarii stan przeszedł przez `recovery_pending`, a następnie
  sam usunął wymuszenie z przyczyną `automatic-recovery-policy`,
- liczba zachowanych zdarzeń nie przekroczyła skonfigurowanego limitu,
- profil ręczny utrzymał awarię po odrzuceniu recovery,
- jawne zatwierdzenie operatora usunęło wymuszenie,
- panel odpowiedział `200` i pokazał efektywną konfigurację,
- reset przywrócił `manual-approval`, `2000` ms i `18` dowodów.

Konfiguracja ekspercka działa na tym samym systemie i API co etapy Poznaj oraz
Eksperymentuj. Nie jest osobną makietą ani kopią przykładu.

## Przeciążenie, trwałe dowody i restart procesu

Ósmy przebieg rozszerzył etap Przejmij kontrolę o granice, których nie da się
potwierdzić samą makietą panelu:

- seria `24` elementów trafiła do rzeczywistego kanału o pojemności `8`,
- przyjęto `8`, jawnie odrzucono `16`, a konsument przetworzył wszystkie
  przyjęte elementy,
- po opróżnieniu kanału system zapisał recovery z przyczyną
  `bounded-queue-drained`,
- stan i oś dowodowa zostały zapisane do odrębnego katalogu trwałego,
- przed restartem ustawiono próg `1337`, ręczne recovery i aktywną awarię,
- proces serwera został zatrzymany i uruchomiony ponownie na tym samym pliku
  wykonywalnym,
- po restarcie odtworzono próg, aktywną awarię oraz `2` wcześniejsze zdarzenia,
- na osi pojawiło się `durable-state-loaded`, a jawne zatwierdzenie operatora
  zakończyło recovery.

`verification/verify-running-laboratory.ps1` oraz
`verification/verify-restart-recovery.ps1` zakończyły się wynikiem `PASS`.
Drugi skrypt wykonuje rzeczywisty restart procesu, nie symuluje go wywołaniem
API.
