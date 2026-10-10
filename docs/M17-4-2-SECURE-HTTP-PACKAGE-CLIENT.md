# M17.4.2 — bezpieczny klient HTTP pakietów

## Zakres

`HttpNetworkPackageSourceClient` implementuje transport wymagany przez
`INetworkPackageSourceClient`. Nie zmienia manifestu projektu, algorytmu grafu,
formatu `junexis.packages.lock` ani mapowania migawki Registry.

## Polityka transportu

- produkcyjna obsługa HTTP ma wyłączone automatyczne przekierowania;
- początkowy adres i każde przekierowanie muszą używać HTTPS;
- przekierowanie nie może zmienić hosta ani portu i nie może zawierać danych użytkownika;
- klient dopuszcza najwyżej trzy przekierowania;
- pojedyncza operacja ma domyślny limit 30 sekund i respektuje anulowanie
  wywołującego;
- odpowiedzi są czytane strumieniowo przez `ResponseHeadersRead`;
- indeks ma limit 4 MiB, a artefakt 256 MiB;
- klient sprawdza zarówno deklarowany `Content-Length`, jak i faktyczną liczbę
  odebranych bajtów oraz wykrywa przesłanie krótsze od deklarowanego.

## Granice odpowiedzialności

Klient nie ponawia żądań, nie mapuje migawki Registry i nie wybiera wersji.
Mechanizm odtwarzania nadal odpowiada za obszar roboczy, SHA-256, walidację
archiwum, atomowe zatwierdzenie pamięci podręcznej i zapis pliku blokady.
Dzięki temu błąd HTTP, przekroczenie czasu, anulowanie, przerwane przesyłanie
lub przekroczenie limitu kończą się usunięciem danych roboczych.

## Weryfikacja

Testy obejmują przesyłanie strumieniowe, bezpieczne przekierowanie, zmianę hosta
lub portu, deklarowany i rzeczywisty nadmiar rozmiaru, niepełne przesłanie,
przekroczenie czasu, anulowanie wywołującego oraz brak zmian pamięci podręcznej
i pliku blokady po przerwaniu.
