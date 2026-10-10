# M19.10.1 — wybór wariantu bez zmiany tożsamości zależności

> **Status:** `VERIFIED` dla granicy wyboru w CLI; nie oznacza jeszcze
> semantycznej zgodności całej macierzy M19.10.

`junexis.packages.lock/0.1` zachowuje logiczną tożsamość pakietu jako `id` i
dokładną `version`. Nie przechowuje RID-u, adresu pobierania, architektury ani
lokalnej ścieżki artefaktu.

Fizyczny wybór korzysta z wcześniej uwierzytelnionego katalogu
`junexis.platform-release-catalog/0.2`. Kluczem wyszukiwania jest
`id + version + RID`, ale RID nie zostaje dopisany do logicznej blokady.

Brak wariantu kończy operację błędem. Zabroniony jest powrót do artefaktu dla
innego RID-u, innej wersji albo „najbliższej” architektury. Wycofany wariant i
wariant bez zweryfikowanego podpisu systemowego również są odrzucane.

Implementacja nie zmienia gramatyki ani semantyki programu `.jnx`. Jest
granicą odtwarzania zależności projektu i przygotowuje porównanie zachowania
między systemami w M19.10.2.
