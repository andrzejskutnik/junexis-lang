# Audyt jakości Golden Home Report — 2026-08-14

## Wynik

`verification/verify-browser-e2e.mjs` zakończył się `PASS` w lokalnym Chrome
na Windows x64. Audyt wykonał pomiary w prawdziwym DOM po uruchomieniu
wygenerowanego serwera, transportu i aplikacji `@report`.

## Dostępność i semantyka

- dokument ma język `pl` i dokładnie jeden nagłówek `h1`,
- stan połączenia używa `role="status"` oraz `aria-live="polite"`,
- pierwszy `Tab` ustawia fokus na widocznym odnośniku „Przejdź do raportu”,
- fokus ma obrys `3 px`,
- `Enter` przenosi fokus do `report-content`,
- pusty stan jest widoczny przed pierwszą obserwacją,
- `prefers-reduced-motion: reduce` zeruje czas przejść i animacji.

## Kontrast WCAG

Wszystkie sprawdzane pary przekroczyły wymagane `4,5:1`.

| Para | Ciemny motyw | Jasny motyw |
|---|---:|---:|
| tekst / tło | 17,17:1 | 14,24:1 |
| tekst pomocniczy / tło | 9,68:1 | 5,44:1 |
| tekst / powierzchnia | 15,30:1 | 15,05:1 |
| akcent / tło | 10,39:1 | 6,10:1 |
| ostrzeżenie / powierzchnia | 11,49:1 | 6,75:1 |

Najniższy wynik wyniósł `5,44:1`.

## Mały ekran

Przy widoku `390 × 844 px`:

- szerokość dokumentu wyniosła dokładnie `390 px`,
- nie pojawiło się poziome przewijanie,
- nagłówek przeszedł do układu kolumnowego,
- panel miał szerokość `358 px` i pozostał wewnątrz viewportu.

## Ograniczone dane

Kontrolowana sonda wywołała ten sam wygenerowany renderer z:

- kolekcją `100` elementów,
- tekstem długości `5000` znaków,
- `55` kolejnymi obserwacjami.

DOM pokazał pierwsze `20` elementów i komunikat o `80` ukrytych, skrócił długi
tekst wielokropkiem oraz zatrzymał historię na `50 / 50`. Widoczna treść sondy
miała `4152` znaki.

## Granica

Audyt potwierdza automatyczny i deklaratywny renderer w Chromium. Nie obejmuje
jeszcze czytnika ekranu, Firefox/WebKit, wymuszonego trybu wysokiego kontrastu
ani własnych adapterów React/Vue.
