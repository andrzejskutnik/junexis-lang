# Junexis Lang — Filozofia języka

> **Status:** FOUNDATIONAL  
> **Wersja języka:** 0.2.0 Experimental  
> **Rola:** fundament decyzji projektowych, nie deklaracja stanu implementacji

---

## Po co istnieje Junexis

Junexis jest językiem do opisywania znaczących systemów rozproszonych: systemów,
których części działają w różnych miejscach, komunikują się ze sobą i wpływają
na rzeczywisty świat.

Twórca powinien móc opisać znaczenie, relacje i odpowiedzialności systemu bez
ukrywania ich pod przypadkową złożonością transportu, serializacji, integracji
i rozmieszczenia. Kompilator bierze na siebie techniczną realizację tego opisu,
ale nie może zacierać podjętych decyzji ani ich konsekwencji.

> **Twórca opisuje znaczenie, relacje i odpowiedzialności. Kompilator bierze na
> siebie przypadkową złożoność ich technicznej realizacji. System powinien nie
> tylko działać, lecz także potrafić wyjaśnić, co zrobił i dlaczego.**

Junexis nie ma usuwać odpowiedzialności twórcy. Ma sprawić, aby odpowiedzialność
była widoczna, możliwa do sprawdzenia i wyrażona w samym modelu systemu.

## Podstawowe pytania

Konstrukcje języka odpowiadają na odrębne pytania dotyczące tworzonego systemu:

| Konstrukcja | Pytanie | Odpowiedzialność |
|-------------|---------|------------------|
| `struct` | Co istnieje? | Definiuje dane i kontrakty wymieniane przez system. |
| `task` | Co działa i po co? | Definiuje zachowanie oraz odpowiedzialność obliczeniową. |
| `device` | W czym działanie zostaje ucieleśnione? | Opisuje sprzęt, jego możliwości i ograniczenia. |
| `resource` | Z jakiego stanu lub I/O korzysta działanie? | Opisuje wymaganą zdolność przechowywania lub dostępu do I/O, niezależnie od dostawcy. |
| `pipeline` | Dokąd prowadzą skutki działania? | Pokazuje logiczny kierunek przepływu danych. |
| `deployment` | Gdzie system istnieje naprawdę? | Przypisuje konkretne instancje zadań do miejsc wykonania. |
| `protocol` | Jak porozumiewają się odległe części? | Definiuje sposób przekraczania granic platform i sieci. |
| `narrative` | Jak system pamięta i wyjaśnia własne zachowanie? | Określa obserwowalność, przyczynowość i kontekst wyjaśnień. |
| `extern task` | Jak system korzysta z działania spoza języka? | Wyznacza kontrolowaną granicę z zewnętrzną implementacją. |
| `extern type` | Jak system rozpoznaje byt, którego wnętrza nie zna? | Reprezentuje zewnętrzny, nieprzezroczysty typ. |

Każda konstrukcja ma własną odpowiedzialność. Jeśli nowy element języka próbuje
odpowiadać na kilka z tych pytań jednocześnie, wymaga ponownego namysłu albo
wyraźnego uzasadnienia.

`resource` jest przyjętą odpowiedzialnością projektową, ale jego składnia i model
wykonawczy pozostają eksperymentalne (D17). Nie należy z samej obecności w tej
tabeli wnioskować, że deklaracja jest już obsługiwana przez gramatykę lub kompilator.

## Relacje są częścią znaczenia

W systemie rozproszonym komunikacja nie jest szczegółem biblioteki. Wybór
rodzaju relacji określa, co system obiecuje zachować:

| Typ | Co wyraża |
|-----|-----------|
| `chan<T>` | Bezpośrednią wymianę z kontrolą przepływu i jawnym oczekiwaniem na drugą stronę. |
| `queue<T>` | Trwałość historii i konieczność ponowienia, gdy przetwarzanie się nie powiedzie. |
| `stream<T>` | Ciągłą obserwację, w której ważniejszy może być aktualny stan niż każdy stan pośredni. |

Dokładne gwarancje zależą od runtime'u, protokołu i granicy awarii. Język nie
powinien obiecywać więcej, niż wygenerowany system potrafi udowodnić lub
rzeczywiście zapewnić.

## Pierwszy model mentalny: Producer–Controller–Presenter

Producer–Controller–Presenter (PCP) jest zalecanym pierwszym modelem mentalnym
dla dużej klasy systemów Junexis:

```text
Producer              Controller                 Presenter
@embedded      →         @server         →          @report
dane                  decyzje i stan             obraz systemu
```

- Producer opisuje, co się wydarzyło, i publikuje typowane obserwacje,
- Controller rozstrzyga, co dane znaczą, utrzymuje źródło prawdy i wykonuje
  dozwolone skutki,
- Presenter buduje obraz dla człowieka i przekazuje jego typowane intencje do
  Controllera.

W uproszczonym przepływie storage Controller jest właścicielem bazy. Presenter
nie zmienia źródła prawdy bezpośrednio, lecz wysyła intencję, którą Controller
sprawdza, wykonuje i utrwala.

PCP nie jest nową konstrukcją języka ani obowiązkową architekturą. To
progresywnie ujawniany punkt wejścia do odpowiedzialności. `@embedded` może
również podejmować decyzje, `@server` produkować dane, a `@report` inicjować
działania. Gdy rzeczywista topologia wymaga autonomii, wielu Controllerów,
orkiestracji albo komunikacji bez Presentera, źródło Junexis powinno opisać tę
rzeczywistość bez wciskania jej w PCP.

### Modele mentalne jako droga odkrywania platformy

PCP ustanawia również metodę, nie tylko pojedynczy wzorzec. Kolejne modele
mentalne Junexis powinny wyłaniać się z działających systemów i Laboratoriów.
Model otrzymuje nazwę dopiero wtedy, gdy powtarzający się układ
odpowiedzialności upraszcza zrozumienie, pasuje do wielu dziedzin, ma jawne
granice i posiada wykonywalny przykład.

Tak rozwijany katalog może pokazać naturalne miejsce Junexis pomiędzy opisem
domeny a platformą wykonawczą, urządzeniem a backendem, kodem a architekturą
oraz działaniem a wyjaśnieniem jego przyczyn. Nie należy tworzyć katalogu z
samych nazw. Najpierw musi istnieć praktyka i dowód, dopiero potem model
mentalny i komunikacja produktu.

## Logika, materia i rozmieszczenie

Junexis rozdziela zachowanie od środowiska wykonania:

- `task` opisuje logikę i wymagania,
- `device` opisuje możliwości konkretnego rodzaju sprzętu,
- `resource` opisuje wymaganą zdolność dostępu do stanu lub I/O,
- `pipeline` opisuje logiczne połączenia między zadaniami,
- `deployment` tworzy instancje i umieszcza je w rzeczywistym środowisku,
- `deployment` wiąże także logiczne zasoby z konkretnymi providerami,
- `protocol` realizuje komunikację przez powstałe granice.

Dzięki temu ten sam task może zostać użyty na wielu zgodnych urządzeniach, a
ten sam pipeline może mieć różne deploymenty bez zmiany znaczenia przepływu.

## Zachowanie, stan i przepływ

Junexis rozdziela trzy rzeczy często sklejane w kodzie aplikacji:

> **Task przetwarza dane. Resource udostępnia stan lub I/O. Pipeline przenosi
> dane i pokazuje kierunek ich skutków.**

Task nie powinien stawać się sterownikiem PostgreSQL, pliku ZIP ani pamięciowego
bufora. Powinien deklarować potrzebne możliwości, na przykład odczyt rekordów,
transakcyjny zapis albo sekwencyjny odczyt bajtów. Deployment wybiera konkretną
realizację: bazę, katalog, magazyn obiektowy, pamięć procesu lub usługę zewnętrzną.

To rozdzielenie umożliwia podmianę produkcyjnego zasobu na implementację
pamięciową w testach bez zmiany znaczenia tasku. Nie oznacza jednak, że wszystkie
implementacje są równoważne: trwałość, transakcje, spójność, kolejność i granice
awarii muszą pozostać jawne i sprawdzalne.

Rozproszony `stream<T>` nie jest tym samym co sekwencyjny strumień I/O. Pierwszy
opisuje relację między częściami systemu. Drugi opisuje sposób odczytu lub zapisu
danych. Plik, pamięć, ZIP/GZIP i socket należą do modelu źródeł, ujść, buforów i
transformacji I/O; nie powinny rozmywać gwarancji komunikacyjnego `stream<T>`.

## Wyjaśnialność jest częścią systemu

System rozproszony nie jest w pełni zrozumiały wyłącznie przez stan pojedynczego
procesu. Potrzebuje śladów zdarzeń, ich kontekstu oraz relacji przyczynowych.

`narrative` nie jest dekoracyjnym logowaniem. Wyraża zamiar, aby system mógł
odtworzyć przebieg działania i odpowiedzieć na pytania:

- co się wydarzyło,
- gdzie się wydarzyło,
- co było tego przyczyną,
- jakie decyzje i dane doprowadziły do skutku,
- czego system nie jest w stanie ustalić.

Wyjaśnienie musi odróżniać fakt od wniosku i brak wiedzy od braku zdarzenia.

## Kontrolowane spotkanie ze światem zewnętrznym

Żaden język nie zawiera całego świata. `extern` jest jawną granicą pozwalającą
korzystać z istniejącego kodu, bibliotek, sterowników i typów natywnych.

Ta granica powinna być:

- widoczna w źródle,
- typowana na tyle, na ile pozwala integracja,
- możliwa do obserwowania,
- walidowana względem platformy,
- traktowana jako świadome wyjście poza gwarancje Junexisa.

`extern` jest drogą integracji i ucieczki, ale nie drugim sposobem modelowania
zwykłych tasków.

## Zasady projektowe

1. **Znaczenie przed mechanizmem.** Składnia powinna ujawniać intencję systemu,
   a nie szczegóły przypadkowo wybranego backendu.
2. **Jedna odpowiedzialność na konstrukcję.** Granice między `task`, `device`,
   `resource`, `pipeline`, `deployment`, `protocol` i `narrative` muszą pozostać czytelne.
3. **Jawne gwarancje.** Komunikacja, trwałość, kolejność i granice awarii nie
   mogą opierać się na marketingowych skrótach.
4. **Bezpieczne domyślne, widoczna automatyzacja.** Kompilator może wybierać
   rozwiązania techniczne, ale musi pokazać wybór i pozwolić go zweryfikować.
5. **Wyjaśnialność od początku.** Obserwowalność nie może być doklejana dopiero
   po wystąpieniu problemu.
6. **Progressive disclosure.** Prosty system ma pozostać prosty, a dodatkowa
   precyzja powinna pojawiać się wtedy, gdy wymaga jej rzeczywistość.
7. **Dowód przed statusem.** Funkcja jest zaimplementowana dopiero wtedy, gdy
   istnieje spójna ścieżka: specyfikacja → gramatyka → AST → semantyka → IR →
   codegen → runtime → test E2E.
8. **Uczciwość wobec niepewności.** System i jego narzędzia powinny umieć
   powiedzieć „nie wiem”, zamiast tworzyć pozorną pewność.

## Kryterium dla przyszłych decyzji

Każdą zmianę języka należy ocenić pytaniami:

1. Jakie znaczenie wnosi do modelu systemu?
2. Do której odpowiedzialności należy?
3. Jakie gwarancje składa użytkownikowi?
4. Czy gwarancje można zweryfikować w kompilatorze, runtime albo Narrative?
5. Czy istniejąca konstrukcja nie wyraża już tej samej intencji?
6. Czy zmiana zwiększa zdolność systemu do wyjaśnienia własnego działania?

Jeżeli odpowiedzi nie są jasne, element nie powinien jeszcze stawać się częścią
stabilnego rdzenia języka.
