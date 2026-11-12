using System;
using System.Diagnostics;
using System.IO;

namespace Jellyfin.Plugin.MusicVideoMeta.Services;

/// <summary>
/// Replaces file contents without replacing the file itself, so the owner, group and permissions stay the same.
/// </summary>
internal static class InPlaceFile
{
    /// <summary>
    /// Copy <paramref name="source"/> over the contents of <paramref name="target"/>, keeping target's owner and
    /// permissions, then delete source. If target doesn't exist yet it is created and given the owner of
    /// <paramref name="ownerReference"/> when the server is allowed to do that.
    /// </summary>
    /// <param name="source">New contents.</param>
    /// <param name="target">File to overwrite.</param>
    /// <param name="ownerReference">File whose owner a newly created target should get.</param>
    public static void Replace(string source, string target, string? ownerReference = null)
    {
        var existed = File.Exists(target);
        using (var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read))
        using (var output = new FileStream(target, existed ? FileMode.Open : FileMode.CreateNew, FileAccess.Write, FileShare.Read))
        {
            output.SetLength(0);
            input.CopyTo(output, 1024 * 1024);
            output.Flush(true);
        }

        File.Delete(source);

        if (!existed && ownerReference is not null)
        {
            CopyOwner(ownerReference, target);
        }
    }

    /// <summary>
    /// Give <paramref name="target"/> the same owner and group as <paramref name="reference"/>.
    /// Only works when the server runs with permission to change owners (for example as root); otherwise it does nothing.
    /// </summary>
    /// <param name="reference">Reference file.</param>
    /// <param name="target">Target file.</param>
    public static void CopyOwner(string reference, string target)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        try
        {
            if (File.Exists(reference) && !OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(target, File.GetUnixFileMode(reference) & ~(UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute));
            }

            var psi = new ProcessStartInfo("chown")
            {
                UseShellExecute = false,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                CreateNoWindow = true,
            };
            psi.ArgumentList.Add("--reference=" + reference);
            psi.ArgumentList.Add(target);
            using var p = Process.Start(psi);
            p?.WaitForExit(5000);
        }
        catch (Exception)
        {
            // Not allowed or chown missing: the file keeps the server's owner.
        }
    }
}
