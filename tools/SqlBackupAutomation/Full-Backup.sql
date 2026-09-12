-- BitCode.Framework — Fase 5 (F5-07): backup completo (full) automatizado.
-- Ver docs/politica-backups-fase5.md seccion 2 (frecuencia por perfil DR) y seccion 4 (scripts).
--
-- Variables sqlcmd esperadas (pasadas con `sqlcmd -v Var=Valor` o por Invoke-SqlBackupJob.ps1):
--   DatabaseName    : nombre de la base de datos a respaldar.
--   BackupPath      : ruta completa (incluye nombre de archivo .bak) donde se escribe el backup.
--   CertificateName : (F5-09) certificado de servidor creado por Enable-BackupEncryption.sql,
--                     usado para cifrar el backup en reposo (ver docs/backups-inmutables-fase5.md
--                     seccion 2). Obligatorio: un backup sin cifrar no cumple el criterio de
--                     aceptacion de F5-09 ("cifrado" del repositorio seguro de backups).
--
-- Requiere que la base de datos este en modelo de recuperacion FULL (ver politica seccion 2,
-- nota sobre RECOVERY FULL) para que los backups de log subsiguientes sean posibles.
--
-- Ejecutar contra una conexion con permiso BACKUP DATABASE (tipicamente la base "master").

BACKUP DATABASE [$(DatabaseName)]
TO DISK = N'$(BackupPath)'
WITH INIT, COMPRESSION, CHECKSUM,
     NAME = N'$(DatabaseName)-full',
     ENCRYPTION (ALGORITHM = AES_256, SERVER CERTIFICATE = [$(CertificateName)]),
     STATS = 10;
