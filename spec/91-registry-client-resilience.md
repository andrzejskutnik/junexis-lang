# M17.8.2 — odporność klienta Registry w Kompilatorze

Status: **VERIFIED**  
Kontrakt nadrzędny: `junexis.registry-http-policy/0.1`

## Cel

Kompilator stosuje wspólną politykę HTTP SDK podczas pobierania snapshotu i
pakietów aplikacyjnych. Awaria sieci nie może pozostawić częściowego artefaktu,
zmienić ostatniego poprawnego `junexis.packages.lock` ani uszkodzić pamięci
podręcznej projektu.

## Zachowanie klienta

- jedna operacja ma najwyżej cztery próby, 60 sekund na próbę i 180 sekund na
  całość;
- ponawiane są tylko żądania `GET`: `429`, wszystkie `5xx`, błąd połączenia,
  timeout i przerwany albo skrócony transfer;
- `404`, `409`, `413`, błędne przekierowanie i przekroczenie rozmiaru kończą
  operację bez ponowienia;
- `Retry-After` ma pierwszeństwo przed lokalnym opóźnieniem, ale podlega
  granicy 120 sekund i budżetowi całej operacji;
- anulowanie użytkownika natychmiast przerywa oczekiwanie i nie uruchamia
  następnej próby;
- klient dopuszcza najwyżej pięć przekierowań HTTPS w obrębie tej samej nazwy
  serwera i portu. Nie przekazuje żądania do obcego źródła adresu.

## Bezpieczne ponowienie artefaktu

Pakiet jest pobierany do pliku stagingowego objętego limitem rozmiaru. Jeżeli
transfer zostanie przerwany, klient przed kolejną próbą przywraca długość i
pozycję strumienia sprzed pierwszej próby. Kolejne bajty nie są dopisywane do
uszkodzonego prefiksu.

Jeśli strumienia docelowego nie można bezpiecznie przewinąć, klient nie ponawia
transferu i zwraca kontrolowany błąd. Dopiero kompletny pakiet przechodzi
weryfikację rozmiaru, SHA-256, manifestu oraz transakcyjną promocję.

## Macierz awarii

| Przypadek | Ponowienie | Stan po wyniku |
|---|---:|---|
| `404` | nie | poprzednia pamięć i plik blokady bez zmian |
| `429` + `Retry-After` | tak | sukces albo stabilne `rate-limited` |
| `500–599` | tak | sukces albo stabilne `registry-unavailable` |
| timeout | tak w budżecie | kontrolowany timeout, bez częściowego stanu |
| utrata połączenia | tak | staging cofnięty przed próbą |
| przerwany/skrócony transfer | tak | staging cofnięty przed próbą |
| przekroczenie rozmiaru | nie | brak promocji artefaktu |
| anulowanie | nie | natychmiastowy koniec bez mutacji |
| równoległy `restore` | nie dotyczy | drugi proces odrzucony przez blokadę |

Testy integracyjne rozpoczynają część przypadków od istniejącego, poprawnego
stanu i porównują jego SHA-256 po awarii. Następnie wykonują `restore --offline`,
aby dowieść, że poprzedni stan nadal jest kompletny i używalny.

## Weryfikacja

```powershell
../junexis-platform/scripts/verify-m17-8-2.ps1
```

Bramka najpierw sprawdza kontrakt M17.8.1, następnie klienta HTTP i macierz
transakcyjnego `restore`. Pełny zestaw testów Kompilatora pozostaje końcową
bramką regresji.
