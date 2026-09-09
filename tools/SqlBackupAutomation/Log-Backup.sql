-- BitCode.Framework — Fase 5 (F5-07): backup de log de transacciones automatizado.
-- Ver docs/politica-backups-fase5.md seccion 2 (frecuencia por perfil DR: cada 1/5/15 minutos
-- segun Platinum/Gold/Standard) y seccion 4 (scripts).
--
-- Variables sqlcmd esperadas (pasadas con `sqlcmd -v Var=Valor` o por Invoke-SqlBackupJob.ps1):
--   DatabaseName : nombre de la base de datos a respaldar.
--   BackupPath   : ruta completa (incluye nombre de archivo .trn) donde se escribe el backup.
--
-- Requiere modelo de recuperacion FULL; con SIMPLE este comando falla (no hay log que respaldar
-- de forma incremental).

BACKUP LOG [$(DatabaseName)]
TO DISK = N'$(BackupPath)'
WITH INIT, COMPRESSION, CHECKSUM,
     NAME = N'$(DatabaseName)-log',
     STATS = 10;
