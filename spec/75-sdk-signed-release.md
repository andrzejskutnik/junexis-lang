# M15.12 — podpisany zestaw wydania SDK

M15.12 dodaje lokalny kontrakt autentyczności zestawu wydania SDK. Po
odtwarzalnym procesie budowania M15.11 narzędzie podpisuje kluczem prywatnym
ECDSA P-256 dokładne sumy manifestu `junexis.sdk.json` i informacji o
pochodzeniu `junexis.sdk.provenance.json`.

Dokument `junexis.sdk.signature.json` ma wersję
`junexis.sdk.signature/0.1` i zawiera wersję wydania, identyfikator klucza,
algorytm, nazwy podpisanych plików, ich SHA-256 oraz podpis DER zakodowany jako
Base64. Klucz prywatny nie jest częścią zestawu.

```text
junexis sdk sign release <katalog> --private-key <plik.pem> --key-id <id>
junexis sdk verify signature <katalog> --public-key <plik.pem> --key-id <id>
```

Weryfikacja używa jawnie wskazanego klucza publicznego. Zmieniony manifest,
zmieniona informacja o pochodzeniu, obcy klucz lub inny identyfikator dają
status `invalid` i kod `8`. Podpis potwierdza posiadanie klucza dla konkretnych
bajtów; nie zastępuje kontroli kompletności wydania ani semantycznej zgodności
SDK.

Pełny rytuał odbioru składa się z `verify release`, `verify provenance` i
`verify signature`. Bramka M15.12 wykonuje drogę poprawną oraz próby negatywne
bez umieszczania klucza prywatnego w wydaniu.

Etap nie ustanawia oficjalnego klucza projektu, sposobu jego bezpiecznego
przechowywania, rotacji, unieważniania, publicznego źródła zaufania ani
znacznika czasu. Nie oznacza więc jeszcze gotowej publicznej dystrybucji.
