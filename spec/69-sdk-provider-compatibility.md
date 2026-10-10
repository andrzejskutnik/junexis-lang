# M15.6 — rozstrzyganie zgodności providera

## Problem

Poprawny manifest dowodzi, że provider jest wewnętrznie spójny. Nie dowodzi,
że pasuje do konkretnej wersji Runtime Host ani że ma capabilities wymagane
przez aplikację. Tę decyzję należy podjąć przed załadowaniem obcego assembly.

## Kontrakt polecenia

```text
junexis sdk compatibility provider <path>
  --runtime-version <semver>
  [--rid <rid>]
  [--require <capability>]...
  [--json]
```

Resolver wykonuje preflight M15.5, a następnie porównuje ABI, wersję SDK,
wersję Hosta, TFM/RID i wymagane capabilities. Nie buduje projektu i nie
uruchamia providera. Wersja Hosta jest jawna i obowiązkowa.

Wynik `junexis.sdk.provider-compatibility/0.1` ma status `compatible` albo
`incompatible`. Stabilne diagnostyki `JXC` zawierają zarówno przyczynę, jak i
remediation. Kod `5` oznacza świadomie stwierdzoną niezgodność; nie jest awarią
narzędzia.

## Pierwsza macierz

Publiczny katalog SDK wiąże:

- `junexis.provider/0.1`;
- `Junexis.Storage.Runtime.Abstractions` 0.1.0;
- ABI `junexis.storage.runtime/0.1`;
- Runtime Host `[0.1.0, 0.2.0)`;
- `net10.0` i przenośny RID `any`.

RID konkretnej paczki binarnej nie jest dopisywany do źródłowego manifestu
0.1. Będzie częścią późniejszego kontraktu wydania. Dlatego dzisiejszy wynik
potwierdza zgodność kontraktu autorskiego i środowiska, nie gotowość podpisanej
paczki dystrybucyjnej.

## Bramka dowodu

M15.6 wymaga testu instalacji rzeczywistego SDK Tool, obecności katalogu
zgodności w `.nupkg`, przypadku pozytywnego oraz odrzucenia niezgodnej wersji
Runtime, brakującej capability i manifestu, który nie przeszedł M15.5.
