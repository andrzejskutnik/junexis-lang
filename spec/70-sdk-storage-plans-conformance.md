# M15.7 — capability ma wykonywalny dowód

M15.7 rozszerza przenośny Conformance Kit o profil `storage-plans`. Provider,
który deklaruje `query-plan` i `mutation-plan`, może dostarczyć stanowy fixture:

```text
query.plan (stan przed)
        ↓
mutation.plan (jawny skutek)
        ↓
query.plan (potwierdzenie stanu po)
```

Polecenie `junexis sdk test provider <path> --profile storage-plans` wykonuje
każdy krok w jednej sesji. `query.plan` musi zwrócić kursor, `mutation.plan`
pojedynczy wynik, a efekty muszą być odpowiednio `read` i `write`. Runner
porównuje kanoniczny JSON, włącznie z kolejnością wierszy kursora.

Fixture używa publicznego schematu
`junexis.provider.conformance.storage-plans/0.1`. Profil nie jest domyślną
obietnicą wszystkich providerów i nie zastępuje bazowego Conformance Kitu.
Jest dodatkowym dowodem wymaganym wtedy, gdy provider chce wiarygodnie
zadeklarować obie capabilities planów.

Bramka M15.7 instaluje rzeczywiste SDK Tool, wykonuje poprawny scenariusz, a
następnie zmienia oczekiwany stan końcowy i wymaga kontrolowanego wyniku `fail`.
