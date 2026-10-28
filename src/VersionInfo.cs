using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using TestStandMCP.Models;

namespace TestStandMCP;

/// <summary>
/// Which release this executable is — the single source for the <c>--version</c> output, the MCP
/// <c>serverInfo</c> and the startup log line, so the three can never disagree.
///
/// <para><b>Where each field comes from.</b> <see cref="Version"/> and <see cref="Commit"/> are stamped
/// into the assembly's informational version at build time (<c>X.Y.Z+sha</c>: the SDK appends
/// <c>SourceRevisionId</c> after a <c>'+'</c>). The release workflow's <c>VERSION.txt</c> only adds what
/// the assembly cannot know — the tag, the build time and the run URL. <see cref="Arch"/> is the running
/// process's architecture, which decides the TestStand bitness this install can drive.</para>
///
/// <para><b>An untagged build is <c>0.0.0-dev</c></b> (the csproj default), never a made-up number.
/// A missing, oversized or malformed <c>VERSION.txt</c> degrades to a <see cref="Note"/>; resolving a
/// version must never be able to stop the server from starting.</para>
/// </summary>
public sealed record VersionInfo(
    string Version,
    string? Commit,
    string? Tag,
    string? BuiltAt,
    string? RunUrl,
    string? Note)
{
    /// <summary>The process architecture (<c>x86</c> or <c>x64</c>): the TestStand bitness this exe drives.</summary>
    public string Arch { get; init; } = RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant();

    /// <summary>The product's name — shown by <c>--version</c>, in <c>serverInfo</c> and in the logs.</summary>
    public const string ProductName = "TestStand MCP Server";

    /// <summary>The version of a build nobody stamped (the csproj default).</summary>
    public const string UntaggedVersion = "0.0.0-dev";

    private const char BuildMetadataSeparator = '+';
    private const char KeyValueSeparator = ':';
    private const string VersionTxtFileName = "VERSION.txt";

    // VERSION.txt keys — also the labels --version prints in front of each value.
    private const string TagKey     = "tag";
    private const string VersionKey = "version";
    private const string CommitKey  = "commit";
    private const string ArchKey    = "arch";
    private const string BuiltKey   = "built";
    private const string RunKey     = "run";
    private const string NoteLabel  = "note";
    private const int LabelColumnWidth = 8;

    /// <summary>A real VERSION.txt is a handful of short lines; anything bigger is not one.</summary>
    private const long MaxVersionTxtBytes = 4096;

    private const string IncompleteTagNote = "release metadata incomplete: no tag";

    // version, commit and arch are listed so a VERSION.txt that carries them still counts as readable;
    // the assembly and the process stay the authority for those three.
    private static readonly HashSet<string> RecognisedKeys =
        new(StringComparer.OrdinalIgnoreCase) { TagKey, VersionKey, CommitKey, ArchKey, BuiltKey, RunKey };

    /// <summary>The version of THIS executable, resolved once. VERSION.txt sits one level above the exe.</summary>
    public static VersionInfo Current { get; } = Resolve(
        typeof(VersionInfo).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion,
        DefaultVersionTxtPath());

    /// <summary>
    /// Resolves the version from the assembly's informational version (<c>X.Y.Z</c> or <c>X.Y.Z+sha</c>)
    /// and the optional release metadata file at <paramref name="versionTxtPath"/>. The file read is the
    /// only I/O, and a problem with it becomes a <see cref="Note"/> — this never throws.
    /// </summary>
    public static VersionInfo Resolve(string? informationalVersion, string? versionTxtPath)
    {
        var (version, commit) = SplitInformationalVersion(informationalVersion);
        var metadata = ReadReleaseMetadata(versionTxtPath);
        var isTagged = version != UntaggedVersion;

        return new VersionInfo(version, commit, metadata.Tag, metadata.BuiltAt, metadata.RunUrl,
            NoteFor(metadata, isTagged));
    }

    /// <summary>What a client is told about this server in the MCP <c>initialize</c> response.</summary>
    public McpServerInfo ToServerInfo() => new() { Name = ProductName, Version = Version };

    /// <summary>The multi-line text <c>--version</c> prints; a field that is not known is left out.</summary>
    public string FormatForCli()
    {
        var text = new StringBuilder();
        text.AppendLine($"{ProductName} {Version}");
        AppendField(text, TagKey, Tag);
        AppendField(text, CommitKey, Commit);
        AppendField(text, ArchKey, Arch);
        AppendField(text, BuiltKey, BuiltAt);
        AppendField(text, RunKey, RunUrl);
        AppendField(text, NoteLabel, Note);
        return text.ToString();
    }

    private static void AppendField(StringBuilder text, string label, string? value)
    {
        if (!string.IsNullOrEmpty(value))
            text.AppendLine($"  {label + ":",-LabelColumnWidth}{value}");
    }

    /// <summary>Null when the location cannot even be computed — the caller then sees "absent".</summary>
    private static string? DefaultVersionTxtPath()
    {
        try
        {
            return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", VersionTxtFileName));
        }
        catch (Exception)
        {
            // A static initializer must not throw — see ReadReleaseMetadata.
            return null;
        }
    }

    private sealed record ReleaseMetadata(
        string? Tag, string? BuiltAt, string? RunUrl, string? Note = null, bool IsAbsent = false);

    private static ReleaseMetadata NoMetadata(string note, bool isAbsent = false) =>
        new(Tag: null, BuiltAt: null, RunUrl: null, note, isAbsent);

    private static ReleaseMetadata ReadReleaseMetadata(string? versionTxtPath)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(versionTxtPath) || !File.Exists(versionTxtPath))
                return NoMetadata(
                    $"release metadata absent ({versionTxtPath ?? VersionTxtFileName} not found)", isAbsent: true);

            var length = new FileInfo(versionTxtPath).Length;
            if (length > MaxVersionTxtBytes)
                return NoMetadata(
                    $"release metadata unreadable ({versionTxtPath} is {length} bytes, the limit is {MaxVersionTxtBytes})");

            var values = ParseKeyValueLines(File.ReadAllLines(versionTxtPath));
            if (values.Count == 0)
                return NoMetadata($"release metadata unreadable ({versionTxtPath} has no key: value lines)");

            return new ReleaseMetadata(
                values.GetValueOrDefault(TagKey),
                values.GetValueOrDefault(BuiltKey),
                values.GetValueOrDefault(RunKey));
        }
        catch (Exception ex)
        {
            // Whatever goes wrong with this optional file (locked, permissions, bad path) must not
            // stop the server — Current is a static initializer, so a throw here would be a TypeInitializationException.
            return NoMetadata($"release metadata unreadable ({ex.Message})");
        }
    }

    /// <summary>
    /// An untagged build has no release metadata by design, so only the "absent" case is silent there;
    /// a tagged build whose readable VERSION.txt lacks the tag is called out.
    /// </summary>
    private static string? NoteFor(ReleaseMetadata metadata, bool isTagged)
    {
        if (metadata.IsAbsent)
            return isTagged ? metadata.Note : null;
        if (metadata.Note is not null)
            return metadata.Note;
        return isTagged && metadata.Tag is null ? IncompleteTagNote : null;
    }

    /// <summary>Recognised <c>key: value</c> lines only; the value keeps any further ':' (URLs, ISO times).</summary>
    private static Dictionary<string, string> ParseKeyValueLines(IEnumerable<string> lines)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in lines)
        {
            var separatorIndex = line.IndexOf(KeyValueSeparator);
            if (separatorIndex <= 0)
                continue;

            var key = line[..separatorIndex].Trim();
            var value = WithoutControlCharacters(line[(separatorIndex + 1)..]).Trim();
            if (RecognisedKeys.Contains(key) && value.Length > 0)
                values[key] = value;
        }
        return values;
    }

    /// <summary>The values reach a terminal — an ESC sequence in VERSION.txt must not.</summary>
    private static string WithoutControlCharacters(string value) =>
        new(value.Where(character => !char.IsControl(character)).ToArray());

    private static (string Version, string? Commit) SplitInformationalVersion(string? informationalVersion)
    {
        if (string.IsNullOrWhiteSpace(informationalVersion))
            return (UntaggedVersion, null);

        var separatorIndex = informationalVersion.IndexOf(BuildMetadataSeparator);
        if (separatorIndex < 0)
            return (informationalVersion.Trim(), null);

        var version = informationalVersion[..separatorIndex].Trim();
        var commit  = informationalVersion[(separatorIndex + 1)..].Trim();
        return (
            version.Length == 0 ? UntaggedVersion : version,
            commit.Length == 0 ? null : commit);
    }
}
