# Dom 4 — pokaż cały system człowiekowi

Czwarty przyrost kończy pierwszy pełny przepływ inteligentnego domu. Projekcja
otrzymuje wyłącznie `VentilationConfirmation`, a lokalny adapter `extern`
udostępnia stronę WWW oraz stan JSON.

```powershell
junexis doctor
junexis check
junexis artifact-gate --target server
junexis run --duration 30
```

Po uruchomieniu CLI wypisuje dynamiczny lokalny adres:

```text
PANEL http://127.0.0.1:<port>/
```

Panel pokazuje pomiar, próg, decyzję, żądanie, potwierdzony stan wentylacji,
status wykonania i przyczynę. Adapter nasłuchuje wyłącznie na localhost i nie
udostępnia operacji zmieniających stan.

Jest to granica pierwszego sukcesu, ale nadal lokalna symulacja `@server`.
Generator `@report` nie tworzy jeszcze samodzielnej aplikacji przeglądarkowej
uruchamianej przez `junexis run`; `user/server/home_panel.go` jest jawnym,
wersjonowanym adapterem tego przyrostu.

Powtarzalny odbiór strony, API oraz obu stanów wykonuje:

```powershell
& verification/verify-home-panel.ps1
```
