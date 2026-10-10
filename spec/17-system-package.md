# M7.2 — Experimental System Package 0.1

> **Status:** EXPERIMENTAL / VERIFIED dla M7.2
> **Artefakt:** `junexis.system-bundle/0.1`

`junexis package` jest kontrolowanym przepływem dostarczenia systemu, a nie
synonimem kompresji katalogu `out/`. Łączy istniejące kontrakty kompilatora,
natywnych toolchainów i System Bundle w jedną operację:

```text
analysis preflight
  → native artifact gates
  → build związany z Build ID preflightu
  → materializacja runtime server + report
  → System Bundle create
  → System Bundle verify offline
```

Polecenie:

```text
junexis package [project]
  [--target embedded|server|report]
  [--out bundle.zip]
  [--go-exe path]
  [--npm-exe path]
  [--platformio-exe path]
  [--sign-key private.pem --signer name]
```

jest project-first. Katalog musi należeć do projektu z
`junexis.project.toml`. Bez `--target` używana jest dokładna lista targetów
manifestu. Opcja może być powtórzona; nieznany target jest odrzucany przed
preflightem.

## Granice faz

1. **Analysis preflight** sprawdza źródło i Structural IR, a następnie wyznacza
   Build ID z treści źródła o zakończeniach linii sprowadzonych do LF oraz
   uporządkowanej listy targetów. Fizyczna ścieżka projektu nie uczestniczy w
   tej tożsamości.
2. **Native artifact gates** generują izolowaną kopię artefaktów w katalogu
   tymczasowym, sprawdzają kontrakt każdego targetu i uruchamiają jego właściwy
   toolchain: PlatformIO, Go albo npm/TypeScript.
3. **Bound build** zapisuje trwałe artefakty tylko wtedy, gdy ponownie obliczony
   Build ID nadal zgadza się z preflightem. Zmiana źródła lub targetów podczas
   bramek kończy się błędem `B0001`. Zwykłe `build` bez wcześniejszego
   `analyze` wyznacza identyczną wartość według tego samego algorytmu.
4. **Bundle create** pakuje dokładnie opublikowany project-mode
   `latest-build`.
5. **Offline verify** ponownie sprawdza strukturę, relacje i wszystkie SHA-256
   utworzonego archiwum przed zgłoszeniem sukcesu.
6. **Package receipt** jest zapisywany atomowo obok ZIP jako
   `<bundle>.package-receipt.json`. Wiąże Bundle ID, Build ID, targety i
   zaliczone natywne bramki dla późniejszego deployment preflightu.

Dla zestawu `server + report` pomiędzy bound buildem a utworzeniem ZIP powstaje
host-native `server/runtime/junexis-server[.exe]` oraz gotowa aplikacja w
`report/runtime/`. Build serwera odbywa się w izolowanej kopii, więc `go mod
tidy` nie może zmienić plików objętych wcześniejszym inventory. Panel zawiera
już `dist/browser.js`; jego późniejsze uruchomienie nie wymaga npm, TypeScriptu
ani bundlera. Artefakty runtime otrzymują własne SHA-256 w inventory, a Bundle
ID wiąże konkretny wynik dla systemu gospodarza.

Każda faza musi zakończyć się sukcesem. Komenda nie ma opcji pominięcia bramek;
świadome pakowanie już istniejącego buildu pozostaje osobnym
`junexis bundle create`.

## Semantyka awarii

- błąd analysis lub native gate nie publikuje nowego `latest-build` i nie
  tworzy System Bundle,
- preflight snapshot jest stanem tymczasowym należącym do operacji i jest
  usuwany także po błędzie,
- błąd bound build nie tworzy System Bundle; wcześniejszy poprawny
  `latest-build`, jeśli istniał, zachowuje znaczenie ostatniego poprawnego
  buildu,
- ZIP powstaje atomowo dopiero po przejściu builda i wszystkich bramek,
- sukces jest raportowany dopiero po niezależnej weryfikacji offline, z jawnym
  Build ID, Bundle ID, listą targetów i ścieżką archiwum.

M7.2 nie jest deploymentem ani deklaracją produkcyjnej gotowości.
Potwierdza, że wygenerowany system przechodzi dostępne natywne bramki i że
utworzony System Bundle jest wewnętrznie spójny. Deployment preflight, opinia
Mentora, lifecycle i receipt wdrożenia należą do kolejnych etapów M7. Package
receipt 0.1 nie jest podpisem. M7.9 może utworzyć odłączony podpis dokładnego
`manifest.json`; jego kontrakt i polityki opisuje
[`20-system-bundle-authenticity.md`](20-system-bundle-authenticity.md).
