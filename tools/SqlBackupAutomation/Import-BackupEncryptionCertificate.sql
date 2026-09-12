-- BitCode.Framework — Fase 5 (F5-09): importa, en un servidor DISTINTO al origen, el certificado
-- de cifrado de backups exportado por Export-BackupEncryptionCertificate.sql, para poder
-- restaurar en este servidor backups cifrados generados en el origen (escenario real de DR entre
-- regiones, ver docs/replicacion-sql-fase5.md y docs/backups-inmutables-fase5.md seccion 2).
--
-- Ejecutar contra "master" en el servidor DESTINO, con los mismos dos archivos (.cer/.pvk)
-- generados por Export-BackupEncryptionCertificate.sql ya transferidos a este servidor por un
-- canal seguro.
--
-- Variables sqlcmd esperadas:
--   MasterKeyPassword    : contraseña para CREATE MASTER KEY de este servidor (puede ser distinta
--                          de la del origen — la Master Key de destino protege la copia importada
--                          de la clave privada, no necesita coincidir).
--   CertificateName      : mismo nombre de certificado usado en el origen (debe coincidir
--                          exactamente con el usado al generar el backup, SQL Server valida el
--                          nombre del certificado al restaurar).
--   CertificateFilePath  : ruta local (en el servidor destino) del archivo .cer transferido.
--   PrivateKeyFilePath   : ruta local (en el servidor destino) del archivo .pvk transferido.
--   PrivateKeyPassword   : misma contraseña usada al exportar en Export-BackupEncryptionCertificate.sql.

IF NOT EXISTS (SELECT 1 FROM sys.symmetric_keys WHERE name = N'##MS_DatabaseMasterKey##')
BEGIN
    CREATE MASTER KEY ENCRYPTION BY PASSWORD = N'$(MasterKeyPassword)';
END

CREATE CERTIFICATE [$(CertificateName)]
    FROM FILE = N'$(CertificateFilePath)'
    WITH PRIVATE KEY (
        FILE = N'$(PrivateKeyFilePath)',
        DECRYPTION BY PASSWORD = N'$(PrivateKeyPassword)'
    );
