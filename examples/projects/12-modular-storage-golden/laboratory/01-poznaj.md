# Poznaj — zapisz obserwację i odzyskaj ją po restarcie

**Czas:** około 45 minut. **Poziom:** prowadzony. **Provider:** SQLite.

## Cel systemu

Dom zapisuje obserwację jakości powietrza z salonu. Po ponownym uruchomieniu
serwera panel i API mają nadal widzieć tę samą informację.

Zanim uruchomisz projekt, nazwij odpowiedzialności:

- Producer tworzy `HomeObservation`;
- Controller decyduje, kiedy ją zapisać i odczytać;
- deklaracja `HomeStore` opisuje potrzebę domeny;
- Runtime wykonuje neutralny kontrakt;
- SQLite zapewnia trwałość;
- Presenter otrzymuje `HomeSnapshot`, a nie połączenie do bazy.

## Przygotuj bezpieczną kopię

Z katalogu projektu Golden przygotuj osobny katalog roboczy:

```powershell
$stage = ./verification/prepare-m12-runtime-project.ps1 `
  -ProviderPackage <pakiet-sqlite>
```

Skrypt wypisuje ścieżkę. Nie zmienia projektu wzorcowego ani danych
użytkownika. Otwórz `junexis.project.toml` w przygotowanej kopii i znajdź
binding `storage.home`.

## Przewidź

Przed uruchomieniem zapisz odpowiedzi:

1. Czy `@report` zna ścieżkę `home.db`?
2. Który element ma prawo otworzyć provider?
3. Czy rekord powinien istnieć po zakończeniu pierwszego procesu?

## Sprawdź i uruchom

```powershell
junexis check $stage
junexis run $stage --duration 120 --no-open `
  --storage-runtime-host <Junexis.Storage.Runtime.Host.dll>
```

Launcher powinien wypisać weryfikację bindingu podobną do:

```text
VERIFIED storage · home -> sqlite@0.1.0 · sha256:...
```

Pod adres API wypisany przez serwer wyślij obserwację:

```powershell
$observation = @{
  observation_id = "lab-living-room-0001"
  room = "living-room"
  status = "air-quality-high"
  note = "wentylacja została uruchomiona"
  sequence = 1
} | ConvertTo-Json

Invoke-RestMethod -Method Post -ContentType "application/json" `
  -Body $observation -Uri "<adres-api>/api/home/observations"
Invoke-RestMethod -Uri "<adres-api>/api/home/living-room"
```

Po zakończeniu procesu uruchom tę samą kopię jeszcze raz i ponów tylko odczyt.

## Odczytaj dowód

Szukasz czterech faktów:

- zapis zwrócił `stored: true`;
- provider ma nazwę `sqlite`;
- po restarcie `restored_after_restart` ma wartość `true`;
- `latest` zawiera `lab-living-room-0001`.

To nie panel zachował dane. Panel ponownie zapytał Controller, Controller użył
tej samej abstrakcji, a nowy proces providera otworzył trwały plik SQLite.

## Punkt kontrolny

Etap jest ukończony, jeśli potrafisz narysować drogę zapisu i odczytu oraz
wyjaśnić, dlaczego ścieżka pliku bazy należy do bindingu deploymentu, a nie do
modułu domenowego ani `@report`.

[Przejdź do etapu Eksperymentuj](02-eksperymentuj.md).
