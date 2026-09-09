-- BitCode.Framework — Fase 5 (F5-09): exporta el certificado de cifrado de backups (con su clave
-- privada) para poder restaurar backups cifrados en OTRO servidor (DR entre regiones).
-- Ver docs/backups-inmutables-fase5.md seccion 2 y Enable-BackupEncryption.sql.
--
-- Ejecutar contra "master" en el servidor ORIGEN, una vez que el certificado ya existe (ver
-- Enable-BackupEncryption.sql). El resultado son DOS archivos que deben transferirse al servidor
-- destino por un canal seguro (nunca junto con el propio backup de datos, ni por el mismo canal
-- que un atacante que ya comprometio el repositorio de backups podria alcanzar — ver
-- docs/backups-inmutables-fase5.md seccion 1, aislamiento) y luego importarse con
-- Import-BackupEncryptionCertificate.sql.
--
-- Variables sqlcmd esperadas:
--   CertificateName      : nombre del certificado creado por Enable-BackupEncryption.sql.
--   CertificateFilePath  : ruta de archivo donde se exporta la parte publica del certificado
--                          (.cer).
--   PrivateKeyFilePath   : ruta de archivo donde se exporta la clave privada, cifrada con
--                          PrivateKeyPassword (.pvk).
--   PrivateKeyPassword   : contraseña que protege el archivo de clave privada exportado. Debe
--                          provenir de un secret store en un entorno real, nunca fija en el
--                          script (ver nota de Enable-BackupEncryption.sql).

BACKUP CERTIFICATE [$(CertificateName)]
TO FILE = N'$(CertificateFilePath)'
WITH PRIVATE KEY (
    FILE = N'$(PrivateKeyFilePath)',
    ENCRYPTION BY PASSWORD = N'$(PrivateKeyPassword)'
);
