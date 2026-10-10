# M15.4 — niezależny dowód zgodności providera

> **Status:** VERIFIED PORTABLE CONFORMANCE
>
> **Kontrakt polecenia:** `junexis sdk test provider`

## Decyzja

Provider nie jest uznawany za zgodny tylko dlatego, że kompiluje się z
Abstractions albo posiada poprawnie wyglądający manifest. Publiczne SDK
dostarcza runner, który ładuje gotowe assembly i wywołuje je wyłącznie przez
Storage Runtime ABI.

Runner nie zależy od implementacji Hosta, kompilatora, oficjalnych providerów
ani kerneli Query/Mutation Plan.

## Fixture autora

`junexis.conformance.json` wiąże neutralny runner z minimalnym działającym
scenariuszem konkretnego providera:

- bezpieczną konfiguracją bindingu;
- operacją i wejściem próby write;
- operacją i wejściem próby read;
- oczekiwanymi wynikami w kanonicznym JSON.

Fixture nie zawiera sterownika w SDK i nie zmienia ABI. Sekrety potrzebne do
testu live muszą pochodzić z kontrolowanego środowiska, nie z publikowanej
paczki.

## Zakres dowodu 0.1

Wynik `junexis.sdk.provider-conformance/0.1` zawiera jedenaście niezależnych
kontroli:

1. załadowanie fabryki;
2. tożsamość deskryptora;
3. wersję ABI;
4. zgodność capabilities kodu i manifestu;
5. otwarcie bindingu;
6. health;
7. anulowanie;
8. klasyfikację nieznanej operacji;
9. próbę write;
10. próbę read potwierdzającą zapis;
11. zgodność deklaracji transakcji z wykonaniem.

Każda kontrola zwraca `pass/fail`, czas i opcjonalny komunikat. Kod `3`
oznacza niezgodność providera, natomiast `2` problem polecenia lub środowiska.

## Dowód fałszywej deklaracji

Bramka musi zawierać próbę negatywną. M15.4 dodaje do manifestu capability
`transactions`, którego kod nie implementuje. Poprawny runner wykrywa zarówno
rozjazd deskryptora, jak i odrzucone begin/commit/rollback.

## Granica

Profil 0.1 nie deklaruje pełnej zgodności Query Plan, Mutation Plan, Native SQL,
migracji, współbieżności i wydajności. Takie możliwości wymagają własnych
wersjonowanych fixtures i testów po ustabilizowaniu walidacji manifestu.
