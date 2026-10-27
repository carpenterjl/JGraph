using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;

namespace JGraph.Deploy.Projects;

/// <summary>A file or folder in a project, by its path under the root.</summary>
/// <param name="RelativePath">The path under the root, with the platform's separator.</param>
/// <param name="IsFolder">Whether it is a folder (its node carries a <c>DIR_SIGNIFIER</c> child).</param>
public readonly record struct ProjectFileEntry(string RelativePath, bool IsFolder);

/// <summary>A label attached to a project file: its category's and its own name, and the data when the category has a type.</summary>
public readonly record struct ProjectFileLabel(string CategoryName, string LabelName, string? Data);

/// <summary>A label definition: a name within a category.</summary>
public sealed record ProjectLabelDefinition(string Name, string Id, bool ReadOnly);

/// <summary>A category of labels, as <c>matlab.project.Category</c> shows one.</summary>
public sealed class ProjectCategory
{
    internal ProjectCategory(string name, string id, string dataType, bool singleValued, bool readOnly, List<ProjectLabelDefinition> labels)
    {
        Name = name;
        Id = id;
        DataType = dataType;
        SingleValued = singleValued;
        ReadOnly = readOnly;
        Labels = labels;
    }

    public string Name { get; }

    /// <summary>The node's location: <c>FileClassCategory</c>, a <c>Deployment_Category_&lt;uuid&gt;</c>, or a uuid.</summary>
    public string Id { get; }

    /// <summary>The API's data type: <c>none</c>, <c>char</c>, <c>double</c>, <c>integer</c>, <c>logical</c>.</summary>
    public string DataType { get; }

    public bool SingleValued { get; }

    /// <summary>Whether it is the default Classification category, which cannot be changed.</summary>
    public bool ReadOnly { get; }

    public IReadOnlyList<ProjectLabelDefinition> Labels { get; }
}

/// <summary>A startup file, shutdown file or shortcut: an entry point node.</summary>
/// <param name="RelativePath">The file under the root.</param>
/// <param name="Name">The entry's name (the file's name without its extension).</param>
/// <param name="Kind"><c>Basic</c> (a shortcut), <c>StartUp</c> or <c>Shutdown</c>.</param>
/// <param name="Visible">Whether it shows as a shortcut.</param>
public readonly record struct ProjectEntryPoint(string RelativePath, string Name, string Kind, bool Visible);

/// <summary>The refusals a project operation raises, in R2025b's identifiers and words (probe <c>k20_projects</c>).</summary>
public sealed class ProjectException(string identifier, string message) : Exception(message)
{
    /// <summary>R2025b's identifier: <c>MATLAB:project:api:FileDoesNotExist</c> and its kin.</summary>
    public string Identifier { get; } = identifier;
}

/// <summary>
/// A MATLAB project as R2025b stores one (compiler and SDK plan, stage K20): the <c>.prj</c> stub and
/// the <c>fixedPathV2</c> definition files under <c>resources\project</c>, read into a node tree and
/// written back after every change as MATLAB writes them, so a project JGraph makes opens in
/// MATLAB and the other way round. What stage K20 needs of projects: the name and description,
/// the files with their labels and categories, the project path, the startup and shutdown files
/// and the shortcuts, and the compiler tasks (<see cref="CompilerTaskConfiguration"/>). References,
/// source control, export and the dependency graph wait (open item 217).
/// </summary>
public sealed class MatlabProject
{
    /// <summary>The default category's node location.</summary>
    public const string ClassificationCategoryId = "FileClassCategory";

    /// <summary>The default category's name.</summary>
    public const string ClassificationCategoryName = "Classification";

    /// <summary>The name a project is created with when none is given.</summary>
    public const string DefaultName = "blank_project";

    /// <summary>The default category's labels, in the order <c>LabelDefinitions</c> lists them.</summary>
    public static readonly string[] ClassificationLabels = ["Artifact", "Convenience", "Derived", "Design", "None", "Other", "Test"];

    /// <summary>The extensions MATLAB classifies Design when a file joins a project (probes k20_projects and k21_legacy, the upgrade's labels).</summary>
    private static readonly string[] DesignExtensions = [".m", ".mlx", ".mlapp", ".p", ".slx", ".mdl", ".sfx", ".mat", ".c", ".h", ".cpp", ".hpp"];

    /// <summary>The extensions MATLAB classifies Derived: built binaries and the XML beside them.</summary>
    private static readonly string[] DerivedExtensions = [".dll", ".exe", ".lib", ".xml"];

    /// <summary>The extensions MATLAB classifies Artifact: generated HTML.</summary>
    private static readonly string[] ArtifactExtensions = [".html"];

    private readonly List<ProjectNode> _root;

    private MatlabProject(string rootFolder, string projectFile, List<ProjectNode> root)
    {
        RootFolder = rootFolder;
        ProjectFile = projectFile;
        _root = root;
    }

    /// <summary>The project's root folder, as a full path without a trailing separator.</summary>
    public string RootFolder { get; }

    /// <summary>The <c>.prj</c> stub.</summary>
    public string ProjectFile { get; }

    /// <summary>The definition files, as nodes: what every operation edits and <see cref="Save"/> writes.</summary>
    public IReadOnlyList<ProjectNode> Root => _root;

    // --- creating, opening, deleting -------------------------------------------------------------------

    /// <summary>
    /// Creates a project in <paramref name="rootFolder"/> (made when missing) named
    /// <paramref name="name"/> (<see cref="DefaultName"/> when null): the stub, the default
    /// category and its labels, and nothing else, as <c>matlab.project.createProject</c> leaves it.
    /// </summary>
    /// <exception cref="ProjectException">The folder already holds a project, or is a file.</exception>
    public static MatlabProject Create(string rootFolder, string? name = null)
    {
        ArgumentNullException.ThrowIfNull(rootFolder);
        string root = Path.GetFullPath(rootFolder).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (File.Exists(root))
        {
            throw new ProjectException("MATLAB:project:creation:RootFolderIsFile",
                $"Unable to create the specified project folder because a file \"{root}\" already exists. Delete this file, or specify a different project folder.");
        }

        if (ProjectDefinitionFiles.IsProjectRoot(root))
        {
            throw new ProjectException("MATLAB:project:creation:ProjectAlreadyExists", "The specified folder already contains a project.");
        }

        Directory.CreateDirectory(root);
        name = string.IsNullOrEmpty(name) ? DefaultName : name;
        string projectFile = Path.Combine(root, name + ".prj");
        ProjectDefinitionFiles.WriteProjectFile(projectFile);

        var nodes = new List<ProjectNode>();
        var data = new ProjectNode("ProjectData", "Info");
        data.SetAttribute("Name", name);
        nodes.Add(data);
        var categories = new ProjectNode("Root", "Categories");
        var classification = categories.EnsureChild(ClassificationCategoryId, "Category");
        classification.SetAttribute("DataType", "None");
        classification.SetAttribute("Name", ClassificationCategoryName);
        classification.SetAttribute("ReadOnly", "1");
        classification.SetAttribute("SingleValued", "1");
        foreach (string label in ClassificationLabels)
        {
            ProjectNode node = classification.EnsureChild(label.ToLowerInvariant(), "Label");
            node.SetAttribute("Name", label);
            node.SetAttribute("ReadOnly", "READ_ONLY");
        }

        nodes.Add(categories);
        var project = new MatlabProject(root, projectFile, nodes);
        project.Save();
        return project;
    }

    /// <summary>
    /// Opens the project at <paramref name="location"/>: its root folder or its <c>.prj</c> stub.
    /// </summary>
    /// <exception cref="ProjectException">Nothing is there, or what is there is not a project.</exception>
    public static MatlabProject Open(string location)
    {
        ArgumentNullException.ThrowIfNull(location);
        string full = Path.GetFullPath(location);
        if (!File.Exists(full) && !Directory.Exists(full))
        {
            throw new ProjectException("MATLAB:project:api:FileDoesNotExist", $"The file or folder \"{full}\" does not exist.");
        }

        string root = File.Exists(full) ? Path.GetDirectoryName(full)! : full;
        root = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string? projectFile = File.Exists(full) && ProjectDefinitionFiles.IsProjectFile(full) ? full : ProjectDefinitionFiles.ProjectFileIn(root);
        if (projectFile is null || !File.Exists(Path.Combine(ProjectDefinitionFiles.DefinitionFolder(root), "Project.xml")))
        {
            // R2025b names the folder for a .prj that is not a project's stub, and the file itself for any other file.
            bool prj = File.Exists(full) && Path.GetExtension(full).Equals(".prj", StringComparison.OrdinalIgnoreCase);
            throw new ProjectException("MATLAB:project:api:LoadFail", $"Unable to load the project at:\n{(File.Exists(full) && !prj ? full : root)}");
        }

        List<ProjectNode> nodes;
        try
        {
            nodes = ProjectDefinitionFiles.Read(root);
        }
        catch (InvalidDataException ex)
        {
            throw new ProjectException("MATLAB:project:api:LoadFail", $"Unable to load the project at:\n{root}\n{ex.Message}");
        }

        return new MatlabProject(root, projectFile, nodes);
    }

    /// <summary>Whether <paramref name="location"/> names a project: its root folder or its stub.</summary>
    public static bool Exists(string location)
    {
        string full = Path.GetFullPath(location);
        return File.Exists(full)
            ? ProjectDefinitionFiles.IsProjectFile(full) && ProjectDefinitionFiles.IsProjectRoot(Path.GetDirectoryName(full)!)
            : ProjectDefinitionFiles.IsProjectRoot(full);
    }

    /// <summary>
    /// Removes the project's definition files and stub from <paramref name="rootFolder"/>, keeping
    /// every other file, as <c>matlab.project.deleteProject</c> does.
    /// </summary>
    /// <exception cref="ProjectException">The folder is not there, or holds no project.</exception>
    public static void Delete(string rootFolder)
    {
        string root = Path.GetFullPath(rootFolder);
        if (!Directory.Exists(root))
        {
            throw new ProjectException("MATLAB:validators:mustBeFolder", $"Invalid argument at position 1. These folders do not exist: '{root}'.");
        }

        if (!ProjectDefinitionFiles.IsProjectRoot(root))
        {
            throw new ProjectException("MATLAB:project:management:DeleteFailNotAProject", $"The location {root.TrimEnd(Path.DirectorySeparatorChar)} is not a project.");
        }

        foreach (string stub in Directory.EnumerateFiles(root, "*.prj").Where(ProjectDefinitionFiles.IsProjectFile).ToList())
        {
            File.Delete(stub);
        }

        string definitions = ProjectDefinitionFiles.DefinitionFolder(root);
        Directory.Delete(definitions, recursive: true);
        string resources = Path.Combine(root, ProjectDefinitionFiles.ResourcesFolder);
        if (Directory.Exists(resources) && !Directory.EnumerateFileSystemEntries(resources).Any())
        {
            Directory.Delete(resources);
        }
    }

    /// <summary>Writes every definition file.</summary>
    public void Save() => ProjectDefinitionFiles.Write(RootFolder, _root);

    // --- the project's own data -----------------------------------------------------------------------

    private ProjectNode RootNode(string location, string type)
    {
        if (_root.FirstOrDefault(n => n.Type == type && n.Location == location) is { } found)
        {
            return found;
        }

        var made = new ProjectNode(location, type);
        _root.Add(made);
        return made;
    }

    private ProjectNode? FindRootNode(string location, string type) => _root.FirstOrDefault(n => n.Type == type && n.Location == location);

    /// <summary>The project's name: the <c>ProjectData</c> node's. Setting it leaves the stub's file name as it was, as R2025b does.</summary>
    public string Name
    {
        get => FindRootNode("ProjectData", "Info")?.Attribute("Name") ?? Path.GetFileNameWithoutExtension(ProjectFile);
        set
        {
            RootNode("ProjectData", "Info").SetAttribute("Name", value);
            Save();
        }
    }

    /// <summary>The description: the <c>Extensions</c> root's <c>Information</c> extension.</summary>
    public string Description
    {
        get => FindRootNode("Extensions", "Root")?.Child("Information", "Extension")?.Attribute("description") ?? "";
        set
        {
            RootNode("Extensions", "Root").EnsureChild("Information", "Extension").SetAttribute("description", value);
            Save();
        }
    }

    // --- paths ----------------------------------------------------------------------------------------

    /// <summary>The full path of a location given to the project: as it is when rooted, else under the root.</summary>
    public string FullPath(string location) =>
        Path.GetFullPath(Path.IsPathRooted(location) ? location : Path.Combine(RootFolder, location));

    /// <summary>
    /// The path under the root of <paramref name="location"/>, or null when it is not under the
    /// root (the root itself answers the empty string).
    /// </summary>
    public string? RelativePath(string location)
    {
        string full = FullPath(location).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (string.Equals(full, RootFolder, StringComparison.OrdinalIgnoreCase))
        {
            return "";
        }

        string relative = Path.GetRelativePath(RootFolder, full);
        return relative.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relative) ? null : relative;
    }

    /// <summary>Whether <paramref name="location"/> is under the root folder (the root itself is not).</summary>
    public bool IsUnderRoot(string location) => RelativePath(location) is { Length: > 0 };

    /// <summary>Whether <paramref name="relativePath"/> is or lies within the definition files.</summary>
    public static bool IsDefinitionPath(string relativePath)
    {
        string[] parts = relativePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return parts.Length >= 2 && parts[0].Equals(ProjectDefinitionFiles.ResourcesFolder, StringComparison.OrdinalIgnoreCase)
            && parts[1].Equals(ProjectDefinitionFiles.ProjectFolder, StringComparison.OrdinalIgnoreCase);
    }

    // --- files ----------------------------------------------------------------------------------------

    private ProjectNode FilesNode => RootNode("Root", "Files");

    private static string[] Segments(string relativePath) =>
        relativePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);

    private ProjectNode? FindFileNode(string relativePath)
    {
        ProjectNode? node = FindRootNode("Root", "Files");
        foreach (string segment in Segments(relativePath))
        {
            node = node?.Children.FirstOrDefault(c => c.Type == "File" && string.Equals(c.Location, segment, StringComparison.OrdinalIgnoreCase));
            if (node is null)
            {
                return null;
            }
        }

        return node;
    }

    private static bool IsFolderNode(ProjectNode node) => node.Child("1", "DIR_SIGNIFIER") is not null;

    /// <summary>Every file and folder in the project, by path under the root, in order.</summary>
    public IReadOnlyList<ProjectFileEntry> Files
    {
        get
        {
            var entries = new List<ProjectFileEntry>();
            if (FindRootNode("Root", "Files") is { } files)
            {
                Collect(files, "", entries);
            }

            entries.Sort((a, b) => string.Compare(a.RelativePath, b.RelativePath, StringComparison.OrdinalIgnoreCase));
            return entries;
        }
    }

    private static void Collect(ProjectNode parent, string prefix, List<ProjectFileEntry> entries)
    {
        foreach (ProjectNode child in parent.Children.Where(static c => c.Type == "File"))
        {
            string path = prefix.Length == 0 ? child.Location : Path.Combine(prefix, child.Location);
            bool folder = IsFolderNode(child);
            entries.Add(new ProjectFileEntry(path, folder));
            if (folder)
            {
                Collect(child, path, entries);
            }
        }
    }

    /// <summary>The entry for <paramref name="relativePath"/>, or null when it is not in the project.</summary>
    public ProjectFileEntry? FindFile(string relativePath) =>
        FindFileNode(relativePath) is { } node ? new ProjectFileEntry(Rejoin(relativePath), IsFolderNode(node)) : null;

    private static string Rejoin(string relativePath) => Path.Combine(Segments(relativePath));

    /// <summary>
    /// Adds the file or folder at <paramref name="relativePath"/> (which must exist on disk), its
    /// parent folders with it; a MATLAB code file is labelled Design, as MATLAB labels it. Adding
    /// what is already there changes nothing.
    /// </summary>
    public ProjectFileEntry AddFile(string relativePath)
    {
        string[] segments = Segments(relativePath);
        if (segments.Length == 0)
        {
            throw new ProjectException("MATLAB:project:management:RootFolderNotAddable", $"Unable to add the project root folder to the project:\n{RootFolder}");
        }

        string full = Path.Combine(RootFolder, Path.Combine(segments));
        bool isFolder = Directory.Exists(full);
        if (!isFolder && !File.Exists(full))
        {
            throw new ProjectException("MATLAB:project:api:FileDoesNotExist", $"The file or folder \"{full}\" does not exist.");
        }

        ProjectNode node = FilesNode;
        string walked = "";
        for (int i = 0; i < segments.Length; i++)
        {
            walked = walked.Length == 0 ? segments[i] : Path.Combine(walked, segments[i]);
            bool last = i == segments.Length - 1;
            ProjectNode? child = node.Children.FirstOrDefault(c => c.Type == "File" && string.Equals(c.Location, segments[i], StringComparison.OrdinalIgnoreCase));
            if (child is null)
            {
                child = node.EnsureChild(segments[i], "File");
                if (!last || isFolder)
                {
                    child.EnsureChild("1", "DIR_SIGNIFIER");
                }
                else if (DesignExtensions.Contains(Path.GetExtension(segments[i]), StringComparer.OrdinalIgnoreCase))
                {
                    AttachLabel(child, ClassificationCategoryId, "design", singleValued: true);
                }
                else if (DerivedExtensions.Contains(Path.GetExtension(segments[i]), StringComparer.OrdinalIgnoreCase))
                {
                    AttachLabel(child, ClassificationCategoryId, "derived", singleValued: true);
                }
                else if (ArtifactExtensions.Contains(Path.GetExtension(segments[i]), StringComparer.OrdinalIgnoreCase))
                {
                    AttachLabel(child, ClassificationCategoryId, "artifact", singleValued: true);
                }
            }

            node = child;
        }

        Save();
        return new ProjectFileEntry(Path.Combine(segments), isFolder);
    }

    /// <summary>Adds a folder and everything under it, skipping the definition files.</summary>
    public IReadOnlyList<ProjectFileEntry> AddFolderIncludingChildFiles(string relativePath)
    {
        var added = new List<ProjectFileEntry> { AddFile(relativePath) };
        string full = Path.Combine(RootFolder, relativePath);
        foreach (string entry in Directory.EnumerateFileSystemEntries(full, "*", SearchOption.AllDirectories).OrderBy(static e => e, StringComparer.OrdinalIgnoreCase))
        {
            string relative = Path.GetRelativePath(RootFolder, entry);
            if (IsDefinitionPath(relative) || relative.Split(Path.DirectorySeparatorChar).Contains(ProjectDefinitionFiles.ResourcesFolder, StringComparer.OrdinalIgnoreCase) && relative.StartsWith(ProjectDefinitionFiles.ResourcesFolder, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            added.Add(AddFile(relative));
        }

        return added;
    }

    /// <summary>Removes a file or folder (and what is under it) from the project; nothing happens when it is not in it.</summary>
    public void RemoveFile(string relativePath)
    {
        string[] segments = Segments(relativePath);
        if (segments.Length == 0)
        {
            throw new ProjectException("MATLAB:project:management:RootFolderNotRemoveable", $"Unable to remove the project root folder from the project:\n{RootFolder}");
        }

        ProjectNode? parent = FindRootNode("Root", "Files");
        for (int i = 0; i < segments.Length - 1 && parent is not null; i++)
        {
            parent = parent.Children.FirstOrDefault(c => c.Type == "File" && string.Equals(c.Location, segments[i], StringComparison.OrdinalIgnoreCase));
        }

        ProjectNode? node = parent?.Children.FirstOrDefault(c => c.Type == "File" && string.Equals(c.Location, segments[^1], StringComparison.OrdinalIgnoreCase));
        if (parent is null || node is null)
        {
            return;
        }

        parent.Children.Remove(node);
        Save();
    }

    // --- labels and categories -------------------------------------------------------------------------

    private static string DiskDataType(string apiType) => apiType switch
    {
        "char" => "String",
        "double" => "Double",
        "integer" => "Integer",
        "logical" => "Logical",
        _ => "None",
    };

    private static string ApiDataType(string diskType) => diskType switch
    {
        "String" => "char",
        "Double" => "double",
        "Integer" => "integer",
        "Logical" => "logical",
        _ => "none",
    };

    /// <summary>Every category, by name, each with its labels in order.</summary>
    public IReadOnlyList<ProjectCategory> Categories
    {
        get
        {
            var list = new List<ProjectCategory>();
            if (FindRootNode("Root", "Categories") is { } categories)
            {
                foreach (ProjectNode node in categories.Children.Where(static c => c.Type == "Category"))
                {
                    list.Add(ReadCategory(node));
                }
            }

            list.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));
            return list;
        }
    }

    private static ProjectCategory ReadCategory(ProjectNode node)
    {
        var labels = node.Children.Where(static c => c.Type == "Label")
            .Select(c => new ProjectLabelDefinition(c.Attribute("Name"), c.Location, c.Attribute("ReadOnly") == "READ_ONLY"))
            .OrderBy(static l => l.Name, StringComparer.Ordinal)
            .ToList();
        return new ProjectCategory(node.Attribute("Name"), node.Location, ApiDataType(node.Attribute("DataType", "None")),
            node.Attribute("SingleValued") == "1", node.Attribute("ReadOnly") == "1", labels);
    }

    private ProjectNode? CategoryNode(string name) =>
        FindRootNode("Root", "Categories")?.Children.FirstOrDefault(c => c.Type == "Category" && c.Attribute("Name") == name);

    private ProjectNode? CategoryNodeById(string id) =>
        FindRootNode("Root", "Categories")?.Children.FirstOrDefault(c => c.Type == "Category" && c.Location == id);

    /// <summary>The category named <paramref name="name"/>, or null.</summary>
    public ProjectCategory? FindCategory(string name) => CategoryNode(name) is { } node ? ReadCategory(node) : null;

    /// <summary>
    /// Creates a category; one with the name already there is answered as it is (R2025b's
    /// <c>createCategory</c> does not refuse it).
    /// </summary>
    public ProjectCategory CreateCategory(string name, string dataType = "none", bool singleValued = false)
    {
        if (CategoryNode(name) is { } existing)
        {
            return ReadCategory(existing);
        }

        ProjectNode node = RootNode("Root", "Categories").EnsureChild(Guid.NewGuid().ToString(), "Category");
        node.SetAttribute("DataType", DiskDataType(dataType));
        node.SetAttribute("Name", name);
        node.SetAttribute("ReadOnly", "0");
        node.SetAttribute("SingleValued", singleValued ? "1" : "0");
        Save();
        return ReadCategory(node);
    }

    /// <summary>Creates a category whose node location is <paramref name="id"/> (a compiler task's <c>Deployment_Category_&lt;uuid&gt;</c>).</summary>
    internal ProjectNode CreateCategoryNode(string id, string name)
    {
        ProjectNode node = RootNode("Root", "Categories").EnsureChild(id, "Category");
        node.SetAttribute("DataType", "None");
        node.SetAttribute("Name", name);
        node.SetAttribute("ReadOnly", "0");
        node.SetAttribute("SingleValued", "0");
        return node;
    }

    /// <summary>Removes a category and every label of it from every file; the default category refuses, a missing one is nothing.</summary>
    public void RemoveCategory(string name)
    {
        ProjectNode? node = CategoryNode(name);
        if (node is null)
        {
            return;
        }

        if (node.Attribute("ReadOnly") == "1")
        {
            throw new ProjectException("MATLAB:project:management:CanNotModifyReadOnlyCategory", "It is not possible to modify a default category.");
        }

        RemoveCategoryNode(node);
        Save();
    }

    internal void RemoveCategoryNode(ProjectNode node)
    {
        FindRootNode("Root", "Categories")?.Children.Remove(node);
        if (FindRootNode("Root", "Files") is { } files)
        {
            DetachCategory(files, node.Location);
        }
    }

    private static void DetachCategory(ProjectNode parent, string categoryId)
    {
        foreach (ProjectNode child in parent.Children.Where(static c => c.Type == "File"))
        {
            child.Info.Elements("Category").Where(c => (string?)c.Attribute("UUID") == categoryId).Remove();
            DetachCategory(child, categoryId);
        }
    }

    /// <summary>Creates a label in a category; one already there is answered as it is.</summary>
    /// <exception cref="ProjectException">The category does not exist.</exception>
    public ProjectLabelDefinition CreateLabel(string categoryName, string labelName)
    {
        ProjectNode category = CategoryNode(categoryName)
            ?? throw new ProjectException("MATLAB:project:api:CategoryDoesNotExist", $"The category \"{categoryName}\" does not exist in the current project.");
        ProjectNode node = LabelNode(category, labelName) ?? MakeLabel(category, Guid.NewGuid().ToString(), labelName);
        Save();
        return new ProjectLabelDefinition(node.Attribute("Name"), node.Location, node.Attribute("ReadOnly") == "READ_ONLY");
    }

    internal static ProjectNode MakeLabel(ProjectNode category, string id, string name)
    {
        ProjectNode node = category.EnsureChild(id, "Label");
        node.SetAttribute("Name", name);
        node.SetAttribute("ReadOnly", "WRITABLE");
        return node;
    }

    private static ProjectNode? LabelNode(ProjectNode category, string labelName) =>
        category.Children.FirstOrDefault(c => c.Type == "Label" && c.Attribute("Name") == labelName);

    /// <summary>Removes a label definition and its uses; a default label refuses, a missing one is nothing.</summary>
    public void RemoveLabelDefinition(string categoryName, string labelName)
    {
        ProjectNode? category = CategoryNode(categoryName);
        ProjectNode? label = category is null ? null : LabelNode(category, labelName);
        if (category is null || label is null)
        {
            return;
        }

        if (label.Attribute("ReadOnly") == "READ_ONLY")
        {
            throw new ProjectException("MATLAB:project:management:CanNotModifyReadOnlyLabel", "It is not possible to modify a default label.");
        }

        category.Children.Remove(label);
        if (FindRootNode("Root", "Files") is { } files)
        {
            DetachLabel(files, category.Location, label.Location);
        }

        Save();
    }

    private static void DetachLabel(ProjectNode parent, string categoryId, string labelId)
    {
        foreach (ProjectNode child in parent.Children.Where(static c => c.Type == "File"))
        {
            foreach (XElement category in child.Info.Elements("Category").Where(c => (string?)c.Attribute("UUID") == categoryId).ToList())
            {
                category.Elements("Label").Where(l => (string?)l.Attribute("UUID") == labelId).Remove();
                if (!category.HasElements)
                {
                    category.Remove();
                }
            }

            if (child.Child("Data", "LabelData") is { } data)
            {
                data.Info.Elements("Label").Where(l => (string?)l.Attribute("UUID") == labelId).Remove();
                if (!data.Info.HasElements)
                {
                    child.Children.Remove(data);
                }
            }

            DetachLabel(child, categoryId, labelId);
        }
    }

    /// <summary>The labels on a file, in the file's own order.</summary>
    public IReadOnlyList<ProjectFileLabel> LabelsOf(string relativePath)
    {
        var labels = new List<ProjectFileLabel>();
        if (FindFileNode(relativePath) is not { } node)
        {
            return labels;
        }

        XElement? data = node.Child("Data", "LabelData")?.Info;
        foreach (XElement category in node.Info.Elements("Category"))
        {
            string categoryId = (string?)category.Attribute("UUID") ?? "";
            ProjectNode? categoryNode = CategoryNodeById(categoryId);
            foreach (XElement label in category.Elements("Label"))
            {
                string labelId = (string?)label.Attribute("UUID") ?? "";
                ProjectNode? labelNode = categoryNode?.Children.FirstOrDefault(c => c.Type == "Label" && c.Location == labelId);
                string? value = data?.Elements("Label").FirstOrDefault(l => (string?)l.Attribute("UUID") == labelId)?.Attribute("Value")?.Value;
                labels.Add(new ProjectFileLabel(categoryNode?.Attribute("Name") ?? categoryId, labelNode?.Attribute("Name") ?? labelId, value));
            }
        }

        return labels;
    }

    /// <summary>
    /// Attaches a label to a file, creating the label when the category has none of the name; a
    /// single-valued category drops the file's other label of it. <paramref name="data"/> is kept
    /// for a category with a data type.
    /// </summary>
    /// <exception cref="ProjectException">The category does not exist, or the file is not in the project.</exception>
    public ProjectFileLabel AddLabel(string relativePath, string categoryName, string labelName, string? data = null)
    {
        ProjectNode category = CategoryNode(categoryName)
            ?? throw new ProjectException("MATLAB:project:api:CategoryDoesNotExist", $"The category \"{categoryName}\" does not exist in the current project.");
        ProjectNode file = FindFileNode(relativePath)
            ?? throw new ProjectException("MATLAB:project:api:FileIsNotInProject", $"The file or folder \"{FullPath(relativePath)}\" is not in the current project.");
        ProjectNode label = LabelNode(category, labelName) ?? MakeLabel(category, Guid.NewGuid().ToString(), labelName);
        AttachLabel(file, category.Location, label.Location, category.Attribute("SingleValued") == "1");
        if (data is not null && category.Attribute("DataType", "None") != "None")
        {
            ProjectNode dataNode = file.EnsureChild("Data", "LabelData");
            XElement? entry = dataNode.Info.Elements("Label").FirstOrDefault(l => (string?)l.Attribute("UUID") == label.Location);
            if (entry is null)
            {
                entry = new XElement("Label", new XAttribute("UUID", label.Location));
                dataNode.Info.Add(entry);
            }

            entry.SetAttributeValue("Value", data);
        }

        Save();
        return new ProjectFileLabel(categoryName, labelName, data);
    }

    internal static void AttachLabel(ProjectNode file, string categoryId, string labelId, bool singleValued)
    {
        XElement? category = file.Info.Elements("Category").FirstOrDefault(c => (string?)c.Attribute("UUID") == categoryId);
        if (category is null)
        {
            category = new XElement("Category", new XAttribute("UUID", categoryId));
            file.Info.Add(category);
        }

        if (singleValued)
        {
            category.Elements("Label").Remove();
        }

        if (!category.Elements("Label").Any(l => (string?)l.Attribute("UUID") == labelId))
        {
            category.Add(new XElement("Label", new XAttribute("UUID", labelId)));
        }
    }

    /// <summary>Detaches a label from a file; nothing happens when it is not there.</summary>
    public void RemoveLabel(string relativePath, string categoryName, string labelName)
    {
        ProjectNode? category = CategoryNode(categoryName);
        ProjectNode? label = category is null ? null : LabelNode(category, labelName);
        ProjectNode? file = FindFileNode(relativePath);
        if (category is null || label is null || file is null)
        {
            return;
        }

        DetachLabelFrom(file, category.Location, label.Location);
        Save();
    }

    internal static void DetachLabelFrom(ProjectNode file, string categoryId, string labelId)
    {
        foreach (XElement category in file.Info.Elements("Category").Where(c => (string?)c.Attribute("UUID") == categoryId).ToList())
        {
            category.Elements("Label").Where(l => (string?)l.Attribute("UUID") == labelId).Remove();
            if (!category.HasElements)
            {
                category.Remove();
            }
        }

        if (file.Child("Data", "LabelData") is { } data)
        {
            data.Info.Elements("Label").Where(l => (string?)l.Attribute("UUID") == labelId).Remove();
            if (!data.Info.HasElements)
            {
                file.Children.Remove(data);
            }
        }
    }

    /// <summary>The files carrying a label, by relative path, in order.</summary>
    public IReadOnlyList<string> FilesWithLabel(string categoryId, string labelId)
    {
        var found = new List<string>();
        if (FindRootNode("Root", "Files") is { } files)
        {
            CollectLabelled(files, "", categoryId, labelId, found);
        }

        found.Sort(StringComparer.OrdinalIgnoreCase);
        return found;
    }

    private static void CollectLabelled(ProjectNode parent, string prefix, string categoryId, string labelId, List<string> found)
    {
        foreach (ProjectNode child in parent.Children.Where(static c => c.Type == "File"))
        {
            string path = prefix.Length == 0 ? child.Location : Path.Combine(prefix, child.Location);
            if (child.Info.Elements("Category").Any(c => (string?)c.Attribute("UUID") == categoryId && c.Elements("Label").Any(l => (string?)l.Attribute("UUID") == labelId)))
            {
                found.Add(path);
            }

            CollectLabelled(child, path, categoryId, labelId, found);
        }
    }

    /// <summary>The file node for a path, or null (for the task configuration's labels).</summary>
    internal ProjectNode? FileNode(string relativePath) => FindFileNode(relativePath);

    // --- the project path ----------------------------------------------------------------------------

    /// <summary>The folders on the project path, under the root; the empty string is the root itself. In order.</summary>
    public IReadOnlyList<string> PathFolders
    {
        get
        {
            var folders = FindRootNode("Root", "ProjectPath")?.Children.Where(static c => c.Type == "Reference").Select(c => c.Attribute("Ref")).ToList() ?? [];
            folders.Sort(StringComparer.OrdinalIgnoreCase);
            return folders;
        }
    }

    /// <summary>Adds a folder to the project path; one already there changes nothing.</summary>
    /// <exception cref="ProjectException">The folder is outside the root, or is not a folder.</exception>
    public string AddPath(string location)
    {
        string? relative = RelativePath(location);
        if (relative is null)
        {
            throw new ProjectException("MATLAB:project:api:PathNotInProject", $"The location \"{FullPath(location)}\" is not a folder in the project root folder.");
        }

        if (!Directory.Exists(FullPath(location)))
        {
            throw new ProjectException("MATLAB:project:api:IsNotDirectory", $"The location \"{FullPath(location)}\" is not a folder");
        }

        ProjectNode path = RootNode("Root", "ProjectPath");
        if (!path.Children.Any(c => c.Type == "Reference" && string.Equals(c.Attribute("Ref"), relative, StringComparison.OrdinalIgnoreCase)))
        {
            ProjectNode node = path.EnsureChild(Guid.NewGuid().ToString(), "Reference");
            node.SetAttribute("Ref", relative);
            node.SetAttribute("Type", "Relative");
            Save();
        }

        return relative;
    }

    /// <summary>Removes a folder from the project path; one not there is nothing.</summary>
    public void RemovePath(string location)
    {
        string? relative = RelativePath(location);
        if (relative is null || FindRootNode("Root", "ProjectPath") is not { } path)
        {
            return;
        }

        int removed = path.Children.RemoveAll(c => c.Type == "Reference" && string.Equals(c.Attribute("Ref"), relative, StringComparison.OrdinalIgnoreCase));
        if (removed > 0)
        {
            Save();
        }
    }

    // --- entry points -----------------------------------------------------------------------------------

    /// <summary>Every entry point: startup files, shutdown files and shortcuts.</summary>
    public IReadOnlyList<ProjectEntryPoint> EntryPoints =>
        FindRootNode("Root", "EntryPoints")?.Children.Where(static c => c.Type == "EntryPoint")
            .Select(c => new ProjectEntryPoint(c.Attribute("File"), c.Attribute("Name"), c.Attribute("Type", "Basic"), c.Attribute("Visible", "1") == "1"))
            .OrderBy(static e => e.Name, StringComparer.Ordinal)
            .ToList() ?? [];

    /// <summary>The startup files, in their order.</summary>
    public IReadOnlyList<string> StartupFiles => OrderedEntries("StartUp");

    /// <summary>The shutdown files, in their order.</summary>
    public IReadOnlyList<string> ShutdownFiles => OrderedEntries("Shutdown");

    /// <summary>The shortcuts: the visible entry points.</summary>
    public IReadOnlyList<ProjectEntryPoint> Shortcuts => EntryPoints.Where(static e => e.Visible).ToList();

    private IReadOnlyList<string> OrderedEntries(string kind)
    {
        List<ProjectNode> nodes = FindRootNode("Root", "EntryPoints")?.Children.Where(c => c.Type == "EntryPoint" && c.Attribute("Type") == kind).ToList() ?? [];
        // Each entry names the one before it (StartUpPrev), so the chain gives the order.
        var ordered = new List<ProjectNode>();
        var remaining = new List<ProjectNode>(nodes);
        string? previous = null;
        while (remaining.Count > 0)
        {
            ProjectNode? next = remaining.FirstOrDefault(n => (n.Info.Elements("Extension").FirstOrDefault(e => (string?)e.Attribute("Name") == kind + "Prev")?.Attribute("Value")?.Value ?? "") == (previous ?? ""))
                ?? remaining[0];
            ordered.Add(next);
            remaining.Remove(next);
            previous = next.Location;
        }

        return ordered.Select(n => n.Attribute("File")).ToList();
    }

    private ProjectNode EntryPointFor(string relativePath, out bool made)
    {
        ProjectNode entries = RootNode("Root", "EntryPoints");
        ProjectNode? node = entries.Children.FirstOrDefault(c => c.Type == "EntryPoint" && string.Equals(c.Attribute("File"), relativePath, StringComparison.OrdinalIgnoreCase));
        made = node is null;
        if (node is null)
        {
            node = entries.EnsureChild(Guid.NewGuid().ToString(), "EntryPoint");
            node.SetAttribute("File", relativePath);
            node.SetAttribute("GroupUUID", "default");
            node.SetAttribute("Icon", "");
            node.SetAttribute("Name", Path.GetFileNameWithoutExtension(relativePath));
            node.SetAttribute("Type", "Basic");
            node.SetAttribute("Visible", "0");
        }

        return node;
    }

    private string EntryFile(string location, string what)
    {
        string? relative = RelativePath(location);
        string full = FullPath(location);
        if (relative is not { Length: > 0 } || !File.Exists(full))
        {
            throw new ProjectException("MATLAB:project:api:FileDoesNotExistUnderRoot", $"The location \"{full}\" is not a file in the project root folder.");
        }

        _ = what;
        return relative;
    }

    /// <summary>Makes a file a startup file (adding it to the project when needed); answers its full path.</summary>
    public string AddStartupFile(string location) => AddRunFile(location, "StartUp");

    /// <summary>Makes a file a shutdown file; answers its full path.</summary>
    public string AddShutdownFile(string location) => AddRunFile(location, "Shutdown");

    private string AddRunFile(string location, string kind)
    {
        string relative = EntryFile(location, kind);
        if (FindFileNode(relative) is null)
        {
            AddFile(relative);
        }

        ProjectNode node = EntryPointFor(relative, out _);
        string? last = FindRootNode("Root", "EntryPoints")?.Children
            .Where(c => c.Type == "EntryPoint" && c.Attribute("Type") == kind && !ReferenceEquals(c, node))
            .Select(c => c.Location).LastOrDefault();
        node.SetAttribute("Type", kind);
        if (kind == "Shutdown" && node.Attribute("Visible") == "0")
        {
            node.SetAttribute("Visible", "0");
        }

        node.Info.Elements("Extension").Where(e => (string?)e.Attribute("Name") == kind + "Prev").Remove();
        if (last is not null)
        {
            node.Info.Add(new XElement("Extension", new XAttribute("Name", kind + "Prev"), new XAttribute("Value", last)));
        }

        Save();
        return FullPath(relative);
    }

    /// <summary>Takes a startup file back to a plain shortcut, or drops its entry; nothing when it was none.</summary>
    public void RemoveStartupFile(string location) => RemoveRunFile(location, "StartUp");

    /// <summary>Drops a shutdown file; nothing when it was none.</summary>
    public void RemoveShutdownFile(string location) => RemoveRunFile(location, "Shutdown");

    private void RemoveRunFile(string location, string kind)
    {
        string? relative = RelativePath(location);
        ProjectNode? entries = FindRootNode("Root", "EntryPoints");
        ProjectNode? node = relative is null ? null : entries?.Children.FirstOrDefault(c => c.Type == "EntryPoint" && string.Equals(c.Attribute("File"), relative, StringComparison.OrdinalIgnoreCase) && c.Attribute("Type") == kind);
        if (node is null || entries is null)
        {
            return;
        }

        if (node.Attribute("Visible") == "1" && kind == "StartUp")
        {
            node.SetAttribute("Type", "Basic");
            node.Info.Elements("Extension").Remove();
        }
        else
        {
            entries.Children.Remove(node);
        }

        Save();
    }

    /// <summary>Makes a file a shortcut; answers the entry.</summary>
    /// <exception cref="ProjectException">A folder, or a file that is not under the root.</exception>
    public ProjectEntryPoint AddShortcut(string location)
    {
        string relative = EntryFile(location, "shortcut");
        if (FindFileNode(relative) is null)
        {
            AddFile(relative);
        }

        ProjectNode node = EntryPointFor(relative, out _);
        node.SetAttribute("Visible", "1");
        Save();
        return new ProjectEntryPoint(relative, node.Attribute("Name"), node.Attribute("Type"), true);
    }

    /// <summary>Drops a shortcut (a startup or shutdown file keeps its entry, hidden); nothing when it was none.</summary>
    public void RemoveShortcut(string location)
    {
        string? relative = RelativePath(location);
        ProjectNode? entries = FindRootNode("Root", "EntryPoints");
        ProjectNode? node = relative is null ? null : entries?.Children.FirstOrDefault(c => c.Type == "EntryPoint" && string.Equals(c.Attribute("File"), relative, StringComparison.OrdinalIgnoreCase));
        if (node is null || entries is null || node.Attribute("Visible") != "1")
        {
            return;
        }

        if (node.Attribute("Type", "Basic") == "Basic")
        {
            entries.Children.Remove(node);
        }
        else
        {
            node.SetAttribute("Visible", "0");
        }

        Save();
    }

    // --- compiler tasks ---------------------------------------------------------------------------------

    private const string ConfigsLocation = "MathWorksDeploymentConfigs";

    private static string ConfigLocation(string uuid) => $"MathWorksDeploymentConfig{{{uuid}}}";

    /// <summary>The name-to-configuration map the <c>MathWorksDeploymentConfigs</c> node keeps.</summary>
    private SortedDictionary<string, string> ConfigMap()
    {
        var map = new SortedDictionary<string, string>(StringComparer.Ordinal);
        string json = FindRootNode(ConfigsLocation, "Files")?.Attribute("configMapData") ?? "";
        if (json.Length > 0)
        {
            try
            {
                if (JsonNode.Parse(json) is JsonObject entries)
                {
                    foreach ((string key, JsonNode? value) in entries)
                    {
                        map[key] = value?.GetValue<string>() ?? "";
                    }
                }
            }
            catch (JsonException)
            {
            }
        }

        return map;
    }

    private void WriteConfigMap(SortedDictionary<string, string> map)
    {
        var entries = new JsonObject();
        foreach ((string key, string value) in map)
        {
            entries[key] = value;
        }

        RootNode(ConfigsLocation, "Files").SetAttribute("configMapData", entries.ToJsonString(new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
    }

    /// <summary>Every compiler task in the project, by name.</summary>
    public IReadOnlyList<CompilerTaskConfiguration> Tasks
    {
        get
        {
            var tasks = new List<CompilerTaskConfiguration>();
            foreach ((string name, string location) in ConfigMap())
            {
                if (FindRootNode(location, "Files") is { } node)
                {
                    tasks.Add(new CompilerTaskConfiguration(this, node, name));
                }
            }

            return tasks;
        }
    }

    /// <summary>The task named <paramref name="name"/> (exact case), or null.</summary>
    public CompilerTaskConfiguration? FindTask(string name) =>
        ConfigMap().TryGetValue(name, out string? location) && FindRootNode(location, "Files") is { } node
            ? new CompilerTaskConfiguration(this, node, name)
            : null;

    /// <summary>
    /// The task named <paramref name="name"/> of the type <paramref name="type"/> (R2025b's
    /// <c>StandaloneDesktopApp</c> and its kin), made when missing with its category and labels, as
    /// <c>compiler.project.get*Configuration</c> makes one.
    /// </summary>
    /// <exception cref="ProjectException">The name is taken by a task of another type, or is not a task name.</exception>
    public CompilerTaskConfiguration GetOrCreateTask(string type, string name)
    {
        CompilerTaskConfiguration.CheckName(name);
        if (FindTask(name) is { } found)
        {
            if (found.Type != type)
            {
                throw new ProjectException("deployment:exception:GET_CONFIGURATION_FAILURE_UNKNOWN", $"Unable to create or retrieve the requested configuration \"{name}\".");
            }

            return found;
        }

        CompilerTaskKind kind = CompilerTaskKinds.ByType(type, name)
            ?? throw new ProjectException("deployment:configmgr:ERROR_INVALID_CONFIG_TYPE", $"Unknown task type \"{type}\" was specified.");
        string uniqueId = Guid.NewGuid().ToString();
        string configId = Guid.NewGuid().ToString();
        var node = new ProjectNode(ConfigLocation(configId), "Files");
        node.EnsureChild("1", "DIR_SIGNIFIER");
        node.SetAttribute("displayName", name);
        node.SetAttribute("type", type);
        node.SetAttribute("uniqueId", uniqueId);
        _root.Add(node);

        string categoryId = $"Deployment_Category_{uniqueId}";
        ProjectNode category = CreateCategoryNode(categoryId, $"{kind.CategoryTitle} - {name}");
        foreach ((string key, string labelName) in kind.Labels)
        {
            MakeLabel(category, $"Deployment_{key}_{uniqueId}", labelName);
        }

        var task = new CompilerTaskConfiguration(this, node, name);
        task.WriteModel(CompilerTaskConfiguration.NewModel(kind, name, uniqueId));
        SortedDictionary<string, string> map = ConfigMap();
        map[name] = node.Location;
        WriteConfigMap(map);
        Save();
        return task;
    }

    /// <summary>Renames a task: its name in the map, its display name, its category's name, and its output folders when they were the defaults.</summary>
    /// <exception cref="ProjectException">No such task, or the new name is taken.</exception>
    public void RenameTask(string name, string newName)
    {
        CompilerTaskConfiguration.CheckName(newName);
        SortedDictionary<string, string> map = ConfigMap();
        if (!map.TryGetValue(name, out string? location) || FindRootNode(location, "Files") is not { } node)
        {
            throw new ProjectException("deployment:configmgr:ERROR_RENAME_NOT_FOUND", $"Failed to rename \"{name}\" to \"{newName}\", it was not found.");
        }

        if (map.ContainsKey(newName))
        {
            throw new ProjectException("deployment:configmgr:ERROR_RENAME_ALREADY_EXISTS", $"Failed to rename \"{name}\" to \"{newName}\", the new name is already in use.");
        }

        var task = new CompilerTaskConfiguration(this, node, name);
        task.Rename(newName);
        map.Remove(name);
        map[newName] = location;
        WriteConfigMap(map);
        Save();
    }

    /// <summary>Removes a task: its node, its category and labels, and its map entry.</summary>
    /// <exception cref="ProjectException">No such task.</exception>
    public void RemoveTask(string name)
    {
        SortedDictionary<string, string> map = ConfigMap();
        if (!map.TryGetValue(name, out string? location) || FindRootNode(location, "Files") is not { } node)
        {
            throw new ProjectException("deployment:configmgr:ERROR_REMOVE_NOT_FOUND", $"Failed to remove \"{name}\", it was not found.");
        }

        string uniqueId = node.Attribute("uniqueId");
        if (CategoryNodeById($"Deployment_Category_{uniqueId}") is { } category)
        {
            RemoveCategoryNode(category);
        }

        _root.Remove(node);
        map.Remove(name);
        WriteConfigMap(map);
        Save();
    }
}
