# M15.9 — jeden dowód planów dla rzeczywistych dostawców

M15.9 potwierdza, że publiczny profil SDK z M15.7 nie jest wyłącznie
demonstracją dla wygenerowanego dostawcy. Memory, SQLite i PostgreSQL mają
identyczny scenariusz testowy wykonywany w jednej sesji:

```text
odczyt: brak
    ↓
mutacja: wstawienie
    ↓
odczyt: dokładnie jedna oczekiwana wartość
    ↓
mutacja: usunięcie
    ↓
odczyt: ponownie brak
```

Profil obserwuje tylko neutralne operacje `query.plan` i `mutation.plan` oraz
ich wersjonowane wyniki. Nie zależy od generatora `@server`, implementacji
sterownika ani fizycznego SQL. Dzięki temu to samo wymaganie może spełnić
dostawca napisany w innym języku środowiska wykonawczego.

`${run:id}` i `${run:entity}` są wartościami sesji testu zgodności. Zapewniają
unikalny stan bez ukrytej zależności od pustej bazy, a końcowa mutacja usuwa
rekord próby. Memory i SQLite są sprawdzane dwoma kolejnymi przebiegami.
PostgreSQL korzysta z tego samego profilu, lecz brak jawnego ciągu połączenia
oznacza `SKIPPED` (pominięto), a nie dowód wykonania na rzeczywistej bazie.

M15.9 dowodzi przenośnego zachowania planów na rzeczywistych implementacjach.
Nie dowodzi wydajności, wszystkich typów i operatorów, migracji schematu ani
produkcyjnej gotowości dowolnej konfiguracji bazy.
