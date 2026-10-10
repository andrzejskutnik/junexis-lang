# M15.14 — uwierzytelniony magazyn zaufania SDK

M15.14 dodaje brakujące powiązanie między zaufanym kluczem głównym a
magazynem kluczy wydawniczych. Oddzielny klucz główny ECDSA P-256 podpisuje
SHA-256 całego magazynu, jego właściciela, rewizję i własny identyfikator.

```text
klucz główny → magazyn zaufania → klucz wydawniczy → wydanie SDK
```

Klucz główny nie podpisuje paczek SDK i może pozostawać poza codziennym
procesem wydawniczym. Prywatny klucz główny jest zabroniony w katalogu
publikacji.

```text
junexis sdk sign trust-store <magazyn.json>
  --private-key <plik.pem> --root-key-id <id> [--output <podpis.json>]

junexis sdk verify trust-store <magazyn.json>
  --signature <podpis.json> --root-key <plik.pem> --root-key-id <id>
  [--minimum-revision <liczba>]
```

Podpis ma kontrakt `junexis.sdk.trust-store-signature/0.1`. Raport weryfikacji
zawiera rewizję i odcisk SHA-256 publicznego klucza głównego. Zmieniona
polityka, obcy klucz albo zbyt stara rewizja dają kod `10`.

`junexis sdk verify trust` przyjmuje podpis polityki, publiczny klucz główny i
jego identyfikator jako nierozdzielny zestaw. Raport
`junexis.sdk.trust-verification/0.2` może dzięki temu potwierdzić cały łańcuch:
klucz główny → polityka → klucz wydawniczy → wydanie.

Bramka M15.14 wykonuje tę ścieżkę oraz odrzuca zmianę magazynu, obcy klucz
główny i poprawnie podpisaną rewizję starszą od wymaganej. Nie ustanawia
jednak oficjalnego klucza Junexis ani kanału, którym użytkownik otrzyma jego
publiczną część lub odcisk.
