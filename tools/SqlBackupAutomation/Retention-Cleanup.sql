-- BitCode.Framework — Fase 5 (F5-07): limpieza de retencion de backups.
-- Ver docs/politica-backups-fase5.md seccion 3 (retencion por perfil DR y por tipo de backup).
--
-- Variables sqlcmd esperadas (pasadas con `sqlcmd -v Var=Valor` o por Invoke-SqlBackupJob.ps1):
--   BackupFolder  : carpeta donde viven los archivos de backup de UN solo tipo (full/differential
--                   usan extension "bak", log usa "trn" — ver Invoke-SqlBackupJob.ps1, que organiza
--                   los backups en subcarpetas separadas por tipo para que esta limpieza nunca
--                   borre por error un backup de un tipo distinto al indicado).
--   Extension     : extension de archivo a considerar para el borrado ("bak" o "trn"), SIN punto.
--   RetentionDays : cantidad de dias a conservar; archivos mas antiguos que
--                   (fecha actual - RetentionDays) se eliminan.
--
-- master.dbo.xp_delete_file es el mismo mecanismo que usan los Maintenance Plans nativos de SQL
-- Server para "Cleanup Task"; funciona igual en SQL Server sobre Linux (verificado manualmente
-- contra la imagen mcr.microsoft.com/mssql/server:2022-latest, ver
-- docs/politica-backups-fase5.md seccion 5) y sobre Windows.
--
-- Parametro 1 (subsistema): 0 = archivo de backup .bak/.trn (no un reporte .txt de plan de
-- mantenimiento, que usaria 1).
--
-- La fecha de corte se calcula en una variable local (no como expresion inline en la lista de
-- argumentos del EXEC): xp_delete_file, al ser un procedimiento extendido, no acepta una
-- expresion (p. ej. `GETDATE() - 30`) directamente como argumento posicional — falla con
-- "Incorrect syntax near ')'" (verificado contra la imagen Linux de SQL Server 2022 real). Una
-- variable tipada evita esa restriccion sin cambiar el resultado.

DECLARE @cutoffDate DATETIME = GETDATE() - $(RetentionDays);

EXEC master.dbo.xp_delete_file
    0,
    N'$(BackupFolder)',
    N'$(Extension)',
    @cutoffDate;
