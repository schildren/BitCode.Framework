-- BitCode.Framework — Fase 5 (F5-09): aprovisiona el cifrado nativo de backups de SQL Server.
-- Ver docs/backups-inmutables-fase5.md seccion 2 (cifrado) para el razonamiento completo.
--
-- Ejecutar UNA VEZ por instancia de SQL Server (idempotente — no falla si ya existe), contra la
-- base "master", antes del primer Full-Backup.sql/Differential-Backup.sql/Log-Backup.sql que use
-- la misma -CertificateName. Crea:
--   1. La Database Master Key de "master" (si no existe), que protege la clave privada del
--      certificado en reposo.
--   2. Un certificado de servidor dedicado a cifrar backups (nunca el certificado usado para
--      otro proposito, p. ej. TDE o Always Encrypted — un compromiso de esa clave no debe exponer
--      los backups y viceversa).
--
-- Variables sqlcmd esperadas:
--   MasterKeyPassword : contraseña para CREATE MASTER KEY. En un entorno productivo real debe
--                       provenir de un secret store (ver docs/guia-secret-provider.md), nunca de
--                       un valor fijo en un script versionado — este script solo la reenvia a
--                       T-SQL, no la persiste ni la loguea.
--   CertificateName   : nombre del certificado de servidor a crear/usar (debe coincidir con el
--                       -CertificateName pasado a Invoke-SqlBackupJob.ps1 en cada backup).
--
-- Nota de continuidad (DR): el certificado creado aqui vive UNICAMENTE en esta instancia. Para
-- poder restaurar un backup cifrado en OTRO servidor (el escenario real de DR entre regiones,
-- ver docs/replicacion-sql-fase5.md), es obligatorio exportar este certificado con
-- Export-BackupEncryptionCertificate.sql e importarlo en el servidor destino con
-- Import-BackupEncryptionCertificate.sql ANTES de restaurar — sin el certificado (clave privada
-- incluida), RESTORE de un backup cifrado falla por diseño (ver prueba de esta propiedad en
-- SqlBackupRestoreIntegrationTests.RestoreEncryptedBackup_WithoutCertificate_FailsWithCertificateError).

IF NOT EXISTS (SELECT 1 FROM sys.symmetric_keys WHERE name = N'##MS_DatabaseMasterKey##')
BEGIN
    CREATE MASTER KEY ENCRYPTION BY PASSWORD = N'$(MasterKeyPassword)';
END

IF NOT EXISTS (SELECT 1 FROM sys.certificates WHERE name = N'$(CertificateName)')
BEGIN
    CREATE CERTIFICATE [$(CertificateName)]
        WITH SUBJECT = N'BitCode.Framework - certificado de cifrado de backups (F5-09)';
END
