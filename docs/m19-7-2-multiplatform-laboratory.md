# M19.7.2 — Laboratorium dla trzech platform

**Status:** `IMPLEMENTED`, natywnie zweryfikowano `win-x64` i `linux-x64`
(2026-09-11)

## Cel

Ten etap przenosi działający demonstrator M19.7.1 do samodzielnych paczek dla
`win-x64`, `linux-x64` i `osx-arm64`. Nie tworzy trzech odmian laboratorium.
Każda paczka niesie ten sam projekt, tryb symulacji i System Bundle, lecz zawiera
program wykonywalny właściwy dla wybranego systemu.

## Ścieżka autora

```text
junexis lab package --runtime win-x64
junexis lab package --runtime linux-x64
junexis lab package --runtime osx-arm64
```

Brak `--runtime` wybiera bieżący system. Wartość spoza zamkniętej macierzy jest
odrzucana. Nazwa punktu wejścia również wynika z RID: Windows używa pliku
`bin/junexis-laboratory.exe`, a Linux i macOS — `bin/junexis-laboratory`.

## Ścieżka uczestnika

```text
junexis lab verify <paczka>
junexis lab run <paczka>
```

`verify` może sprawdzić dowolny wariant offline. `run` wykonuje jeszcze tę samą
kontrolę integralności, po czym wymaga RID zgodnego z bieżącym systemem. Dzięki
temu błędnie pobrana paczka kończy się czytelnym błędem przed uruchomieniem
obcego programu.

Określenie „panel” używane przez polecenie oznacza wyłącznie interfejs WWW
dołączony do demonstratora inteligentnego domu. Nie jest to osobny konstrukt
języka ani ogólny komponent platformy Junexis.

## Co stanowi dowód

Skrypt `scripts/publish-m19-7-2.ps1` tworzy całą macierz przez publiczne CLI,
sprawdza każdą paczkę i zapisuje wspólny rachunek z sumami SHA-256. Przekrojowa
bramka `junexis-platform/scripts/verify-m19-7-2.ps1` ponawia kontrole, uruchamia
wariant bieżącego systemu oraz sprawdza odrzucenie obcego RID.

Kompilacja krzyżowa nie jest dowodem działania na docelowym systemie. Rachunki
Windows x64 i Linux x64 potwierdzają natywne wykonanie właściwych wariantów,
sprawdzenie całej macierzy offline oraz odrzucenie paczki dla obcego RID-u.
macOS ARM64 otrzyma taki sam status dopiero po wykonaniu bramki na tym systemie.

Na Linuksie publikacja pracowała na izolowanej kopii projektu, a `lab run`
zapisało stan w tymczasowym katalogu. Próba nie zmieniła checkoutu ani trwałego
stanu Laboratorium użytkownika. Wszystkie trzy paczki zachowały ten sam
`buildId`: `2793f4aa297ab2301e8e517da55f2ff571e11836d4d5d928ce7c4361419a1915`.

Dowody wykonania znajdują się w:

- `junexis-platform/evidence/m19-7-2/win-x64/junexis.laboratory-matrix-trial.json`;
- `junexis-platform/evidence/m19-7-2/linux-x64/junexis.laboratory-matrix-trial.json`.

## Granice etapu

M19.7.3 dodaje porównanie logicznej tożsamości, topologii i zachowania między
wykonawcami. M19.7.2 nadal nie publikuje paczek w sieci i nie zastępuje podpisu
wydania z M19.8.
