# M17.12.2 — własny urząd certyfikacji Registry

Kompilator domyślnie ufa wyłącznie systemowemu magazynowi certyfikatów. Prywatne
wdrożenie Registry może jawnie wskazać publiczny certyfikat swojego urzędu:

```powershell
$env:JUNEXIS_REGISTRY_CA_CERTIFICATE = 'C:\Junexis\certificates\registry-ca.cer'
junexis restore . --registry https://registry.example
```

Plik może być zapisany jako PEM albo DER. Kompilator buduje dla tego połączenia
oddzielny łańcuch `CustomRootTrust`. Nadal odrzuca niezgodną nazwę hosta,
nieważny łańcuch i certyfikat niepodpisany przez wskazany korzeń. Ustawienie
nie wyłącza TLS i nie zmienia systemowego magazynu zaufania.

Pełna bramka `junexis-platform/scripts/verify-m17-12-2.ps1` uruchamia rzeczywisty
odtworzenie pakietu aplikacyjnego przez HTTPS z takim prywatnym korzeniem.
