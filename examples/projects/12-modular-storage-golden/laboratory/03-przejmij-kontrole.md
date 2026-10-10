# Przejmij kontrolę — integralność, sekrety i paczka wykonawcza

**Czas:** około 90 minut. **Poziom:** pełna kontrola deploymentu.

Masz już działający system i rozumiesz różnicę providerów. Teraz odpowiadasz
za pytanie: **co dokładnie zostanie uruchomione i skąd można to udowodnić?**

## 1. Przeczytaj binding i lockfile

W przygotowanym wariancie SQLite wykonaj:

```powershell
junexis build $stage
Get-Content "$stage/out/debug/junexis.providers.lock.json"
```

Połącz każdy wpis z decyzją:

| Pole | Czego dowodzi |
|---|---|
| provider i version | wybranej implementacji |
| runtime ABI | zgodności z hostem |
| package SHA-256 | integralności całej paczki |
| files i entrypoint | dokładnego kodu dopuszczonego do uruchomienia |
| configuration source | pochodzenia wartości bez ujawniania sekretu |

## 2. Sprawdź bramkę integralności

Pracujesz w kopii utworzonej przez skrypt, więc możesz bezpiecznie zmienić jej
manifest providera. Po udanym buildzie dopisz komentarz:

```powershell
Add-Content "$stage/providers/sqlite/junexis.provider.toml" "# kontrolowana zmiana laboratorium"
junexis run $stage --duration 5 --no-open `
  --storage-runtime-host <Junexis.Storage.Runtime.Host.dll>
```

Launcher ma odmówić startu, ponieważ aktualny hash nie odpowiada lockfile.
Nie jest to awaria domu. To świadomie wywołana próba uruchomienia innej paczki
niż zatwierdzona przez build.

Usuń kopię i przygotuj nowy stage. Nie naprawiaj dowodu przez ręczną edycję
lockfile.

## 3. Oddziel sekret od projektu

Provider PostgreSQL deklaruje `connectionString` jako sekret:

```toml
config.connectionString = "secret:JUNEXIS_STORAGE_CONNECTION_STRING"
```

Wartość nie może znaleźć się w `.jnx`, bindingu, lockfile ani System Bundle.
Launcher pobiera ją ze środowiska dopiero przed startem. Bez testowego serwera
PostgreSQL przeczytaj i zweryfikuj kontrakt; nie udawaj udanego połączenia.

## 4. Zbuduj samodzielny System Bundle

Na świeżym wariancie SQLite wykonaj:

```powershell
junexis package $stage `
  --storage-runtime-host <Junexis.Storage.Runtime.Host.dll>
```

Rozpakuj kopię paczki i znajdź:

```text
artifacts/server/runtime/storage/
├── host/
├── providers/sqlite/
└── junexis.storage.deployment.json
```

Manifest deploymentu ma wskazywać względne ścieżki hosta i providera oraz
źródła konfiguracji. Nie może zawierać rozwiązanych sekretów.

## 5. Uruchom dowód bez checkoutu

```powershell
./verification/verify-system-bundle.ps1 `
  -Bundle "$stage/out/system/<nazwa>.system-bundle.zip"
```

Próba kończy się `PASS`, jeśli uruchomi serwer i panel z rozpakowanej paczki,
zapisze obserwację, zrestartuje serwer i odtworzy rekord bez kompilatora i
checkoutu źródeł.

## Punkt kontrolny

Etap jest ukończony, jeśli potrafisz:

1. wskazać różnicę między manifestem providera, lockfile i manifestem deploymentu;
2. udowodnić, że zmieniona paczka nie została uruchomiona;
3. wskazać moment rozwiązania sekretu;
4. znaleźć host i provider w inventory System Bundle;
5. uruchomić dowód z paczki bez drzewa źródeł.

Pełna kontrola nie oznacza ręcznego obchodzenia zabezpieczeń. Oznacza, że
wszystkie decyzje są jawne, sprawdzalne i należą do ciebie.
