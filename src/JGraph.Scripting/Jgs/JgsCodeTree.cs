using System.IO;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// What the search folders hold <em>by kind</em> (the packages and class folders plan, stage P1,
/// ADR 0307): the <c>+package</c> folders under each search folder, nested as deep as they go, and
/// the files inside them. A package of one name spread over several search folders is one package
/// — R2025b measured: <c>cur/+pkg/fromcur.m</c> and <c>lib/+pkg/f.m</c> both answer, and where both
/// folders hold a member the first in search order wins — so every question here is asked folder by
/// folder in search order and the first answer stands.
/// </summary>
/// <remarks>
/// The tree keeps nothing of its own: it is a walk over the file index's folder entries, which the
/// index refreshes by the two rules every entry has — at once after the interpreter's own write
/// into a folder, and by the folder's write time at most once per statement — so a package file
/// written in one statement is called in the next, where R2025b needs <c>rehash</c> (a recorded
/// divergence in JGraph's favour, as ADR 0149's was). A dotted mention costs one set lookup per
/// search folder per segment, and nothing at all in a session with no package anywhere
/// (<see cref="JgsFileIndex.AnyPackages"/>).
/// </remarks>
internal sealed class JgsCodeTree
{
    private readonly JgsFileIndex _index;

    /// <summary>Creates the tree over <paramref name="index"/>'s search folders.</summary>
    public JgsCodeTree(JgsFileIndex index)
    {
        _index = index;
    }

    /// <summary>One folder holding a package: the search folder it sits under and the package folder itself.</summary>
    internal readonly record struct PackageHit(string SearchFolder, string Folder, JgsFileIndex.Folder Entry);

    /// <summary>
    /// Every folder holding the package named by <paramref name="parts"/> (<c>["pkg", "sub"]</c> for
    /// <c>pkg.sub</c>), in search order — empty when no search folder holds it.
    /// </summary>
    public IEnumerable<PackageHit> Hits(IReadOnlyList<string> parts)
    {
        if (parts.Count == 0 || !_index.AnyPackages)
        {
            yield break;
        }

        foreach (string root in _index.SearchFolders)
        {
            if (Descend(root, parts) is { } hit)
            {
                yield return hit;
            }
        }
    }

    private PackageHit? Descend(string root, IReadOnlyList<string> parts)
    {
        JgsFileIndex.Folder entry = _index.Track(root);
        string folder = root;
        foreach (string part in parts)
        {
            _index.StemsOf(entry); // refreshed by the index's rules before its sub-folders are read
            if (!entry.Packages.Contains(part))
            {
                return null;
            }

            folder = Path.Combine(folder, "+" + part);
            entry = _index.Track(folder);
        }

        _index.StemsOf(entry);
        return new PackageHit(root, folder, entry);
    }

    /// <summary>Whether some search folder holds the package.</summary>
    public bool IsPackage(IReadOnlyList<string> parts) => Hits(parts).Any();

    /// <summary>
    /// The folder whose <c>+package</c> holds a code file named <paramref name="member"/>, the first
    /// in search order, with the search folder it sits under; null when none does.
    /// </summary>
    public PackageHit? MemberFolder(IReadOnlyList<string> parts, string member)
    {
        foreach (PackageHit hit in Hits(parts))
        {
            if (hit.Entry.Stems.Contains(member))
            {
                return hit;
            }
        }

        return null;
    }

    /// <summary>Whether the package has a sub-package named <paramref name="name"/> in some folder.</summary>
    public bool HasSubPackage(IReadOnlyList<string> parts, string name)
    {
        foreach (PackageHit hit in Hits(parts))
        {
            if (hit.Entry.Packages.Contains(name))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// The package's code-file stems, sub-packages and class folders merged over every folder that
    /// holds it, each sorted ordinally — the listing <c>help pkg</c> prints and completion offers.
    /// </summary>
    public (IReadOnlyList<string> Members, IReadOnlyList<string> SubPackages, IReadOnlyList<string> ClassFolders) Contents(IReadOnlyList<string> parts)
    {
        var members = new SortedSet<string>(StringComparer.Ordinal);
        var subPackages = new SortedSet<string>(StringComparer.Ordinal);
        var classFolders = new SortedSet<string>(StringComparer.Ordinal);
        foreach (PackageHit hit in Hits(parts))
        {
            members.UnionWith(hit.Entry.Stems);
            subPackages.UnionWith(hit.Entry.Packages);
            classFolders.UnionWith(hit.Entry.ClassFolders);
        }

        return ([.. members], [.. subPackages], [.. classFolders]);
    }

    /// <summary>
    /// The package a file sits in, as the prefix a qualified name carries (<c>pkg.sub.</c> for a
    /// file under <c>+pkg/+sub/</c>), or "" for a file in no package — read off the path alone, so a
    /// frame can be printed as R2025b's <c>dbstack</c> prints it (<c>In pkg.sub.stk (line 2)</c>).
    /// </summary>
    public static string PackagePrefix(string file)
    {
        if (file.Length == 0)
        {
            return string.Empty;
        }

        var parts = new List<string>();
        string? folder;
        try
        {
            folder = Path.GetDirectoryName(file);
        }
        catch (Exception ex) when (ex is ArgumentException or PathTooLongException)
        {
            return string.Empty;
        }

        while (folder is not null)
        {
            string leaf = Path.GetFileName(folder);
            if (leaf.Length > 1 && leaf[0] == '+')
            {
                parts.Add(leaf[1..]);
                folder = Path.GetDirectoryName(folder);
            }
            else
            {
                break;
            }
        }

        if (parts.Count == 0)
        {
            return string.Empty;
        }

        parts.Reverse();
        return string.Join('.', parts) + ".";
    }

    /// <summary>
    /// The parts of a dotted name (<c>pkg.sub.h</c> → <c>pkg</c>, <c>sub</c>, <c>h</c>), or null when
    /// it is not a chain of at least two plain identifiers — the shape a package member's name has.
    /// </summary>
    public static string[]? DottedParts(string dotted)
    {
        if (dotted.Length == 0 || !dotted.Contains('.'))
        {
            return null;
        }

        string[] parts = dotted.Split('.');
        if (parts.Length < 2)
        {
            return null;
        }

        foreach (string part in parts)
        {
            if (part.Length == 0 || !(char.IsLetter(part[0]) || part[0] == '_'))
            {
                return null;
            }

            foreach (char c in part)
            {
                if (!(char.IsLetterOrDigit(c) || c == '_'))
                {
                    return null;
                }
            }
        }

        return parts;
    }
}
