# M7.3 — Deployment Preflight 0.1

> **Status:** EXPERIMENTAL / VERIFIED dla M7.3
> **Identyfikator:** `junexis.deployment-preflight/0.1`

Deployment preflight odpowiada na dwa różne pytania i nie miesza ich
odpowiedzialności:

1. **Czy pakiet ma techniczne dowody pozwalające rozpocząć deployment?**
2. **Co Mentor sądzi o ryzykach architektonicznych tego dokładnego buildu?**

Pierwsza odpowiedź jest bramką. Druga jest jawnie oznaczoną, nieblokującą
opinią.

## Polecenie

```text
junexis deploy preflight <bundle.zip>
  [--receipt <package-receipt.json>]
  [--target embedded|server|report]
  [--mentor-exe <path>]
  [--signature <signature.json>]
  [--auth-policy permissive|signed|trusted]
  [--trusted-signer <sha256-fingerprint>]
  [--report-out <report.json>]
  [--json]
```

Bez `--receipt` CLI szuka `<bundle.zip>.package-receipt.json`. Bez
`--target` sprawdza wszystkie targety bundle. Mentor jest odkrywany przez
`--mentor-exe`, `JUNEXIS_MENTOR_CLI` albo `PATH`; jego brak nie zmienia
technicznego statusu.

## Dowód pakowania

M7.3 uzupełnia `junexis package` o atomowy sidecar:

```text
<bundle.zip>.package-receipt.json
schema: junexis.package-receipt/0.1
```

Receipt wiąże:

- Bundle ID,
- Build ID,
- uporządkowane targety,
- dokładnie jedną zaliczoną natywną bramkę i właściwy toolchain per target.

Zwykłe `bundle create` świadomie nie tworzy receiptu, ponieważ nie uruchamia
natywnych bramek. Deployment preflight bez pasującego receiptu ma status
`blocked`.

Receipt jest dowodem przepływu lokalnego CLI i spójności identyfikatorów, nie
podpisem ani łańcuchem zaufania. M7.9 dodaje oddzielny podpis manifestu i jawną
politykę dopuszczenia opisaną w
[`20-system-bundle-authenticity.md`](20-system-bundle-authenticity.md).

## Techniczna bramka

Raport sprawdza:

1. pełną integralność System Bundle offline,
2. zgodność package receipt z Bundle ID, Build ID i targetami,
3. obecność zaliczonej natywnej bramki wybranego targetu,
4. minimalny zestaw artefaktów wykonawczych targetu; dla nowej paczki
   `server + report` są to natywne binarium serwera i statyczny bundle panelu,
5. dostępność obsługi deploymentu w aktualnym etapie.

`server` i `report` mogą osiągnąć `ready`. `embedded` pozostaje `blocked`
kodem `DP1005` do chwili wdrożenia bezpiecznego wyboru urządzenia i flash w
M7.7. Brak jawnego `deployment-plan.json` jest informacją `DP2001`, ponieważ
executor M7.4 używa jawnie udokumentowanych target-local defaults.

Raport rozróżnia `passed`, `blocked` i `info`. Wyłącznie obecność
technicznego `blocked` ustala kod zakończenia procesu równy 1.

## Dyskretna opinia Mentora

Po weryfikacji ZIP CLI odczytuje checksummed
`artifacts/analysis/junexis.analysis.json`, przekazuje tymczasową kopię do
Mentor CLI i akceptuje opinię tylko wtedy, gdy:

- envelope Mentora ma wspierany kontrakt 1.x,
- analiza zakończyła się sukcesem,
- Mentor zwrócił dokładny Build ID bundle.

Status opinii może być `reviewed`, `stale`, `unavailable` albo
`not-configured`. Każdy wariant ma `nonBlocking: true`. Nieaktualne findings
są odrzucane. W trybie tekstowym preflight pokazuje najwyżej trzy opinie
`Error`/`Warning`; pełna lista pozostaje w JSON.

Mentor nie może zmienić `technicalStatus`, kodu zakończenia, receiptu ani
artefaktów. Nawet finding Mentora `Error` nie jest błędem deploymentu.

## Granice bezpieczeństwa

- preflight nie rozpakowuje całego bundle i niczego nie uruchamia z ZIP,
- Analysis Snapshot jest ponownie sprawdzany SHA-256 przed przekazaniem,
- proces Mentora ma limit czasu 15 sekund i ograniczony wynik,
- raport JSON jest zapisywany atomowo i ma limit 4 MiB,
- preflight nie uruchamia kontenerów, procesów systemu ani flashowania,
- M7.3 nie jest jeszcze deploymentem; side effects rozpoczynają się dopiero w
  kolejnych etapach.
