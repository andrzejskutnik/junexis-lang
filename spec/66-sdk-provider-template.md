# M15.3 — SDK prowadzi autora od działającego providera

> **Status:** VERIFIED LOCAL AUTHORING
>
> **Kontrakt polecenia:** `junexis sdk new provider`

## Cel

Pierwszy kontakt autora rozszerzenia nie może zaczynać się od ręcznego
odtwarzania ABI ani kopiowania kodu Runtime. SDK tworzy mały, działający
provider, który demonstruje lifecycle, klasyfikację błędów, read/write, health
i jawne capabilities.

## Własność

Generator i jego szablony należą do `junexis-sdk`. Kompilator języka nie
zawiera ich kopii. Główny launcher `junexis` jest punktem wejścia dystrybucji i
deleguje grupę `sdk` do komponentu SDK.

```text
junexis (launcher)
   └─ sdk new provider
          └─ Junexis.SDK.Tool
                 └─ projekt zależny od publicznego Abstractions 0.1.0
```

Samodzielny komponent jest pakowany jako `Junexis.SDK.Tool` i ma techniczny
entrypoint `junexis-sdk`. Integracja z instalatorem nie zmienia kontraktu
argumentów ani własności szablonu.

## Kontrakt wyniku

Generator przyjmuje identyfikator złożony z poprawnych segmentów przestrzeni
nazw .NET. Wynik zawiera:

- produkcyjny projekt z jednym `PackageReference` do Abstractions 0.1.0;
- fabrykę i sesję providera;
- działające `sample.write`, `sample.read` i health;
- manifest `junexis.provider/0.1` zgodny z deskryptorem fabryki;
- samodzielny smoke-test;
- solution, README, konfigurację NuGet i reguły ignorowania artefaktów.

Transakcje nie są deklarowane, ponieważ szablon ich nie implementuje. Próba
wywołania zostaje sklasyfikowana jako `constraint`. Nieznana operacja zwraca
`not-found`.

Opcjonalna odpowiedź maszynowa ma schemat
`junexis.sdk.new-provider/0.1` i zawiera ID, ścieżkę, ABI oraz capabilities.

## Ochrona kodu użytkownika

Generator nigdy nie nadpisuje istniejącej ścieżki. Najpierw materializuje
pełny wynik w katalogu tymczasowym, a następnie wykonuje atomowe przeniesienie.
Nie ma trybu force, który mógłby usunąć lub zastąpić kod dewelopera.

## Dowód

Bramka `junexis-sdk/scripts/verify-m15-3.ps1`:

1. pakuje Abstractions i SDK Tool oraz kontroluje ich sumy;
2. generuje provider przez wersjonowany kontrakt CLI;
3. potwierdza brak tokenów i zależności źródłowych;
4. sprawdza niezmienność plików po odrzuconej próbie ponownego generowania;
5. wykonuje zapis, odczyt oraz health;
6. pakuje provider i sprawdza manifest oraz README wewnątrz `.nupkg`.

## Granica

Smoke-test dowodzi poprawności szablonu, a nie pełnej zgodności dowolnego
providera. Przenośny Conformance Kit i polecenie
`junexis sdk test provider` należą do M15.4.
