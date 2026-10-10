# System Bundle authenticity 0.1

> **Status:** EXPERIMENTAL / VERIFIED dla M7.9

M7.9 rozdziela trzy pytania, które nie mogą być jednym werdyktem:

1. **Integralność** — czy zawartość ZIP odpowiada `manifest.json` i Bundle ID?
2. **Autentyczność** — czy dokładne bajty `manifest.json` mają poprawny podpis?
3. **Polityka dopuszczenia** — czy operator ufa temu stanowi i podpisującemu?

Poprawna suma SHA-256 nie mówi, kto utworzył pakiet. Poprawny podpis nie oznacza
automatycznie zaufania. Mentor nie zmienia żadnego z tych wyników.

## Podpis odłączony

```text
<bundle.zip>.signature.json
```

Kontrakt `junexis.system-bundle-signature/0.1` zawiera Bundle ID, Build ID,
nazwę podpisującego, publiczny klucz SPKI, jego fingerprint SHA-256 oraz podpis
ECDSA P-256/SHA-256 w formacie IEEE P1363. Podpis obejmuje dokładne bajty
`manifest.json`. Dzięki odłączeniu podpisu:

- deterministyczny ZIP i Bundle ID nie zmieniają się;
- prywatny klucz PEM pozostaje plikiem zewnętrznym;
- podpis może być transportowany i przechowywany jako osobny dowód;
- zmiana manifestu, tożsamości albo podpisu jest wykrywana offline.

## Polecenia

```text
junexis bundle sign <bundle.zip> --key <private.pem> --signer <name>
  [--out <signature.json>]

junexis bundle verify <bundle.zip>
  [--signature <signature.json>]
  [--auth-policy permissive|signed|trusted]
  [--trusted-signer <sha256-fingerprint>]

junexis package [project]
  [--sign-key <private.pem> --signer <name>]
```

`bundle verify` zawsze najpierw weryfikuje integralność. Następnie klasyfikuje
autentyczność jako:

- `unsigned` — brak podpisu;
- `verified` — podpis poprawny, ale fingerprint nie jest jawnie zaufany;
- `trusted` — podpis poprawny i fingerprint znajduje się w przekazanym zbiorze;
- `invalid` — podpis lub jego związek z Build/Bundle ID jest niepoprawny.

## Polityki deploymentu

| Polityka | Dopuszczone stany |
|---|---|
| `permissive` | `unsigned`, `verified`, `trusted`; nigdy `invalid` |
| `signed` | `verified`, `trusted` |
| `trusted` | wyłącznie `trusted` |

`deploy preflight` zapisuje stan, politykę, decyzję, signer i fingerprint w
`junexis.deployment-preflight/0.2`. Niespełniona polityka tworzy blokującą
kontrolę `DP1006`; spełniona — `DP0005`. Opinia Mentora pozostaje jawnie
nieblokująca i nie może zastąpić decyzji kryptograficznej ani polityki.
`deploy embedded flash` egzekwuje tę samą politykę bezpośrednio przed
ekstrakcją i uploadem, ponieważ embedded nie korzysta z local preflight.

## Studio

Deploy Center pokazuje politykę przed `Prepare`, opcjonalną ścieżkę zewnętrznego
klucza, nazwę podpisującego oraz jawny zbiór zaufanych fingerprintów. Po
preflight pokazuje osobno status autentyczności i wynik polityki. Studio nie
kopiuje prywatnego klucza do projektu, bundle, receipt ani własnego trust store.
Ta sama konfiguracja jest zachowywana przez **Prepare Embedded** i ponownie
sprawdzana przez CLI przed **Flash Selected Device**.

## Granice

M7.9 nie wprowadza PKI, certyfikatów CA, zdalnego KMS/HSM, rotacji kluczy,
unieważniania ani organizacyjnego serwera polityk. Zaufanie jest lokalnym,
jawnym wejściem operatora. Golden przepływ całego systemu należy do M7.10.
