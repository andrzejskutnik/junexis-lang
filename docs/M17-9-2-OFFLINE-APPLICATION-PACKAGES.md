# M17.9.2 — pakiety aplikacyjne bez połączenia z Registry

## Wynik

Udane przywrócenie pakietów z Registry utrwala teraz trzy wzajemnie związane
elementy:

1. `junexis.packages.lock` — dokładny graf pakietów projektu;
2. `.junexis/cache/packages/registry-snapshot.json` — pełny, zweryfikowany
   migawka `junexis.registry-snapshot/0.1`;
3. `.junexis/cache/packages/registry-offline-snapshot.json` — rachunek
   `junexis.registry-offline-snapshot/0.1` zawartości dostępnej lokalnie.

Pliki migawki powstają w obszarze tymczasowym razem z paczkami. Dopiero po
sprawdzeniu rozmiarów, SHA-256, manifestów, zależności i zgodności cały katalog
pamięć podręczna oraz plik blokady są zatwierdzane przez istniejącą transakcję. Błąd nie może
pozostawić połowy nowego stanu.

## Co oznacza `--offline`

`junexis restore --offline` nie wybiera wersji i nie przyjmuje adresu Registry.
Czyta wyłącznie plik blokady, obie lokalne migawki oraz pliki pamięci podręcznej. Dla każdego
pakietu sprawdza:

- obecność dokładnej tożsamości w pliku blokady i źródłowej migawce;
- zgodność przestrzeni nazw, źródła, zależności i skrótów;
- zgodność rachunku pamięci podręcznej z rozmiarem zapisanym przez Registry;
- rzeczywisty rozmiar i SHA-256 archiwum, manifestu oraz plików modułu.

Polecenia `junexis check` i `junexis build` korzystają z tej samej ścieżki
walidacji pamięci podręcznej podczas rozwiązywania projektu. Nie mają ukrytego klienta
Registry ani mechanizmu dociągania brakującego pakietu.

Brak lub uszkodzenie wymaganej zawartości kończy się diagnostyką. Użytkownik
przywraca pamięć podręczną jawnie po odzyskaniu połączenia:

```powershell
junexis restore --registry https://registry.example.org
```

## Zgodność wsteczna

Istniejący lokalny `packageIndex` nadal może przygotować pamięć podręczną bez migawki
Registry. Gdy jednak którykolwiek z dwóch plików migawki jest obecny, drugi
jest obowiązkowy i cały zestaw musi przejść walidację M17.9.1. Tryb lokalnego
indeksu pozostaje narzędziem rozwoju; publiczna ścieżka Registry otrzymuje
silniejsze dowody działania bez sieci.

## Dowód

`PackageRestoreTests` sprawdza rzeczywistą sekwencję:

```text
odtworzenie z sieci → usunięcie źródła Registry → odtworzenie bez sieci → sprawdzenie → budowanie
```

Podstawione źródło pakietów zgłasza liczbę wywołań równą zero. Osobny przypadek
zmienia rozmiar w lokalnym rachunku i potwierdza kontrolowane odrzucenie bez
próby użycia źródła.
