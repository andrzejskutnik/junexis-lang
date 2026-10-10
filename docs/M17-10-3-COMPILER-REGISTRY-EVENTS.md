# M17.10.3 — zdarzenia klienta Registry w kompilatorze

Publiczne `junexis restore --registry <adres>` tworzy jeden identyfikator
korelacji dla całej operacji. Klient HTTP przekazuje go w nagłówku
`X-Correlation-Id` przy odczycie migawki, każdej dozwolonej próbie ponowienia,
przekierowaniu oraz przesyłaniu pakietu.

## Miejsce emisji

Zdarzenia emituje `RegistrySnapshotPackageSource`, a nie surowy transport HTTP.
Ta warstwa zna już zweryfikowany `snapshotId` oraz zadeklarowane w migawce:
identyfikator, wersję i SHA-256 pakietu. Nie odtwarza więc tożsamości z adresu
URL i nie raportuje sukcesu przed walidacją kontraktu.

Dla jednego przywrócenia powstają pary:

- `snapshot-read: started/completed`;
- `artifact-download: started/completed` dla każdego potrzebnego pakietu.

Zdarzenie wyniku zawiera stan `succeeded`, `failed` albo `canceled` oraz czas w
milisekundach. Format pochodzi bezpośrednio z osadzonego kontraktu
`junexis.registry-event/0.1` z `junexis-sdk`.

## Granica prywatności

Model zdarzenia jest zamknięty. Nie ma pola na katalog projektu, manifest,
treść `.jnx`, pełny URL, token ani tekst wyjątku. Błąd jest sprowadzany do
stabilnego kodu. Kompilator nie zależy od `junexis-observability`; wewnętrzny
odbiornik używa standardowego `Trace`, dzięki czemu zewnętrzny moduł może
podłączyć konsumenta bez odwracania zależności.

Test przywracania potwierdza wspólny identyfikator czterech zdarzeń i zasiewa
unikalną lokalną ścieżkę, która nie może pojawić się w ich serializacji. Osobny
test transportu sprawdza zachowanie nagłówka także po przekierowaniu.
