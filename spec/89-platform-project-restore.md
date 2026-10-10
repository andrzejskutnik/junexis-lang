# M16.11 — odtwarzanie zależności projektu

## Decyzja

Publiczne polecenie `junexis restore` materializuje wymagania zapisane przez
projekt. Instalator jest właścicielem komendy i koordynuje prywatny resolver
pakietów aplikacyjnych kompilatora oraz resolver dostawców. Źródłem prawdy są
`packages`, `packageIndex`, `providerSdkVersion` oraz powiązania `[storage.*]`
w `junexis.project.toml`; polecenie nie tworzy drugiej listy zależności.

Dopasowanie identyfikatora dostawcy, jego wersji i wersji SDK jest dokładne.
Nie wolno wybierać najnowszej ani podobnej paczki. Paczka przed instalacją
przechodzi kontrolę rachunku `junexis.provider.release/0.1`, manifestu, ABI,
zamkniętego inwentarza oraz skrótów SHA-256.

## Powtarzalność i wycofanie

Ponowne odtworzenie kompletnego projektu nie zmienia profilu. Gdy operacja
obejmuje kilka brakujących dostawców i jedna instalacja zawiedzie, polecenie
usuwa tylko paczki dodane w bieżącej operacji. Nie narusza dostawców, którzy
byli dostępni przed jej rozpoczęciem.

## Granica

M16.11 korzysta z jawnego lokalnego katalogu wydania podanego przez
`--local-packages`, gdy projekt wymaga dostawców. Pakiety aplikacyjne korzystają
z lokalnego indeksu projektu, a `--offline` ogranicza je do lockfile i cache.
Automatyczne pobieranie providerów, publiczny rejestr i polityka zaufania dla
źródła sieciowego pozostają poza tym etapem.
