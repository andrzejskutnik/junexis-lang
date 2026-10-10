# M7.4 — Local Deployment 0.1

> **Status:** EXPERIMENTAL / VERIFIED dla M7.4

M7.4 jest pierwszym kontrolowanym side effectem w łańcuchu dostarczania
systemu Junexis. Nie wdraża do chmury ani na urządzenie. Uruchamia targety
`server` i `report` jako ograniczoną czasowo, foregroundową sesję lokalną,
wyłącznie na podstawie wcześniej zweryfikowanych dowodów.

## Łańcuch wejściowy

```text
System Bundle 0.1
  + Package Receipt 0.1
  + Deployment Preflight 0.1 (technicalStatus = ready)
  → selective extraction
  → użycie gotowego runtime albo legacy native build
  → readiness
  → bounded run
  → stop owned processes
```

`junexis deploy local <bundle> --preflight <report>`:

1. ponownie weryfikuje integralność całego bundle offline;
2. wymaga package receipt z tym samym Bundle ID, Build ID, targetami i
   zaliczonymi bramkami natywnymi;
3. parsuje ograniczony do 4 MiB raport preflight i wymaga dokładnego Bundle ID
   oraz Build ID;
4. pozwala uruchomić tylko target objęty raportem preflight;
5. odrzuca `embedded` przed ekstrakcją lub wywołaniem toolchainu;
6. wypakowuje wyłącznie artefakty wybranych targetów do nowego lub pustego
   workspace i ponownie liczy ich SHA-256;
7. dla paczki z gotowym runtime uruchamia zweryfikowane binarium serwera oraz
   statyczny panel bez Go, npm, TypeScriptu i bundlera; starszy Bundle 0.1 może
   nadal przejść kompatybilną ścieżkę `go build` i `npm install`;
8. uruchamia procesy na dynamicznych portach loopback i czeka na
   `/health/ready` serwera oraz `/health/ready` aplikacji raportowej; legacy
   raport Node zachowuje marker `junexis.local-runtime/0.1`;
9. po `--duration` (1–300 s) zatrzymuje w odwrotnej kolejności całe drzewa
   procesów, które sama utworzyła.

## Bootstrap targetów

Generator Go publikuje konfigurowalne `--http-port`, `--health-port` i
`--tcp-port`. Generator TypeScript publikuje `report/deploy.ts`; entrypoint
uruchamia zadania bez parametrów lub z uchwytami komunikacyjnymi, raportuje
readiness z Build ID i pozostaje aktywny do sygnału zakończenia. Zadania
wymagające wartości domenowych nie otrzymują wymyślonych argumentów — są
jawnie liczone jako `skippedTasks`.

Adres bridge raportu pochodzi z `JUNEXIS_SERVER_URL`, z zachowaniem lokalnego
defaultu `http://localhost:8080` poza executorem M7.4.

Każdy uruchomiony target otrzymuje wspólną tożsamość Narrative:
`JUNEXIS_BUILD_ID=<verified build>` oraz
`JUNEXIS_BUNDLE_ID=<verified System Bundle>`. Runtime publikuje je jako
`build_id` i `bundle_id`; proces `server` i `report` nie mogą otrzymać różnych
wartości dla tej samej sesji deploymentowej.

## Workspace i diagnostyka

Bez `--work-dir` executor tworzy własny katalog o prefiksie
`junexis-local-deploy-` w systemowym katalogu tymczasowym i usuwa go po
zatrzymaniu procesów. Jawny `--work-dir` musi być nowy albo pusty i jest
zachowywany, aby można było zbadać wypakowane binarium, statyczny panel oraz
dowody sesji. `node_modules` pojawia się wyłącznie dla starszej paczki
wymagającej kompatybilnego rebuilda.

Build i readiness mają ograniczone timeouty. Niezerowy exit przed końcem sesji
jest błędem. Executor nie wyszukuje ani nie zatrzymuje procesów, których sam
nie utworzył.

## Granice

- wynik Mentora został związany z Build ID w M7.3 i pozostaje opinią
  nieblokującą; M7.4 nie wywołuje Mentora ponownie po rozpoczęciu side effectu;
- M7.4 nie jest daemonem, supervisorem ani mechanizmem restartu;
- nie powstaje trwały deployment receipt ani historia instancji — to zakres
  M7.6;
- nie ma zdalnych hostów, sekretów, TLS, rollbacku ani podpisu;
- `embedded` pozostaje zablokowany do przepływu wyboru urządzenia i flashowania
  w M7.7;
- package receipt i preflight nie są podpisane; autentyczność jest zakresem
  M7.9.
