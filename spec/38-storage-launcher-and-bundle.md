# M12.8 — zweryfikowany launcher storage i samodzielny System Bundle

> **Status:** VERIFIED
>
> **Deployment manifest:** `junexis.storage.deployment/0.1`

M12.8 łączy lockfile, neutralnego klienta M12.7 i paczki providerów w jeden
bezpieczny rytuał uruchomienia. Użytkownik nie ustawia już ręcznie entrypointu
providera ani JSON-u konfiguracji, gdy korzysta z `junexis run`.

```text
junexis run
    → build projektu
    → ponowne obliczenie SHA-256 pakietu
    → dokładne porównanie z junexis.providers.lock/0.1
    → rozwiązanie literal/env/secret
    → uruchomienie serwera z neutralnym klientem
    → osobny Storage Runtime Host
    → zweryfikowany provider
```

## Uruchomienie projektu

Projekt wskazuje pakiet i binding w `junexis.project.toml`. Host przekazuje się
jawnie:

```powershell
junexis run . `
  --storage-runtime-host ./runtime/Junexis.Storage.Runtime.Host.dll
```

Alternatywnie deployment może ustawić `JUNEXIS_STORAGE_RUNTIME_HOST`. Launcher:

1. wymaga udanego bound builda,
2. ponownie rozwiązuje manifest i wszystkie deklarowane pliki pakietu,
3. porównuje kanoniczny lockfile z plikiem w `out/debug`,
4. odrzuca zmianę pakietu wykonaną po buildzie,
5. rozwiązuje `env:` i `secret:` dopiero przed startem,
6. nie zapisuje ani nie wypisuje rozwiązanych wartości,
7. przekazuje procesowi ID bindingu, providera i zweryfikowany entrypoint.

Baseline obsługuje dokładnie jeden binding storage na proces generowanego
serwera. To jawne ograniczenie, a nie automatyczny wybór pierwszego bindingu.

## System Bundle

`junexis package` przyjmuje tę samą opcję `--storage-runtime-host`. Dla projektu
z bindingiem umieszcza w paczce:

```text
artifacts/server/runtime/storage/
├── host/
├── providers/<provider-id>/
└── junexis.storage.deployment.json
```

Host, entrypoint, zależności providera, pliki natywne i manifest deploymentu są
częścią inventory oraz integralności System Bundle. Manifest zachowuje ID,
wersję, package SHA-256 i źródła konfiguracji. Nie zawiera rozwiązanych
sekretów. Wartości zależne od miejsca wdrożenia są dostarczane przy starcie.

## Dowód Golden

Wykonany Golden Modular Storage System Bundle zawierał targety `server` i
`report`, 50 wpisów inventory oraz 52 zweryfikowane pliki payloadu. Z rozpakowanej
paczki uruchomiono serwer, panel, host oraz SQLite. Zapisana obserwacja została
odtworzona po restarcie procesu. Wynik: `PASS`, bez checkoutu oraz bez Go, C/C++
i npm w środowisku wykonawczym.

## Granice etapu

- launcher nie pobiera providerów z rejestru,
- zaufanie do wydawcy wymaga podpisu paczki; SHA-256 dowodzi integralności,
- wiele bindingów na jeden proces pozostaje kolejnym rozszerzeniem,
- PostgreSQL live nadal wymaga dostępnego serwera testowego i sekretu.
