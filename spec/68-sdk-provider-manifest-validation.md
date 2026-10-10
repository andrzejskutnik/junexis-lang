# M15.5 — manifest providera jest wykonywalnym kontraktem metadanych

> **Status:** VERIFIED STRICT MANIFEST
>
> **Kontrakt polecenia:** `junexis sdk validate provider`

## Decyzja

Manifest nie jest luźnym opisem marketingowym paczki. Musi być wersjonowany,
ściśle walidowany i zgodny z fixture oraz projektem, który faktycznie tworzy
artefakt.

SDK publikuje schematy JSON Draft 2020-12 dla:

- `junexis.provider/0.1`;
- `junexis.provider.conformance/0.1`.

Nieznane pola są odrzucane. Nowa informacja kontraktowa wymaga jawnej ewolucji
schematu.

## Tożsamość providera

Manifest wiąże w jednym miejscu:

- ID i SemVer paczki;
- wersję Storage Runtime ABI;
- factory type i resource kind;
- portable capabilities;
- dokładny PackageId i wersję publicznego SDK;
- nazwę assembly i target framework.

Walidator porównuje te dane z produkcyjnym `.csproj`. `ProjectReference` jest
zabroniony, ponieważ oznaczałby zależność od checkoutu zamiast publicznego SDK.

## Zgodność fixture

Capability `read` wymaga `readProbe`, a `write` wymaga `writeProbe`. Próba bez
odpowiedniej capability także jest błędem. Binding i wartości konfiguracyjne
mają jawne typy, a efekt próby musi odpowiadać jej roli.

## Diagnostyki

Raport `junexis.sdk.provider-validation/0.1` zwraca stabilne kody `JXMxxxx`,
severity, ścieżkę i komunikat. Kod wyjścia `4` oznacza niepoprawny kontrakt
metadanych. Kod `2` pozostaje błędem użycia narzędzia.

Conformance Kit wykonuje walidację jako obowiązkowy preflight i nie buduje ani
nie ładuje providera z błędnym manifestem.

## Granica

Schemat źródłowy nie zawiera jeszcze sum gotowej paczki, podpisu, RID i SBOM.
Są to dane wydania, które mogą powstać dopiero po deterministycznym buildzie.
