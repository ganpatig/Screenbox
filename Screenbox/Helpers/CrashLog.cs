using System;
using System.IO;
using Windows.Storage;

namespace Screenbox.Helpers;

/// <summary>
/// Records unhandled exceptions in a file, so that a crash can be looked into after the fact.
/// </summary>
/// <remarks>
/// The log is kept in the temporary folder of the app and is limited in size. Crash reports also go to the
/// telemetry service, which is not available while working on a build of the app.
/// </remarks>
internal static class CrashLog
{
    private const long MaxLength = 256 * 1024;

    private static string? _path;

    /// <summary>
    /// Appends an exception to the log.
    /// </summary>
    /// <param name="source">Where the exception came from, such as the event that reported it.</param>
    /// <param name="exception">The exception, or <see langword="null"/> if only a message is available.</param>
    /// <param name="message">The message that came with the exception.</param>
    public static void Write(string source, Exception? exception, string? message = null)
    {
        try
        {
            _path ??= Path.Combine(ApplicationData.Current.TemporaryFolder.Path, "crash.log");
            FileInfo file = new(_path);
            if (file.Exists && file.Length > MaxLength)
            {
                file.Delete();
            }

            File.AppendAllText(_path,
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{source}] {message}{Environment.NewLine}{exception}{Environment.NewLine}{Environment.NewLine}");
        }
        catch (Exception)
        {
            // The app is already failing. Failing to write the log must not make it worse.
        }
    }
}
