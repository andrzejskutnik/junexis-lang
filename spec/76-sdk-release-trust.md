# M15.13 — zaufanie do kluczy wydań SDK

M15.13 oddziela poprawność kryptograficzną podpisu od decyzji, którym kluczom
wolno podpisywać wydania SDK. Zewnętrzny magazyn
`junexis.sdk.trust-store/0.1` przechowuje klucze publiczne P-256, ich
identyfikatory, przeznaczenie, okresy ważności oraz status aktywny lub
unieważniony.

```text
junexis sdk verify trust <katalog> --trust-store <plik.json>
  [--at <UTC>] [--minimum-revision <liczba>]
```

Narzędzie wybiera klucz na podstawie `keyId` podpisu, sprawdza jego zasady i
wykonuje weryfikację kryptograficzną M15.12. Magazyn zaufania musi znajdować
się poza badanym wydaniem. Wydanie nie może samo ustanowić własnego źródła
zaufania.

Każdy magazyn ma rosnącą `revision`. Opcja `--minimum-revision` pozwala
odbiorcy odrzucić starszą politykę, która mogłaby nie zawierać późniejszego
unieważnienia. Jawne `--at` umożliwia powtarzalne sprawdzanie granic ważności;
brak opcji oznacza bieżący czas UTC. Klucz unieważniony jest zawsze odrzucany.

Raport `junexis.sdk.trust-verification/0.1` zachowuje rewizję, identyfikator
klucza, czas decyzji i wersję wydania. Niezaufany wynik ma kod `9`.

Bramka M15.13 potwierdza aktywny klucz w okresie nakładania się rotacji oraz
odrzuca klucz wygasły, unieważniony, nieznany i podmieniony. Sprawdza również
cofnięcie rewizji i próbę samodzielnego zadeklarowania zaufania przez wydanie.

Etap nie oznacza jeszcze istnienia oficjalnego magazynu Junexis. Bezpieczna
dystrybucja magazynu, ochrona klucza prywatnego, podpisywanie samej polityki,
aktualizacje i publiczny hosting pozostają osobnymi zadaniami wydawniczymi.
