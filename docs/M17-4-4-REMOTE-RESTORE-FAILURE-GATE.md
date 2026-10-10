# M17.4.4 — bramka awarii i współbieżności odtwarzania

## Zakres

Bramka integracyjna obejmuje klienta HTTP, źródło sieciowe, obszar roboczy
mechanizmu odtwarzania, trwałą pamięć podręczną, `junexis.packages.lock`,
dziennik transakcji i blokadę projektu. Nie zmienia implementacji ani semantyki
odtwarzania.

## Ochrona ostatniego poprawnego stanu

Każdy scenariusz najpierw wykonuje poprawne odtworzenie i oblicza SHA-256 nad
plikiem blokady oraz wszystkimi plikami trwałej pamięci podręcznej. Następnie pełna ścieżka
`HttpNetworkPackageSourceClient` → `NetworkPackageSource` → mechanizm odtwarzania wstrzykuje:

- przekroczenie czasu podczas żądania artefaktu;
- utratę połączenia po odebraniu części bajtów;
- anulowanie przez wywołującego.

Po błędzie SHA-256 stanu musi pozostać identyczny. Test wymaga także braku
`packages.backup`, `packages.lock.backup`, `packages.transaction.json` i
tymczasowych katalogów roboczych. Odtworzenie bez sieci potwierdza używalność stanu.

## Współbieżność procesów

Test uruchamia dwa niezależne procesy odtwarzania dla tego samego projektu. Pierwszy
proces pomocniczy testu wykonuje `RestoreAsync` i po zdobyciu blokady czeka w
kontrolowanym źródle paczek. Drugi proces wykonuje polecenie CLI i kończy się
kontrolowanym błędem konfliktu. Po zwolnieniu bramki pierwsze odtworzenie kończy
się poprawnie, pamięć podręczna i plik blokady pozostają identyczne, nie ma
pozostałości transakcji, a odtworzenie bez sieci przechodzi.

## Dziennik transakcji

Nowe scenariusze sprawdzają brak dziennika po awarii przed zatwierdzeniem. Istniejące
testy `PackageRestoreTests` pozostają bramką odzyskiwania faz `prepared` i
`committed`, wycofania pamięci podręcznej oraz pliku blokady i usuwania kopii
zapasowych po zatwierdzeniu.
