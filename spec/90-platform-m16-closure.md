# M16.12 — zamknięcie dystrybucji platformy

## Decyzja

M16 jest zamknięty dla lokalnego kandydata Windows x64. Zamknięcie oznacza, że
manifest wydania, katalog zgodności, Installer, prywatny compiler, Studio, LSP,
VSIX, Runtime, SDK i zestawy providerów tworzą jeden zbiorczo weryfikowany
zestaw. Nie oznacza stabilności języka ani publicznej dostępności kanału.

Publiczne `junexis restore` jest jedynym wejściem odtwarzania projektu.
Installer koordynuje wyspecjalizowany resolver pakietów aplikacyjnych compilera
i resolver providerów. Zestaw Studio zawiera publiczne wejście Installera,
deleguje do niego przez `IProcessService` i nie implementuje żadnego z
resolverów.

## Dowód

Meta-repozytorium `junexis-platform` jest właścicielem bramki
`scripts/verify-m16.ps1`. Bramka wykonuje etapy M16.1–M16.11 i sprawdza komplet
dokumentów. M16.11 dowodzi restore pakietów aplikacyjnych online i offline,
deterministycznego lockfile, dokładnych wersji providerów i idempotentnego
ponowienia przez rzeczywisty zestaw wydania.

## Granica

Bieżące wydanie zachowuje stan `local-candidate`. Publiczne Registry, sieciowe
źródło providerów, oficjalny podpis całego wydania, trwałe adresy pobierania,
Marketplace, automatyczna aktualizacja oraz dystrybucje macOS i Linux pozostają
poza M16.

Pakiety aplikacyjne i profil providerów zachowują osobne granice transakcji.
Koordynator zapewnia bezpieczną kolejność oraz idempotentne ponowienie, lecz nie
deklaruje atomowości obejmującej oba magazyny.
