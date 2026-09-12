using System;
using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;

namespace Shared.Infrastructure.Persistence.Tests.Integration;

/// <summary>
/// F5-09 (Fase 5 — Disaster Recovery y multi-región): prueba real y ejecutada del mecanismo de
/// inmutabilidad local (WORM) descrito en <c>docs/backups-inmutables-fase5.md</c> y aplicado por
/// <c>tools/SqlBackupAutomation/Protect-BackupImmutability.ps1</c>.
///
/// Esta prueba NO reimplementa el script de PowerShell: ejercita el mismo mecanismo del sistema
/// operativo (una <see cref="FileSystemAccessRule"/> de tipo <see cref="AccessControlType.Deny"/>
/// sobre Delete/WriteData/AppendData, más el atributo <c>ReadOnly</c>) directamente contra un
/// archivo real en el disco de esta máquina, y verifica que un intento real de
/// <see cref="File.Delete(string)"/> desde este mismo proceso falla con
/// <see cref="UnauthorizedAccessException"/> — el criterio de aceptación exacto de F5-09
/// ("Resistencia a borrado probada").
///
/// Solo se ejecuta contra el mecanismo real en Windows (las ACL de <see cref="FileSystemSecurity"/>
/// son específicas de Windows; en Linux/macOS este tipo no aplica permisos NTFS). El pipeline de
/// CI de este repositorio corre en <c>ubuntu-latest</c> (ver <c>.github/workflows/*.yml</c>), por
/// lo que en ese entorno la prueba se omite en tiempo de ejecución sin fallar el build — la
/// evidencia real de ejecución en Windows se documenta en
/// <c>docs/backups-inmutables-fase5.md</c> sección 4, con el comando y output reales.
/// </summary>
public class BackupImmutabilityFileSystemTests
{
    [Fact]
    public void ArchivoBloqueadoConAclDeny_FileDelete_LanzaUnauthorizedAccessException()
    {
        if (!OperatingSystem.IsWindows())
        {
            // Ver comentario de clase: el mecanismo de ACL NTFS solo aplica en Windows, el
            // entorno real de este proyecto. No se simula en otros SO.
            return;
        }

        var directory = Directory.CreateTempSubdirectory("bitcode-worm-test-");
        var filePath = Path.Combine(directory.FullName, "backup-worm-demo.bak");
        File.WriteAllText(filePath, "contenido de backup de demostracion — F5-09");

        var identity = WindowsIdentity.GetCurrent().Name;

        // --- Mismo mecanismo real que Protect-BackupImmutability.ps1 ---
        var fileInfo = new FileInfo(filePath) { IsReadOnly = true };

        var acl = fileInfo.GetAccessControl();
        var denyRights = FileSystemRights.Delete
                          | FileSystemRights.DeleteSubdirectoriesAndFiles
                          | FileSystemRights.WriteData
                          | FileSystemRights.AppendData;
        var denyRule = new FileSystemAccessRule(identity, denyRights, AccessControlType.Deny);
        acl.AddAccessRule(denyRule);
        fileInfo.SetAccessControl(acl);

        try
        {
            // --- Prueba real: intento real de borrado desde este mismo proceso ---
            var exception = Record.Exception(() => File.Delete(filePath));

            Assert.NotNull(exception);
            Assert.IsType<UnauthorizedAccessException>(exception);
            Assert.True(File.Exists(filePath), "El archivo bloqueado debe seguir existiendo tras el intento de borrado fallido.");
        }
        finally
        {
            // Limpieza del entorno de pruebas: quitar la regla Deny específica (acción
            // administrativa separada y explícita, igual que Unlock-BackupImmutability.ps1 en
            // producción) para poder borrar el archivo temporal al final del test.
            var cleanupAcl = fileInfo.GetAccessControl();
            cleanupAcl.RemoveAccessRule(denyRule);
            fileInfo.SetAccessControl(cleanupAcl);
            fileInfo.IsReadOnly = false;
            fileInfo.Refresh();

            File.Delete(filePath);
            directory.Delete(recursive: true);
        }
    }
}
