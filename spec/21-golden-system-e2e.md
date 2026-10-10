# M7.10 — Golden System E2E

> **Status:** EXPERIMENTAL / VERIFIED dla M7.10

M7.10 jest pierwszym powtarzalnym dowodem, że Junexis działa jako jeden
przepływ pracy nad systemem, a nie jako zbiór niezależnych narzędzi. Jeden
projekt przechodzi przez kompilator, natywne bramki C++/Go/TypeScript, System
Bundle, podpis, politykę zaufania, dyskretną opinię Mentora, lokalny deployment
i obserwację Narrative.

## Niezmiennik tożsamości

Cały przebieg zachowuje jedną parę:

- `buildId` — tożsamość wyniku kompilacji;
- `bundleId` — tożsamość dokładnego manifestu System Bundle.

Para musi być zgodna w package receipt, deployment preflight, deployment
receipt, każdym zdarzeniu runtime oraz Diagnostic Story 1.2. Zgodność samych
plików albo samego Build ID nie wystarcza.

## Kanoniczny przebieg

`scripts/test-golden-system-e2e.ps1` wykonuje kolejno:

1. budowę CLI kompilatora, Mentor CLI i TraceCollectora;
2. `junexis package` projektu `golden-runtime-path` dla `embedded`, `server`
   i `report`, wraz z trzema rzeczywistymi bramkami natywnymi;
3. podpisanie dokładnego `manifest.json` zewnętrznym kluczem ECDSA P-256;
4. negatywną próbę dopuszczenia kopii bez sidecaru podpisu;
5. dopuszczenie oryginału przez politykę `trusted` i jawny fingerprint;
6. deployment preflight 0.2 dla `server + report` z opinią Mentora związaną z
   dokładnym Build ID i oznaczoną jako `nonBlocking`;
7. ograniczony lokalny deployment Go + TypeScript i weryfikację receipt;
8. uruchomienie artefaktów C++, Go i TypeScript z rozpakowanego dopiero po
   weryfikacji bundle;
9. zebranie pięciu zdarzeń oraz rekonstrukcję `Why` i Diagnostic Story dla
   ścieżki `Device → Service → Dashboard`;
10. opcjonalny zapis raportu `junexis.golden-system-evidence/0.1`.

Automatyczny deployment nie obejmuje flashowania `embedded`. Pakiet, podpis i
runtime evidence obejmują C++, ale fizyczny ESP32 nadal wymaga jawnego wyboru
urządzenia, osobnego potwierdzenia człowieka i dowodu sprzętowego M7.7.

## Raport dowodowy 0.1

Raport JSON zawiera co najmniej:

- status przebiegu oraz nazwę projektu;
- kanoniczne `buildId` i `bundleId`;
- status autentyczności, fingerprint i dowód odrzucenia unsigned;
- schemat package receipt i liczbę zaliczonych bramek;
- schemat oraz stan preflight, w tym status i nieblokujący charakter Mentora;
- schemat i wynik deployment receipt;
- liczbę eventów, głębokość Why, wersję Story, Narrative ID i ścieżkę targetów.

Raport opisuje konkretny przebieg. Klucz prywatny i katalog roboczy są
efemeryczne i usuwane; utrwalony raport nie jest poświadczeniem produkcyjnym.

## Uruchomienie

Z katalogu `junexis-platform`:

```powershell
pwsh -File scripts/test-golden-system-e2e.ps1 `
  -EvidenceOut .junexis/evidence/golden-system.json
```

Wymagane są .NET 10, Go, Node/npm, `g++` oraz PlatformIO. Jeśli `pio` nie jest
w `PATH`, test używa lokalnego
`../junexis-lang/.m4-platformio/Scripts/pio.exe`.

## Granica dowodu

M7.10 weryfikuje lokalny, pojedynczy przebieg wielotargetowy. Nie ustanawia
stabilności 1.0, produkcyjnego PKI/KMS, zdalnego orkiestratora, odporności
wielowęzłowej ani fizycznego flash smoke. Te granice pozostają jawne zamiast
być ukrywane przez zielony test integracyjny.
