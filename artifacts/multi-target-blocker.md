# Multi-target partial-failure E2E evidence

The full Elsa multi-target recovery test requires a real SQL Server and a real MySQL instance. It is executed by the `multi-target-recovery` job in `.github/workflows/database-evidence.yml` and its result is bound in `artifacts/multi-target-result.json`.

Local runs without `GOCORDIS_SQLSERVER_CONNECTION` and `GOCORDIS_MYSQL_CONNECTION` record an explicit skip; that skip is not acceptance evidence.
