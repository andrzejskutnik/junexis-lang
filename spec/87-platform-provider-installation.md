# M16.9 — instalacja i rozwiązywanie dostawców

## Decyzja

Dostawca Storage Runtime jest instalowany niezależnie od generatora platformy.
Publiczna tożsamość wyboru składa się z:

1. identyfikatora dostawcy, na przykład `postgresql`;
2. wersji implementacji dostawcy;
3. dokładnej wersji paczki SDK;
4. kontraktu `junexis.provider/0.2`;
5. ABI `junexis.storage.runtime/0.1`.

Rejestr `junexis.provider-registry/0.1` jest lokalnym katalogiem dostępnych
implementacji. Generator nie zakłada języka, w którym napisano adapter. Pyta o
tożsamość i wersję SDK, a brak dokładnego dopasowania jest błędem, nie powodem
do cichego użycia przypadkowej wersji.

## Operacje

- `junexis provider install` — waliduje M16.8 i atomowo instaluje paczkę;
- `junexis provider list` — pokazuje wszystkie lokalne implementacje;
- `junexis provider resolve` — zwraca katalog dokładnie zgodnej implementacji;
- `junexis provider uninstall` — usuwa tylko wskazaną rodzinę zgodności.

Rejestr nie przechowuje sekretów ani konfiguracji połączeń. Te wartości należą
do uruchamianego systemu i kontraktu wdrożenia.

## Granica

M16.9 ustanawia lokalne wykrywanie na Windows x64. Nie publikuje paczek w sieci,
nie wybiera dostawcy bez deklaracji projektu i nie definiuje adapterów dla
kolejnych języków wykonawczych. Powiązanie rejestru z manifestem projektu
realizuje M16.10.
