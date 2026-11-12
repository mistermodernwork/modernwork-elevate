namespace ModernWorkElevate.Core.Configuration;

using System.Security.AccessControl;
using System.Security.Principal;

/// <summary>
/// Restricts a per-user data directory to the signed-in user and SYSTEM: inheritance from
/// the profile is cut, so no other account on the machine — another user, and by default
/// not even a local administrator without taking ownership — can read or change the
/// settings and account list. The rule applies to all files and subfolders.
/// </summary>
/// <remarks>
/// SYSTEM stays because profile management, backup and Windows itself run as SYSTEM.
/// The files hold no secrets either way (no tokens, no passwords); the ACL keeps another
/// profile from tampering with which tenants and App Registrations the user signs in to.
/// </remarks>
public static class UserOnlyDirectoryAcl
{
    /// <summary>
    /// Applies the ACL to <paramref name="directory"/>. Returns false (never throws) when it
    /// cannot: not Windows, a file system without ACLs, or no right to change the ACL.
    /// </summary>
    public static bool TryApply(string directory)
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            var user = identity.User;
            if (user is null)
            {
                return false;
            }

            var system = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
            const InheritanceFlags inherit = InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;

            var security = new DirectorySecurity();
            security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
            security.AddAccessRule(new FileSystemAccessRule(user, FileSystemRights.FullControl, inherit, PropagationFlags.None, AccessControlType.Allow));
            security.AddAccessRule(new FileSystemAccessRule(system, FileSystemRights.FullControl, inherit, PropagationFlags.None, AccessControlType.Allow));

            new DirectoryInfo(directory).SetAccessControl(security);
            return true;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or PlatformNotSupportedException or InvalidOperationException)
        {
            return false;
        }
    }
}
