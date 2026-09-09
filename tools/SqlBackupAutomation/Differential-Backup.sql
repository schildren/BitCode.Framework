-- BitCode.Framework — Fase 5 (F5-07): backup diferencial automatizado.
-- Ver docs/politica-backups-fase5.md seccion 2 (frecuencia por perfil DR) y seccion 4 (scripts).
--
-- Variables sqlcmd esperadas (pasadas con `sqlcmd -v Var=Valor` o por Invoke-SqlBackupJob.ps1):
--   DatabaseName : nombre de la base de datos a respaldar.
--   BackupPath   : ruta completa (incluye nombre de archivo .bak) donde se escribe el backup.
--
-- Requiere que exista al menos un backup FULL previo de esta base (Full-Backup.sql) — un
-- diferencial sin un full base no es restaurable por si solo.

BACKUP DATABASE [$(DatabaseName)]
TO DISK = N'$(BackupPath)'
WITH DIFFERENTIAL, INIT, COMPRESSION, CHECKSUM,
     NAME = N'$(DatabaseName)-differential',
     STATS = 10;
