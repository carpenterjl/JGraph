function probe_k20_projects(work)
%PROBE_K20_PROJECTS Asks R2025b's MATLAB Projects and compiler tasks every question a session without
%a desktop can answer (compiler and SDK plan, stage K20): matlab.project.createProject, openProject,
%currentProject and their siblings; a project's files, path, startup and shutdown files, shortcuts,
%labels and categories; what the project writes under resources\project (the fixedPathV2 definition
%files); the compiler.project.get*Configuration tasks of every target, their properties, what each
%set stores, and the deployment script compiler.internal.deployScriptGenerator.DeployScriptBuilder
%writes for them; closing, reopening, a second project, deleting one; and the catalog texts the
%Compiler Task Manager is made of. Run under matlab -batch -noFigureWindows; nothing opens a window.
%One key<TAB>value line per question, what a call printed as indented lines under <key>.printed,
%what a step added to, changed or removed from the project's definition files under tree.<label>.
beep('off');
here = fileparts(mfilename('fullpath'));
addpath(fullfile(here, 'src'));
if exist(work, 'dir'), rmdir(work, 's'); end
mkdir(work);
src = fullfile(work, 'src');
mkdir(src);
old = cd(src);
cleanup = onCleanup(@() cd(old)); %#ok<NASGU>
writeText(fullfile(src, 'k20note.txt'), sprintf('hello\n'));
writeText(fullfile(src, 'k20stub.prj'), sprintf(['<?xml version="1.0" encoding="UTF-8"?>\n' ...
    '<deployment-project plugin="plugin.ezdeploy" plugin-version="1.0">\n' ...
    '  <configuration build-checksum="1" file="k20stub.prj" location="." name="k20stub" target="target.ezdeploy.standalone" target-name="Application Compiler">\n' ...
    '    <param.appname>k20stub</param.appname>\n' ...
    '  </configuration>\n' ...
    '</deployment-project>\n']));
mkdir(fullfile(src, 'plain'));
writeText(fullfile(src, 'plain', 'p.m'), sprintf('disp(1)\n'));

%% The names: existence, counts, where they are, and their first help line.
names = {'matlab.project.createProject', 'openProject', 'currentProject', 'matlab.project.currentProject', ...
    'matlab.project.loadProject', 'matlab.project.rootProject', 'matlab.project.deleteProject', ...
    'matlab.project.isFileInProject', 'matlab.project.isUnderProjectRoot', 'matlab.project.extractProject', ...
    'matlab.project.convertDefinitionFiles', 'matlab.project.show', 'matlab.project.Project', ...
    'matlab.project.ProjectFile', 'matlab.project.Label', 'matlab.project.LabelDefinition', ...
    'matlab.project.Category', 'matlab.project.PathFolder', 'matlab.project.Shortcut', ...
    'matlab.project.DefinitionFiles', 'simulinkproject', ...
    'compiler.project.getStandaloneDesktopAppConfiguration', 'compiler.project.getProductionServerArchiveConfiguration', ...
    'compiler.project.getPythonLibraryConfiguration', 'compiler.project.getDotNETLibraryConfiguration', ...
    'compiler.project.getCppSharedLibraryConfiguration', 'compiler.project.getCSharedLibraryConfiguration', ...
    'compiler.project.getExcelAddInConfiguration', 'compiler.project.getJavaLibraryConfiguration', ...
    'compiler.project.getCOMLibraryConfiguration', 'compiler.project.getWebAppConfiguration', ...
    'compiler.project.getToolboxConfiguration', 'compiler.internal.project.rename', ...
    'compiler.internal.project.remove', 'compiler.internal.project.openEditor', ...
    'compiler.internal.deployScriptGenerator.DeployScriptBuilder', 'compiler.internal.DeploymentTarget', ...
    'compiler.internal.PackageType', 'deployment.compiler.model.MCRInstallType', ...
    'compiler.internal.ui.getStandaloneDeploymentScript'};
for k = 1:numel(names)
    n = names{k};
    k0_try(['exist.' n], @() exist(n));
    k0_try(['nargin.' n], @() nargin(n));
    k0_try(['nargout.' n], @() nargout(n));
    k0_try(['which.' n], @() which(n));
    k0_try(['h1.' n], @() firstLine(evalc(['help ' n])));
end
k0_try('enum.DeploymentTarget', @() cellstr(string(enumeration('compiler.internal.DeploymentTarget')))');
k0_try('enum.PackageType', @() cellstr(string(enumeration('compiler.internal.PackageType')))');
k0_try('enum.MCRInstallType', @() cellstr(string(enumeration('deployment.compiler.model.MCRInstallType')))');
k0_try('enum.DefinitionFiles', @() cellstr(string(enumeration('matlab.project.DefinitionFiles')))');

%% No project loaded.
k0_try('none.currentProject', @() currentProject());
k0_try('none.matlab.project.currentProject', @() matlab.project.currentProject());
k0_try('none.rootProject', @() matlab.project.rootProject());
k0_try('none.isFileInProject', @() matlab.project.isFileInProject(fullfile(src, 'k20note.txt')));
k0_try('none.isUnderProjectRoot', @() matlab.project.isUnderProjectRoot(fullfile(src, 'k20note.txt')));
k0_try('none.isUnderProjectRoot.two', @() matlab.project.isUnderProjectRoot({fullfile(src, 'k20note.txt'), src}));
k0_try('none.isUnderProjectRoot.number', @() matlab.project.isUnderProjectRoot(1));
k0_try('none.openProject.missing', @() openProject(fullfile(work, 'nope')));
k0_try('none.openProject.folder', @() openProject(fullfile(src, 'plain')));
k0_try('none.openProject.txt', @() openProject(fullfile(src, 'k20note.txt')));
k0_try('none.openProject.legacyprj', @() openProject(fullfile(src, 'k20stub.prj')));
k0_try('none.openProject.number', @() openProject(1));
k0_try('none.openProject.none', @() openProject());
k0_try('none.openProject.string', @() openProject(string(fullfile(work, 'nope'))));
k0_try('none.loadProject.missing', @() matlab.project.loadProject(fullfile(work, 'nope')));
call('none.deleteProject.folder', @() matlab.project.deleteProject(fullfile(src, 'plain')));
call('none.deleteProject.missing', @() matlab.project.deleteProject(fullfile(work, 'nope')));
k0_try('none.createProject.number', @() matlab.project.createProject(1));
k0_try('none.createProject.file', @() matlab.project.createProject(fullfile(src, 'k20note.txt')));
k0_try('none.createProject.badnv', @() matlab.project.createProject('Bogus', 'x'));
k0_try('none.createProject.three', @() matlab.project.createProject('a', 'b', 'c'));
k0_try('none.currentProject.arg', @() currentProject(1));

%% A project: created, its properties, and what it writes.
root = fullfile(work, 'proj1');
call('create', @() matlab.project.createProject(root));
proj = currentProject;
k0_try('create.class', @() class(proj));
call('create.disp', @() disp(proj));
props = properties(proj);
for k = 1:numel(props)
    k0_try(['create.prop.' props{k}], @() proj.(props{k}));
end
k0_try('create.prop.DefinitionFilesType.string', @() string(proj.DefinitionFilesType));
k0_try('create.prop.Categories.Name', @() proj.Categories.Name);
k0_try('create.prop.Categories.DataType', @() proj.Categories.DataType);
k0_try('create.prop.Categories.SingleValued', @() proj.Categories.SingleValued);
k0_try('create.prop.Categories.LabelDefinitions', @() {proj.Categories.LabelDefinitions.Name});
k0_try('create.methods', @() methods(proj)');
k0_try('create.prjfile', @() exist(fullfile(root, 'blank_project.prj'), 'file'));
k0_try('create.current.eq', @() currentProject == proj);
k0_try('create.rootProject', @() matlab.project.rootProject().Name);
k0_try('create.isLoaded', @() isLoaded(proj));
k0_try('create.isFileInProject.root', @() matlab.project.isFileInProject(root));
k0_try('create.isUnderProjectRoot.root', @() matlab.project.isUnderProjectRoot(root));
k0_try('create.isUnderProjectRoot.outside', @() matlab.project.isUnderProjectRoot(src));
k0_try('create.pwd.changed', @() strcmp(pwd, root));
tree = containers.Map('KeyType', 'char', 'ValueType', 'any');
tree = snapshot('create', root, tree);
ids(root);
k0_try('create.Description.set', @() setget(proj, 'Description', 'A K20 project'));
tree = snapshot('create.description', root, tree);
k0_try('create.Name.set', @() setget(proj, 'Name', 'k20proj'));
k0_try('create.Name.prjfile.old', @() exist(fullfile(root, 'blank_project.prj'), 'file'));
k0_try('create.Name.prjfile.new', @() exist(fullfile(root, 'k20proj.prj'), 'file'));
tree = snapshot('create.name', root, tree);
k0_try('create.RootFolder.set', @() setget(proj, 'RootFolder', src));

%% Files.
hello = fullfile(root, 'hello.m');
writeText(hello, sprintf('function y = hello(x)\n%%HELLO Adds one.\ny = x + helper(1);\nend\n'));
mkdir(fullfile(root, 'lib'));
helper = fullfile(root, 'lib', 'helper.m');
writeText(helper, sprintf('function z = helper(x)\nz = x * 2;\nend\n'));
mkdir(fullfile(root, 'data'));
vals = fullfile(root, 'data', 'vals.txt');
writeText(vals, sprintf('1 2 3\n'));
writeText(fullfile(root, 'data', 'more.txt'), sprintf('4\n'));
mkdir(fullfile(root, 'extra'));
start = fullfile(root, 'k20start.m');
writeText(start, sprintf('disp(''k20 startup ran'')\n'));
stop = fullfile(root, 'k20stop.m');
writeText(stop, sprintf('disp(''k20 shutdown ran'')\n'));
helptxt = fullfile(root, 'help.txt');
writeText(helptxt, sprintf('Help for k20\n'));
sigs = fullfile(root, 'sigs.json');
writeText(sigs, sprintf('{"_schemaVersion": "1.1.0", "hello": {"inputs": [{"name": "x", "type": "double"}], "outputs": [{"name": "y", "type": "double"}]}}\n'));
icon = fullfile(root, 'icon.png');
imwrite(uint8(zeros(16, 16, 3)), icon);
splash = fullfile(root, 'splash.png');
imwrite(uint8(255 * ones(20, 30, 3)), splash);
logo = fullfile(root, 'logo.png');
imwrite(uint8(128 * ones(10, 40, 3)), logo);
key = fullfile(root, 'key.snk');
writeText(key, 'not a key');

k0_try('files.addFile.hello', @() describeFile(addFile(proj, hello), root));
call('files.addFile.hello.disp', @() disp(addFile(proj, hello)));
k0_try('files.addFile.twice', @() describeFile(addFile(proj, hello), root));
k0_try('files.addFile.string', @() describeFile(addFile(proj, string(helper)), root));
k0_try('files.addFile.relative', @() describeFile(addFile(proj, fullfile('data', 'vals.txt')), root));
k0_try('files.addFile.outside', @() addFile(proj, fullfile(src, 'k20note.txt')));
k0_try('files.addFile.missing', @() addFile(proj, fullfile(root, 'nope.m')));
k0_try('files.addFile.definition', @() addFile(proj, fullfile(root, 'resources', 'project', 'Project.xml')));
k0_try('files.addFile.resources', @() addFile(proj, fullfile(root, 'resources')));
k0_try('files.addFile.root', @() addFile(proj, root));
k0_try('files.addFile.number', @() addFile(proj, 1));
k0_try('files.addFile.none', @() addFile(proj));
k0_try('files.addFile.folder', @() describeFile(addFile(proj, fullfile(root, 'lib')), root));
k0_try('files.addFile.extra', @() describeFile(addFile(proj, fullfile(root, 'extra')), root));
k0_try('files.addFolderIncludingChildFiles', @() describeFiles(addFolderIncludingChildFiles(proj, fullfile(root, 'data')), root));
k0_try('files.addFile.images', @() describeFiles(addFile(proj, {icon, splash, logo}), root));
k0_try('files.addFile.cell', @() describeFiles(addFile(proj, {helptxt, sigs, key, start, stop}), root));
k0_try('files.Files', @() relAll(proj.Files, root));
k0_try('files.Files.class', @() class(proj.Files));
k0_try('files.Files.size', @() size(proj.Files));
k0_try('files.hello.labels', @() labelsOf(findFiles(proj, hello, 'OutputFormat', 'ProjectFile')));
k0_try('files.vals.labels', @() labelsOf(findFiles(proj, vals, 'OutputFormat', 'ProjectFile')));
k0_try('files.findFiles', @() findFiles(proj));
k0_try('files.findFiles.class', @() class(findFiles(proj)));
k0_try('files.findFiles.design', @() findFiles(proj, 'Label', 'Design'));
k0_try('files.findFiles.category', @() findFiles(proj, 'Category', 'Classification'));
k0_try('files.findFiles.nolabel', @() findFiles(proj, 'Label', 'Nope'));
k0_try('files.findFiles.projectfile', @() describeFiles(findFiles(proj, 'OutputFormat', 'ProjectFile'), root));
k0_try('files.findFiles.one', @() findFiles(proj, hello));
k0_try('files.findFiles.notin', @() findFiles(proj, fullfile(src, 'k20note.txt')));
k0_try('files.isFileInProject.yes', @() matlab.project.isFileInProject(hello));
k0_try('files.isFileInProject.no', @() matlab.project.isFileInProject(fullfile(root, 'extra', 'nope.m')));
k0_try('files.isFileInProject.outside', @() matlab.project.isFileInProject(fullfile(src, 'k20note.txt')));
k0_try('files.isFileInProject.two', @() matlab.project.isFileInProject({hello, fullfile(src, 'k20note.txt')}));
k0_try('files.isUnderProjectRoot.yes', @() matlab.project.isUnderProjectRoot(fullfile(root, 'extra', 'nope.m')));
k0_try('files.listRequiredFiles.hello', @() listRequiredFiles(proj, hello));
k0_try('files.listRequiredFiles.helper', @() listRequiredFiles(proj, helper));
k0_try('files.listRequiredFiles.notin', @() listRequiredFiles(proj, fullfile(src, 'plain', 'p.m')));
k0_try('files.listImpactedFiles.helper', @() listImpactedFiles(proj, helper));
k0_try('files.listModifiedFiles', @() listModifiedFiles(proj));
k0_try('files.Dependencies.class', @() class(proj.Dependencies));
k0_try('files.Dependencies.nodes', @() proj.Dependencies.Nodes.Name');
call('files.updateDependencies', @() updateDependencies(proj));
k0_try('files.Dependencies.edges', @() size(proj.Dependencies.Edges, 1));
call('files.removeFile.more', @() removeFile(proj, fullfile(root, 'data', 'more.txt')));
call('files.removeFile.notin', @() removeFile(proj, fullfile(root, 'extra', 'nope.m')));
call('files.removeFile.outside', @() removeFile(proj, fullfile(src, 'k20note.txt')));
call('files.removeFile.root', @() removeFile(proj, root));
k0_try('files.Files.after', @() relAll(proj.Files, root));
tree = snapshot('files', root, tree);
ids(root);

%% The project path.
lib = fullfile(root, 'lib');
k0_try('path.addPath.lib', @() describePath(addPath(proj, lib), root));
call('path.addPath.lib.disp', @() disp(addPath(proj, lib)));
k0_try('path.addPath.twice', @() describePath(addPath(proj, lib), root));
k0_try('path.addPath.notin', @() addPath(proj, fullfile(root, 'nope')));
k0_try('path.addPath.file', @() addPath(proj, hello));
k0_try('path.addPath.outside', @() addPath(proj, src));
k0_try('path.addPath.root', @() describePath(addPath(proj, root), root));
k0_try('path.addPath.relative', @() describePath(addPath(proj, 'data'), root));
k0_try('path.ProjectPath', @() arrayfun(@(p) rel(p.File, root), proj.ProjectPath, 'UniformOutput', false));
k0_try('path.ProjectPath.class', @() class(proj.ProjectPath));
k0_try('path.onpath.lib', @() any(strcmp(strsplit(path, pathsep), lib)));
k0_try('path.onpath.root', @() any(strcmp(strsplit(path, pathsep), root)));
call('path.removePath.data', @() removePath(proj, fullfile(root, 'data')));
call('path.removePath.root', @() removePath(proj, root));
call('path.removePath.notthere', @() removePath(proj, fullfile(root, 'extra')));
k0_try('path.ProjectPath.after', @() arrayfun(@(p) rel(p.File, root), proj.ProjectPath, 'UniformOutput', false));
tree = snapshot('path', root, tree);

%% Startup and shutdown files, shortcuts.
k0_try('entry.addStartupFile', @() addStartupFile(proj, start));
k0_try('entry.addStartupFile.twice', @() addStartupFile(proj, start));
k0_try('entry.addStartupFile.notin', @() addStartupFile(proj, fullfile(root, 'extra', 'nope.m')));
k0_try('entry.addStartupFile.folder', @() addStartupFile(proj, lib));
k0_try('entry.addStartupFile.outside', @() addStartupFile(proj, fullfile(src, 'plain', 'p.m')));
k0_try('entry.StartupFiles', @() rel(proj.StartupFiles, root));
k0_try('entry.addShutdownFile', @() addShutdownFile(proj, stop));
k0_try('entry.ShutdownFiles', @() rel(proj.ShutdownFiles, root));
k0_try('entry.addShortcut', @() describeShortcut(addShortcut(proj, hello), root));
call('entry.addShortcut.disp', @() disp(addShortcut(proj, hello)));
k0_try('entry.addShortcut.folder', @() addShortcut(proj, lib));
k0_try('entry.addShortcut.notin', @() addShortcut(proj, fullfile(root, 'extra', 'nope.m')));
k0_try('entry.addShortcut.startup', @() describeShortcut(addShortcut(proj, start), root));
k0_try('entry.Shortcuts', @() arrayfun(@(s) describeShortcut(s, root), proj.Shortcuts, 'UniformOutput', false));
k0_try('entry.StartupFiles.after', @() rel(proj.StartupFiles, root));
tree = snapshot('entry', root, tree);
call('entry.removeShortcut.start', @() removeShortcut(proj, start));
call('entry.removeShortcut.notthere', @() removeShortcut(proj, helper));
call('entry.removeStartupFile.notthere', @() removeStartupFile(proj, helper));
k0_try('entry.Shortcuts.after', @() arrayfun(@(s) describeShortcut(s, root), proj.Shortcuts, 'UniformOutput', false));
k0_try('entry.listStartupIssues', @() listStartupIssues(proj));
tree = snapshot('entry.remove', root, tree);

%% Labels and categories.
k0_try('labels.Categories', @() {proj.Categories.Name});
call('labels.category.disp', @() disp(proj.Categories(1)));
k0_try('labels.findCategory', @() findCategory(proj, 'Classification').Name);
k0_try('labels.findCategory.missing', @() findCategory(proj, 'Nope'));
k0_try('labels.createCategory', @() describeCategory(createCategory(proj, 'Owners', 'char')));
k0_try('labels.createCategory.exists', @() createCategory(proj, 'Owners'));
k0_try('labels.createCategory.classification', @() createCategory(proj, 'Classification'));
k0_try('labels.createCategory.badtype', @() createCategory(proj, 'Weights', 'bogus'));
k0_try('labels.createCategory.single', @() describeCategory(createCategory(proj, 'Review', 'logical', 'single-valued')));
k0_try('labels.createLabel', @() describeDefinition(createLabel(findCategory(proj, 'Owners'), 'Jacob')));
k0_try('labels.createLabel.exists', @() createLabel(findCategory(proj, 'Owners'), 'Jacob'));
k0_try('labels.createLabel.design', @() createLabel(findCategory(proj, 'Classification'), 'Design'));
k0_try('labels.createLabel.new.classification', @() describeDefinition(createLabel(findCategory(proj, 'Classification'), 'Requirement')));
k0_try('labels.findLabel', @() describeDefinition(findLabel(findCategory(proj, 'Owners'), 'Jacob')));
k0_try('labels.findLabel.missing', @() findLabel(findCategory(proj, 'Owners'), 'Nope'));
k0_try('labels.addLabel.data', @() describeLabel(addLabel(proj, hello, 'Owners', 'Jacob', 'me'), root));
k0_try('labels.addLabel.nodata', @() describeLabel(addLabel(proj, helper, 'Owners', 'Jacob'), root));
k0_try('labels.addLabel.badcat', @() addLabel(proj, hello, 'Nope', 'Jacob'));
k0_try('labels.addLabel.newlabel', @() describeLabel(addLabel(proj, vals, 'Owners', 'Someone'), root));
k0_try('labels.addLabel.definition', @() describeLabel(addLabel(proj, vals, matlab.project.LabelDefinition('Classification', 'Requirement')), root));
k0_try('labels.addLabel.baddata', @() addLabel(proj, hello, 'Review', 'Done', 'yes'));
k0_try('labels.addLabel.logical', @() describeLabel(addLabel(proj, hello, 'Review', 'Done', true), root));
k0_try('labels.addLabel.single.second', @() describeLabel(addLabel(proj, hello, 'Review', 'Again', false), root));
k0_try('labels.addLabel.notin', @() addLabel(proj, fullfile(src, 'k20note.txt'), 'Owners', 'Jacob'));
k0_try('labels.file.addLabel', @() describeLabel(addLabel(findFiles(proj, helper, 'OutputFormat', 'ProjectFile'), 'Classification', 'Test'), root));
k0_try('labels.hello.labels', @() labelsOf(findFiles(proj, hello, 'OutputFormat', 'ProjectFile')));
k0_try('labels.helper.labels', @() labelsOf(findFiles(proj, helper, 'OutputFormat', 'ProjectFile')));
k0_try('labels.vals.labels', @() labelsOf(findFiles(proj, vals, 'OutputFormat', 'ProjectFile')));
k0_try('labels.findFiles.jacob', @() rel(findFiles(proj, 'Label', 'Jacob'), root));
k0_try('labels.findFiles.owners', @() rel(findFiles(proj, 'Category', 'Owners'), root));
k0_try('labels.findFiles.both', @() rel(findFiles(proj, 'Category', 'Owners', 'Label', 'Someone'), root));
k0_try('labels.Categories.after', @() {proj.Categories.Name});
k0_try('labels.Owners.definitions', @() {findCategory(proj, 'Owners').LabelDefinitions.Name});
k0_try('labels.Classification.definitions', @() {findCategory(proj, 'Classification').LabelDefinitions.Name});
tree = snapshot('labels', root, tree);
ids(root);
call('labels.removeLabel.hello', @() removeLabel(proj, hello, 'Owners', 'Jacob'));
call('labels.removeLabel.notthere', @() removeLabel(proj, hello, 'Owners', 'Jacob'));
call('labels.removeLabel.byname', @() removeLabel(proj, helper, 'Jacob'));
call('labels.category.removeLabel', @() removeLabel(findCategory(proj, 'Owners'), 'Someone'));
call('labels.category.removeLabel.design', @() removeLabel(findCategory(proj, 'Classification'), 'Design'));
call('labels.removeCategory.review', @() removeCategory(proj, 'Review'));
call('labels.removeCategory.classification', @() removeCategory(proj, 'Classification'));
call('labels.removeCategory.missing', @() removeCategory(proj, 'Nope'));
k0_try('labels.vals.labels.after', @() labelsOf(findFiles(proj, vals, 'OutputFormat', 'ProjectFile')));
k0_try('labels.Categories.final', @() {proj.Categories.Name});
tree = snapshot('labels.remove', root, tree);

%% Compiler tasks: one per target.
getters = { ...
    'Standalone', @compiler.project.getStandaloneDesktopAppConfiguration; ...
    'PSA', @compiler.project.getProductionServerArchiveConfiguration; ...
    'Python', @compiler.project.getPythonLibraryConfiguration; ...
    'DotNET', @compiler.project.getDotNETLibraryConfiguration; ...
    'Cpp', @compiler.project.getCppSharedLibraryConfiguration; ...
    'C', @compiler.project.getCSharedLibraryConfiguration; ...
    'Excel', @compiler.project.getExcelAddInConfiguration; ...
    'Java', @compiler.project.getJavaLibraryConfiguration; ...
    'COM', @compiler.project.getCOMLibraryConfiguration; ...
    'WebApp', @compiler.project.getWebAppConfiguration; ...
    'Toolbox', @compiler.project.getToolboxConfiguration};
cfgs = struct();
for k = 1:size(getters, 1)
    t = getters{k, 1};
    g = getters{k, 2};
    name = ['my' t];
    try
        cfg = g(proj, name);
        cfgs.(t) = cfg;
    catch ME
        fprintf('task.%s.get\tERR %s | %s\n', t, ME.identifier, k0_flat(ME.message));
        continue
    end
    fprintf('task.%s.get\tok\n', t);
    k0_try(['task.' t '.class'], @() class(cfg));
    call(['task.' t '.disp'], @() disp(cfg));
    mc = metaclass(cfg);
    k0_try(['task.' t '.superclasses'], @() cellfun(@(c) c.Name, num2cell(mc.SuperclassList), 'UniformOutput', false)');
    for j = 1:numel(mc.PropertyList)
        pr = mc.PropertyList(j);
        k0_try(['task.' t '.prop.' pr.Name], @() cfg.(pr.Name));
        fprintf('task.%s.access.%s\tget=%s set=%s hidden=%d\n', t, pr.Name, char(string(pr.GetAccess)), char(string(pr.SetAccess)), pr.Hidden);
        if isstruct(cfg.(pr.Name)) && isscalar(cfg.(pr.Name))
            k0_try(['task.' t '.label.' pr.Name], @() describeLabelStruct(cfg.(pr.Name)));
        end
    end
    k0_try(['task.' t '.methods'], @() methods(cfg)');
    k0_try(['task.' t '.again.same'], @() g(proj, name) == cfg);
    k0_try(['task.' t '.again.ProfileDisplayName'], @() g(proj, name).ProfileDisplayName);
    tree = snapshot(['task.' t], root, tree);
end
k0_try('task.Categories', @() {proj.Categories.Name});
k0_try('task.get.noname', @() compiler.project.getStandaloneDesktopAppConfiguration(proj));
k0_try('task.get.none', @() compiler.project.getStandaloneDesktopAppConfiguration());
k0_try('task.get.number', @() compiler.project.getStandaloneDesktopAppConfiguration(proj, 1));
k0_try('task.get.empty', @() compiler.project.getStandaloneDesktopAppConfiguration(proj, ''));
k0_try('task.get.string', @() compiler.project.getStandaloneDesktopAppConfiguration(proj, "myString").ProfileDisplayName);
k0_try('task.get.cell', @() compiler.project.getStandaloneDesktopAppConfiguration(proj, {'a'}));
k0_try('task.get.notproject', @() compiler.project.getStandaloneDesktopAppConfiguration(root, 'x'));
k0_try('task.get.three', @() compiler.project.getStandaloneDesktopAppConfiguration(proj, 'x', 'y'));
k0_try('task.get.othertype', @() class(compiler.project.getProductionServerArchiveConfiguration(proj, 'myStandalone')));
k0_try('task.get.spaces', @() compiler.project.getStandaloneDesktopAppConfiguration(proj, 'my Standalone 2!').ProfileDisplayName);
k0_try('task.get.spaces.folder', @() compiler.project.getStandaloneDesktopAppConfiguration(proj, 'my Standalone 2!').BuildOutputFolder);
k0_try('task.get.slash', @() compiler.project.getStandaloneDesktopAppConfiguration(proj, 'a/b').ProfileDisplayName);
k0_try('task.get.long', @() compiler.project.getStandaloneDesktopAppConfiguration(proj, repmat('x', 1, 80)).ProfileDisplayName);
k0_try('task.get.case', @() compiler.project.getStandaloneDesktopAppConfiguration(proj, 'MYSTANDALONE') == cfgs.Standalone);
tree = snapshot('task.get.more', root, tree);

%% Setting a task's properties, what it stores, what it refuses.
s = cfgs.Standalone;
values = { ...
    'ApplicationName', 'K20 App'; 'ApplicationVersion', '2.3.4'; 'AuthorName', 'Jacob'; 'AuthorEmail', 'j@example.com'; ...
    'AuthorOrganization', 'JGraph'; 'Summary', 'A summary'; 'Description', sprintf('Line one\nLine two'); ...
    'AdditionalFileList', {vals}; 'BuildOutputFolder', fullfile('out', 'b'); 'AutoDetectDataFiles', false; ...
    'ObfuscateArchive', true; 'Verbose', false; 'ExecutableName', 'k20exe'; 'SuppressWindowsCmdPrompt', true; ...
    'TreatInputsAsNumeric', true; 'EmbedArchive', false; 'ExampleText', 'k20exe 1 2'; 'HelpText', 'Help me'; ...
    'InstallNotesText', 'Notes here'; 'ExecutableIconFile', icon; 'ExecutableSplashFile', splash; 'MainFile', hello; ...
    'CustomHelpTextFile', helptxt; 'PackageOutputFolder', fullfile('out', 'p'); 'InstallationSubFolder', 'K20Sub'; ...
    'RuntimeInstallType', 'installer'; 'InstallerName', 'K20Installer'; 'InstallerVersion', '2.3.4'; ...
    'InstallerAdditionalFileList', {helptxt, vals}; 'InstallerAddRemoveProgramsIconFile', icon; 'InstallerIconFile', icon; ...
    'InstallerSplashFile', splash; 'InstallerLogoFile', logo; 'InstallerNotes', 'Install notes'};
for k = 1:size(values, 1)
    k0_try(['set.Standalone.' values{k, 1}], @() setget(s, values{k, 1}, values{k, 2}));
end
k0_try('set.Standalone.RuntimeInstallType.string', @() string(s.RuntimeInstallType));
k0_try('set.Standalone.RuntimeInstallType.enum', @() setget(s, 'RuntimeInstallType', deployment.compiler.model.MCRInstallType.NONE));
k0_try('set.Standalone.RuntimeInstallType.enum.string', @() string(s.RuntimeInstallType));
k0_try('set.Standalone.RuntimeInstallType.web', @() string(setget(s, 'RuntimeInstallType', 'web')));
k0_try('set.Standalone.RuntimeInstallType.upper', @() string(setget(s, 'RuntimeInstallType', 'Installer')));
k0_try('set.Standalone.RuntimeInstallType.bogus', @() setget(s, 'RuntimeInstallType', 'bogus'));
k0_try('set.Standalone.ApplicationName.number', @() setget(s, 'ApplicationName', 123));
k0_try('set.Standalone.ApplicationName.string', @() setget(s, 'ApplicationName', "K20 String"));
k0_try('set.Standalone.ApplicationName.empty', @() setget(s, 'ApplicationName', ''));
k0_try('set.Standalone.ApplicationVersion.bad', @() setget(s, 'ApplicationVersion', 'abc'));
k0_try('set.Standalone.ApplicationVersion.number', @() setget(s, 'ApplicationVersion', 2));
k0_try('set.Standalone.MainFile.missing', @() setget(s, 'MainFile', fullfile(root, 'nope.m')));
k0_try('set.Standalone.MainFile.outside', @() setget(s, 'MainFile', fullfile(src, 'plain', 'p.m')));
k0_try('set.Standalone.MainFile.txt', @() setget(s, 'MainFile', helptxt));
k0_try('set.Standalone.MainFile.notin', @() setget(s, 'MainFile', fullfile(root, 'extra', 'nope.m')));
k0_try('set.Standalone.MainFile.relative', @() setget(s, 'MainFile', 'hello.m'));
k0_try('set.Standalone.MainFile.cell', @() setget(s, 'MainFile', {hello}));
k0_try('set.Standalone.MainFile.empty', @() setget(s, 'MainFile', ''));
k0_try('set.Standalone.MainFile.back', @() setget(s, 'MainFile', hello));
k0_try('set.Standalone.MainFile.helper', @() setget(s, 'MainFile', helper));
k0_try('set.Standalone.MainFile.hello', @() setget(s, 'MainFile', hello));
k0_try('set.Standalone.AdditionalFileList.char', @() setget(s, 'AdditionalFileList', vals));
k0_try('set.Standalone.AdditionalFileList.string', @() setget(s, 'AdditionalFileList', string(vals)));
k0_try('set.Standalone.AdditionalFileList.missing', @() setget(s, 'AdditionalFileList', {fullfile(root, 'nope.txt')}));
k0_try('set.Standalone.AdditionalFileList.outside', @() setget(s, 'AdditionalFileList', {fullfile(src, 'k20note.txt')}));
k0_try('set.Standalone.AdditionalFileList.folder', @() setget(s, 'AdditionalFileList', {lib}));
k0_try('set.Standalone.AdditionalFileList.main', @() setget(s, 'AdditionalFileList', {hello}));
k0_try('set.Standalone.AdditionalFileList.two', @() setget(s, 'AdditionalFileList', {vals, helper}));
k0_try('set.Standalone.AdditionalFileList.empty', @() setget(s, 'AdditionalFileList', {}));
k0_try('set.Standalone.AdditionalFileList.back', @() setget(s, 'AdditionalFileList', {vals}));
k0_try('set.Standalone.AutoDetectDataFiles.char', @() setget(s, 'AutoDetectDataFiles', 'yes'));
k0_try('set.Standalone.AutoDetectDataFiles.number', @() setget(s, 'AutoDetectDataFiles', 1));
k0_try('set.Standalone.BuildOutputFolder.absolute', @() setget(s, 'BuildOutputFolder', fullfile(root, 'absout')));
k0_try('set.Standalone.BuildOutputFolder.outside', @() setget(s, 'BuildOutputFolder', fullfile(src, 'out')));
k0_try('set.Standalone.BuildOutputFolder.empty', @() setget(s, 'BuildOutputFolder', ''));
k0_try('set.Standalone.BuildOutputFolder.back', @() setget(s, 'BuildOutputFolder', fullfile('out', 'b')));
k0_try('set.Standalone.ExecutableIconFile.missing', @() setget(s, 'ExecutableIconFile', fullfile(root, 'nope.png')));
k0_try('set.Standalone.ExecutableIconFile.txt', @() setget(s, 'ExecutableIconFile', helptxt));
k0_try('set.Standalone.ExecutableIconFile.back', @() setget(s, 'ExecutableIconFile', icon));
k0_try('set.Standalone.ExecutableName.spaces', @() setget(s, 'ExecutableName', 'my exe'));
k0_try('set.Standalone.ExecutableName.back', @() setget(s, 'ExecutableName', 'k20exe'));
k0_try('set.Standalone.ProfileDisplayName', @() setget(s, 'ProfileDisplayName', 'x'));
k0_try('set.Standalone.MainFileLabel', @() setget(s, 'MainFileLabel', struct()));
k0_try('set.Standalone.SupportPackages', @() setget(s, 'SupportPackages', struct('Name', 'x')));
k0_try('set.Standalone.Bogus', @() setget(s, 'Bogus', 1));
k0_try('set.Standalone.hello.labels', @() labelsOf(findFiles(proj, hello, 'OutputFormat', 'ProjectFile')));
k0_try('set.Standalone.vals.labels', @() labelsOf(findFiles(proj, vals, 'OutputFormat', 'ProjectFile')));
k0_try('set.Standalone.helper.labels', @() labelsOf(findFiles(proj, helper, 'OutputFormat', 'ProjectFile')));
k0_try('set.Standalone.icon.labels', @() labelsOf(findFiles(proj, icon, 'OutputFormat', 'ProjectFile')));
k0_try('set.Standalone.findFiles.mainfunction', @() rel(findFiles(proj, 'Label', 'Main Function'), root));
k0_try('set.Standalone.findFiles.category', @() rel(findFiles(proj, 'Category', 'Standalone Application - myStandalone'), root));
call('set.Standalone.disp', @() disp(s));
tree = snapshot('set.Standalone', root, tree);

p = cfgs.PSA;
values = {'ArchiveName', 'k20archive'; 'FunctionFileList', {hello, helper}; 'FunctionSignatureFile', sigs; ...
    'AdditionalFileList', {vals}; 'BuildOutputFolder', fullfile('psa', 'b'); 'AutoDetectDataFiles', false; ...
    'ObfuscateArchive', true; 'Verbose', false};
for k = 1:size(values, 1)
    k0_try(['set.PSA.' values{k, 1}], @() setget(p, values{k, 1}, values{k, 2}));
end
k0_try('set.PSA.FunctionFileList.txt', @() setget(p, 'FunctionFileList', {helptxt}));
k0_try('set.PSA.FunctionFileList.missing', @() setget(p, 'FunctionFileList', {fullfile(root, 'nope.m')}));
k0_try('set.PSA.FunctionFileList.char', @() setget(p, 'FunctionFileList', hello));
k0_try('set.PSA.FunctionFileList.back', @() setget(p, 'FunctionFileList', {hello, helper}));
k0_try('set.PSA.FunctionSignatureFile.txt', @() setget(p, 'FunctionSignatureFile', helptxt));
k0_try('set.PSA.FunctionSignatureFile.back', @() setget(p, 'FunctionSignatureFile', sigs));
k0_try('set.PSA.hello.labels', @() labelsOf(findFiles(proj, hello, 'OutputFormat', 'ProjectFile')));
call('set.PSA.disp', @() disp(p));
tree = snapshot('set.PSA', root, tree);

others = {'Python', {'PackageName', 'k20py'; 'FunctionFileList', {hello}; 'AdditionalFileList', {vals}; 'ObfuscateArchive', true; 'Verbose', false; 'SampleGenerationFileList', {}}; ...
    'DotNET', {'AssemblyName', 'K20Net'; 'AssemblyVersion', '2.0.0.0'; 'ClassName', 'K20Class'; 'FunctionFileList', {hello}; 'AdditionalFileList', {vals}; 'Interface', 'mwarray'; 'StrongNameKeyFile', key; 'Verbose', false}; ...
    'Cpp', {'LibraryName', 'k20cpp'; 'FunctionFileList', {hello}; 'AdditionalFileList', {vals}; 'Interface', 'mwarray'; 'Verbose', false}; ...
    'C', {'LibraryName', 'k20c'; 'FunctionFileList', {hello}; 'AdditionalFileList', {vals}; 'Verbose', false}; ...
    'Excel', {'AddInName', 'k20xl'; 'ClassName', 'K20XlClass'; 'FunctionFileList', {hello}; 'AdditionalFileList', {vals}; 'Verbose', false}; ...
    'WebApp', {'ArchiveName', 'k20web'; 'MainFile', hello; 'AdditionalFileList', {vals}; 'Verbose', false}};
for k = 1:size(others, 1)
    t = others{k, 1};
    if ~isfield(cfgs, t), continue; end
    c = cfgs.(t);
    vs = others{k, 2};
    for j = 1:size(vs, 1)
        k0_try(['set.' t '.' vs{j, 1}], @() setget(c, vs{j, 1}, vs{j, 2}));
    end
    call(['set.' t '.disp'], @() disp(c));
    tree = snapshot(['set.' t], root, tree);
end

%% The deployment script R2025b writes for a task.
targets = {'Standalone', 'StandaloneApplication'; 'PSA', 'ProductionServerArchive'; 'Python', 'PythonPackage'; ...
    'DotNET', 'DotNETAssembly'; 'Cpp', 'CppSharedLibrary'; 'C', 'CSharedLibrary'; 'Excel', 'ExcelAddin'; ...
    'Java', 'JavaPackage'; 'COM', 'COMComponent'; 'WebApp', 'WebAppArchive'; 'Toolbox', 'Toolbox'};
packages = {'NoInstaller', 'MCRInstaller'};
for k = 1:size(targets, 1)
    t = targets{k, 1};
    if ~isfield(cfgs, t), continue; end
    c = cfgs.(t);
    for j = 1:numel(packages)
        call(['script.' t '.' packages{j}], @() printScript(c, root, targets{k, 2}, packages{j}));
    end
end
call('script.Standalone.nopackage', @() printScript(cfgs.Standalone, root, 'StandaloneApplication', ''));
call('script.Standalone.ExcelClientForProductionServer', @() printScript(cfgs.Standalone, root, 'StandaloneApplication', 'ExcelClientForProductionServer'));
call('script.Standalone.wrongtarget', @() printScript(cfgs.Standalone, root, 'PythonPackage', 'NoInstaller'));
call('script.Standalone.otherroot', @() printScript(cfgs.Standalone, src, 'StandaloneApplication', 'MCRInstaller'));
k0_try('script.builder.noargs', @() compiler.internal.deployScriptGenerator.DeployScriptBuilder());
k0_try('script.builder.one', @() compiler.internal.deployScriptGenerator.DeployScriptBuilder(cfgs.Standalone));
k0_try('script.builder.notcfg', @() compiler.internal.deployScriptGenerator.DeployScriptBuilder(1, root));
k0_try('script.builder.props', @() properties(compiler.internal.deployScriptGenerator.DeployScriptBuilder(cfgs.Standalone, root))');
k0_try('script.builder.buildTarget.default', @() string(compiler.internal.deployScriptGenerator.DeployScriptBuilder(cfgs.Standalone, root).buildTarget));
k0_try('script.builder.packageTarget.default', @() string(compiler.internal.deployScriptGenerator.DeployScriptBuilder(cfgs.Standalone, root).packageTarget));
call('script.builder.unset', @() disp(compiler.internal.deployScriptGenerator.DeployScriptBuilder(cfgs.Standalone, root).generateDeploymentScript()));
cfgs.Standalone.SuppressWindowsCmdPrompt = false;
cfgs.Standalone.RuntimeInstallType = 'web';
call('script.Standalone.console.web', @() printScript(cfgs.Standalone, root, 'StandaloneApplication', 'MCRInstaller'));
try, cfgs.Standalone.RuntimeInstallType = 'NONE'; catch ME, fprintf('script.Standalone.console.none.set\tERR %s | %s\n', ME.identifier, k0_flat(ME.message)); end
call('script.Standalone.console.none', @() printScript(cfgs.Standalone, root, 'StandaloneApplication', 'MCRInstaller'));
cfgs.Standalone.RuntimeInstallType = 'installer';
cfgs.Standalone.SuppressWindowsCmdPrompt = true;
fresh = compiler.project.getStandaloneDesktopAppConfiguration(proj, 'myFresh');
call('script.fresh.MCRInstaller', @() printScript(fresh, root, 'StandaloneApplication', 'MCRInstaller'));
fresh.MainFile = hello;
call('script.fresh.main.MCRInstaller', @() printScript(fresh, root, 'StandaloneApplication', 'MCRInstaller'));
call('script.fresh.main.NoInstaller', @() printScript(fresh, root, 'StandaloneApplication', 'NoInstaller'));
k0_try('script.getStandaloneDeploymentScript', @() firstLine(compiler.internal.ui.getStandaloneDeploymentScript(root, 'myFresh', true)));
call('script.getStandaloneDeploymentScript.printed', @() disp(compiler.internal.ui.getStandaloneDeploymentScript(root, 'myFresh', true)));
k0_try('script.getStandaloneDeploymentScript.missing', @() compiler.internal.ui.getStandaloneDeploymentScript(root, 'nope', true));
k0_try('script.getStandaloneDeploymentScript.notproject', @() compiler.internal.ui.getStandaloneDeploymentScript(src, 'myFresh', true));
% That call closed the project on its way to failing (it opens what it is given); back to it.
k0_try('script.notproject.closed', @() isLoaded(proj));
call('script.notproject.reopen', @() openProject(root));
proj = currentProject;
tree = snapshot('script', root, tree);

%% Renaming and removing a task.
call('manage.rename.guess.a', @() compiler.internal.project.rename(proj, 'StandaloneDesktopApp', 'myFresh', 'myRenamed'));
call('manage.rename.guess.b', @() compiler.internal.project.rename(proj, 'myFresh', 'myRenamed', 'StandaloneDesktopApp'));
call('manage.rename.guess.c', @() compiler.internal.project.rename(root, 'StandaloneDesktopApp', 'myFresh', 'myRenamed'));
call('manage.rename.guess.d', @() compiler.internal.project.rename(proj, 'myFresh', 'myRenamed', 1));
call('manage.rename.again', @() compiler.internal.project.rename(proj, 'StandaloneDesktopApp', 'myFresh', 'myRenamed'));
call('manage.rename.taken', @() compiler.internal.project.rename(proj, 'StandaloneDesktopApp', 'myRenamed', 'myStandalone'));
k0_try('manage.renamed.get', @() compiler.project.getStandaloneDesktopAppConfiguration(proj, 'myRenamed').MainFile);
k0_try('manage.renamed.folder', @() compiler.project.getStandaloneDesktopAppConfiguration(proj, 'myRenamed').BuildOutputFolder);
k0_try('manage.old.get', @() compiler.project.getStandaloneDesktopAppConfiguration(proj, 'myFresh').MainFile);
k0_try('manage.Categories', @() {proj.Categories.Name});
tree = snapshot('manage.rename', root, tree);
call('manage.remove.guess.a', @() compiler.internal.project.remove(proj, 'StandaloneDesktopApp', 'myFresh'));
call('manage.remove.guess.b', @() compiler.internal.project.remove(proj, 'myFresh', 'StandaloneDesktopApp'));
call('manage.remove.guess.c', @() compiler.internal.project.remove(root, 'StandaloneDesktopApp', 'myFresh'));
call('manage.remove.missing', @() compiler.internal.project.remove(proj, 'StandaloneDesktopApp', 'nope'));
call('manage.remove.java', @() compiler.internal.project.remove(proj, 'JavaLibrary', 'myJava'));
call('manage.remove.java.b', @() compiler.internal.project.remove(proj, 'JavaPackage', 'myJava'));
call('manage.remove.com', @() compiler.internal.project.remove(proj, 'COMLibrary', 'myCOM'));
call('manage.remove.com.b', @() compiler.internal.project.remove(proj, 'COMComponent', 'myCOM'));
call('manage.remove.toolbox', @() compiler.internal.project.remove(proj, 'Toolbox', 'myToolbox'));
k0_try('manage.Categories.after', @() {proj.Categories.Name});
k0_try('manage.hello.labels', @() labelsOf(findFiles(proj, hello, 'OutputFormat', 'ProjectFile')));
tree = snapshot('manage.remove', root, tree);
call('manage.openEditor.noargs', @() compiler.internal.project.openEditor());
call('manage.openEditor.guess', @() compiler.internal.project.openEditor(proj, 'StandaloneDesktopApp', 'myStandalone'));

%% Close, reopen, what persisted.
call('close', @() close(proj));
k0_try('close.isLoaded', @() isLoaded(proj));
k0_try('close.current', @() currentProject());
k0_try('close.Name', @() proj.Name);
k0_try('close.Files', @() proj.Files);
k0_try('close.pwd.root', @() strcmp(pwd, root));
k0_try('close.onpath.lib', @() any(strcmp(strsplit(path, pathsep), lib)));
call('close.again', @() close(proj));
k0_try('close.task.get', @() compiler.project.getStandaloneDesktopAppConfiguration(proj, 'myStandalone'));
k0_try('close.task.old.ApplicationName', @() s.ApplicationName);
k0_try('close.task.old.set', @() setget(s, 'AuthorName', 'Closed'));
tree = snapshot('close', root, tree);
call('reopen', @() openProject(root));
proj = currentProject;
k0_try('reopen.Name', @() proj.Name);
k0_try('reopen.Description', @() proj.Description);
k0_try('reopen.Files', @() relAll(proj.Files, root));
k0_try('reopen.StartupFiles', @() rel(proj.StartupFiles, root));
k0_try('reopen.ShutdownFiles', @() rel(proj.ShutdownFiles, root));
k0_try('reopen.ProjectPath', @() arrayfun(@(p) rel(p.File, root), proj.ProjectPath, 'UniformOutput', false));
k0_try('reopen.Shortcuts', @() arrayfun(@(sh) describeShortcut(sh, root), proj.Shortcuts, 'UniformOutput', false));
k0_try('reopen.Categories', @() {proj.Categories.Name});
k0_try('reopen.hello.labels', @() labelsOf(findFiles(proj, hello, 'OutputFormat', 'ProjectFile')));
k0_try('reopen.onpath.lib', @() any(strcmp(strsplit(path, pathsep), lib)));
k0_try('reopen.pwd.root', @() strcmp(pwd, root));
k0_try('reopen.old.eq', @() proj == s);
s2 = compiler.project.getStandaloneDesktopAppConfiguration(proj, 'myStandalone');
mc = metaclass(s2);
for j = 1:numel(mc.PropertyList)
    pr = mc.PropertyList(j);
    if isstruct(s2.(pr.Name)) || pr.Hidden, continue; end
    k0_try(['reopen.task.Standalone.' pr.Name], @() s2.(pr.Name));
end
p2 = compiler.project.getProductionServerArchiveConfiguration(proj, 'myPSA');
k0_try('reopen.task.PSA.FunctionFileList', @() p2.FunctionFileList);
k0_try('reopen.task.PSA.FunctionSignatureFile', @() p2.FunctionSignatureFile);
k0_try('reopen.task.PSA.ArchiveName', @() p2.ArchiveName);
k0_try('reopen.task.renamed', @() compiler.project.getStandaloneDesktopAppConfiguration(proj, 'myRenamed').MainFile);
k0_try('reopen.openProject.prjfile', @() openProject(fullfile(root, 'k20proj.prj')).Name);
k0_try('reopen.openProject.root.again', @() openProject(root).Name);
k0_try('reopen.loadProject', @() matlab.project.loadProject(root).Name);
k0_try('reopen.rootProject', @() matlab.project.rootProject().Name);
call('reopen.reload', @() reload(proj));
k0_try('reopen.isLoaded', @() isLoaded(proj));
k0_try('reopen.runChecks.class', @() class(runChecks(proj)));
k0_try('reopen.listShutdownIssues', @() listShutdownIssues(proj));
tree = snapshot('reopen', root, tree);

%% A second project, an existing one, a named one, a nested one.
call('second.create', @() matlab.project.createProject(fullfile(work, 'proj2')));
k0_try('second.current', @() currentProject().Name);
k0_try('second.first.isLoaded', @() isLoaded(proj));
k0_try('second.first.Name', @() proj.Name);
k0_try('second.first.onpath', @() any(strcmp(strsplit(path, pathsep), lib)));
k0_try('second.create.existing', @() matlab.project.createProject(root));
k0_try('second.current.after.existing', @() currentProject().Name);
k0_try('second.create.nested', @() matlab.project.createProject(fullfile(root, 'inner')));
k0_try('second.current.after.nested', @() currentProject().Name);
named = fullfile(work, 'named');
k0_try('second.create.nv', @() matlab.project.createProject('Name', 'k20named', 'Folder', named).Name);
k0_try('second.create.nv.prj', @() exist(fullfile(named, 'k20named.prj'), 'file'));
k0_try('second.create.nv.RootFolder', @() strcmp(currentProject().RootFolder, named));
k0_try('second.create.folderonly', @() matlab.project.createProject('Folder', fullfile(work, 'folderonly')).Name);
k0_try('second.create.existingfolder', @() matlab.project.createProject(fullfile(work, 'src')).Name);
k0_try('second.create.existingfolder.files', @() numel(currentProject().Files));
call('second.close', @() close(currentProject()));
tree2 = snapshot('second.src', fullfile(work, 'src'), containers.Map('KeyType', 'char', 'ValueType', 'any')); %#ok<NASGU>
k0_try('second.openProject.first', @() openProject(root).Name);
k0_try('second.openProject.other.whileopen', @() openProject(named).Name);
k0_try('second.first.isLoaded.after', @() isLoaded(proj));
call('second.close.named', @() close(currentProject()));

%% Deleting a project.
call('delete.open', @() openProject(root));
call('delete.whileloaded', @() matlab.project.deleteProject(root));
k0_try('delete.whileloaded.isLoaded', @() isLoaded(currentProject()));
call('delete.close', @() close(currentProject()));
call('delete.ok', @() matlab.project.deleteProject(root));
k0_try('delete.files.left', @() relFiles(root));
call('delete.again', @() matlab.project.deleteProject(root));
k0_try('delete.open.after', @() openProject(root));
call('delete.prjfile', @() matlab.project.deleteProject(fullfile(named, 'k20named.prj')));

%% The catalog texts the Compiler Task Manager and the projects are made of.
texts = {'deployment:configmgr:CONFIGURATION_MANAGER_BUTTON_LABEL', 'deployment:configmgr:CONFIGURATION_MANAGER_BUTTON_DESC', ...
    'deployment:configmgr:ERROR_CREATE_ALREADY_EXISTS', 'deployment:configmgr:ERROR_REMOVE_NOT_FOUND', ...
    'deployment:configmgr:ERROR_RENAME_NOT_FOUND', 'deployment:configmgr:ERROR_RENAME_ALREADY_EXISTS', ...
    'deployment:configmgr:ERROR_NAME_ALREADY_EXISTS', 'deployment:configmgr:ERROR_PROPERTY_CONFIG_NOT_FOUND', ...
    'deployment:configmgr:ERROR_PROPERTY_CONFIG_NOT_VALID', 'deployment:configmgr:ERROR_PROPERTY_NOT_FOUND', ...
    'deployment:configmgr:ERROR_PROPERTY_SET_FAILURE', 'deployment:configmgr:ERROR_INVALID_CONFIG_TYPE', ...
    'deployment:configmgr:INFO_LABEL_CATEGORY_CREATED', ...
    'deployment:model:CONFIGTYPE_STANDALONEAPP', 'deployment:model:CONFIGTYPE_MPSARCHIVE', 'deployment:model:CONFIGTYPE_PYTHONPACKAGE', ...
    'deployment:model:CONFIGTYPE_DOTNETASSEMBLY', 'deployment:model:CONFIGTYPE_CPPSHAREDLIB', 'deployment:model:CONFIGTYPE_CSHAREDLIB', ...
    'deployment:model:CONFIGTYPE_EXCELADDIN', 'deployment:model:CONFIGTYPE_JAVAPACKAGE', 'deployment:model:CONFIGTYPE_COMCOMPONENT', ...
    'deployment:model:CONFIGTYPE_WEBAPPARCHIVE', 'deployment:model:CONFIGTYPE_TOOLBOX', 'deployment:model:CONFIGTYPE_APP', ...
    'deployment:model:CONFIGTYPE_EXCELCLIENTFORMPS', 'deployment:model:TITLE_PROFILE_NAME', ...
    'deployment:project_labels:LABEL_DEFAULT_CATEGORY_NAME', 'deployment:project_labels:LABEL_DEFAULT_CATEGORY_NAME_TEMPLATE', ...
    'deployment:project_labels:LABEL_INDEXED_CATEGORY_NAME_TEMPLATE', 'deployment:project_labels:LABEL_NAME_ADDITIONAL_FILE', ...
    'deployment:project_labels:LABEL_NAME_EXE_ICON_FILE', 'deployment:project_labels:LABEL_NAME_EXE_SPLASH_FILE', ...
    'deployment:project_labels:LABEL_NAME_EXPORTED_FUNCTION_FILE', 'deployment:project_labels:LABEL_NAME_FXN_SIGNATURES_FILE', ...
    'deployment:project_labels:LABEL_NAME_HELP_TEXT_FILE', 'deployment:project_labels:LABEL_NAME_INSTALLER_ADD_REMOVE_PRGRMS_ICON_FILE', ...
    'deployment:project_labels:LABEL_NAME_INSTALLER_ADDITIONAL_FILE', 'deployment:project_labels:LABEL_NAME_INSTALLER_ICON_FILE', ...
    'deployment:project_labels:LABEL_NAME_INSTALLER_LOGO_FILE', 'deployment:project_labels:LABEL_NAME_INSTALLER_SPLASH_FILE', ...
    'deployment:project_labels:LABEL_NAME_MAIN_FILE', 'deployment:project_labels:LABEL_NAME_SAMPLES_FILE', ...
    'deployment:project_labels:LABEL_NAME_STRONG_KEY_FILE', 'deployment:project_labels:LABEL_NAME_APP_GALLERY_FILE', ...
    'deployment:project_labels:LABEL_NAME_DOCUMENTATION_FILE', 'deployment:project_labels:LABEL_NAME_GETTING_STARTED_GUIDE_FILE', ...
    'deployment:project_labels:LABEL_NAME_TOOLBOX_FILE', 'deployment:project_labels:LABEL_NAME_TOOLBOX_IMAGE_FILE', ...
    'compiler_ui_common:messages:createTask', 'compiler_ui_common:messages:deploymentTaskName', 'compiler_ui_common:messages:deploymentTasks', ...
    'compiler_ui_common:messages:deploymentTasksInProject', 'compiler_ui_common:messages:enterName', 'compiler_ui_common:messages:manageTasks', ...
    'compiler_ui_common:messages:nameTaken', 'compiler_ui_common:messages:newTask', 'compiler_ui_common:messages:noTasksFound', ...
    'compiler_ui_common:messages:noTasksDetected', 'compiler_ui_common:messages:outsideProjectGreeting', 'compiler_ui_common:messages:loadingTasks', ...
    'compiler_ui_common:messages:tasksDetected', 'compiler_ui_common:messages:specifyDetails', 'compiler_ui_common:messages:targetType', ...
    'compiler_ui_common:messages:taskName', 'compiler_ui_common:messages:setupCompilerDocumentNew', 'compiler_ui_common:messages:setupCompilerRediscover', ...
    'compiler_ui_common:messages:compilerInfoNew', 'compiler_ui_common:messages:outsideProjectInformCompiler', 'compiler_ui_common:messages:addTask', ...
    'compiler_ui_common:messages:addTaskExisting', 'compiler_ui_common:messages:addTaskExistingProjectHistory', 'compiler_ui_common:messages:addTaskExistingProjectBrowse', ...
    'compiler_ui_common:messages:projectCreateError', 'compiler_ui_common:messages:setupCompilerSelectedFilesNew', 'compiler_ui_common:messages:setupCompilerSelectedFilesNewLibrary', ...
    'compiler_ui_common:deploymentscript:notAProjectPath', ...
    'MATLAB:project:api:NoProjectCurrentlyLoaded', 'MATLAB:project:api:NoProjectFound', 'MATLAB:project:api:CanOnlyLoadPRJFile', ...
    'MATLAB:project:api:FileDoesNotExist', 'MATLAB:project:api:FileDoesNotExistUnderRoot', 'MATLAB:project:api:FileIsNotInProject', ...
    'MATLAB:project:api:PathNotInProject', 'MATLAB:project:api:IsNotPathFolder', 'MATLAB:project:api:CategoryDoesNotExist', ...
    'MATLAB:project:api:CategoryExists', 'MATLAB:project:api:LabelExists', 'MATLAB:project:api:LabelDoesNotExist', ...
    'MATLAB:project:api:ProjectRootFolderNotFound', 'MATLAB:project:api:DefaultProjectFolderName', 'MATLAB:project:api:StaleProjectHandle', ...
    'MATLAB:project:api:SpecifiedProjectNotLoaded', 'MATLAB:project:api:FileIsNotAShortcut', 'MATLAB:project:api:WrongLabelDataType', ...
    'MATLAB:project:management:NotAProject', 'MATLAB:project:management:DeleteFailNotAProject', 'MATLAB:project:management:DeleteFailLoaded', ...
    'MATLAB:project:management:FileNotInProjectRoot', 'MATLAB:project:management:FileIsMetadataError', 'MATLAB:project:management:RootFolderNotAddable', ...
    'MATLAB:project:management:ShortcutCanNotBeFolder', 'MATLAB:project:management:StartupFileCanNotBeFolder', 'MATLAB:project:management:ShutdownFileCanNotBeFolder', ...
    'MATLAB:project:management:EntryPointGroupDefaultName', 'MATLAB:project:management:CategoryExistsByName', 'MATLAB:project:management:LabelExistsByName', ...
    'MATLAB:project:management:DuplicatePathEntryException', 'MATLAB:project:management:CanNotModifyReadOnlyCategory', 'MATLAB:project:management:CanNotModifyReadOnlyLabel', ...
    'MATLAB:project:creation:NotInProject'};
for k = 1:numel(texts)
    k0_try(['text.' texts{k}], @() getText(texts{k}));
end
end

function printScript(cfg, root, target, package)
b = compiler.internal.deployScriptGenerator.DeployScriptBuilder(cfg, root);
b.buildTarget = compiler.internal.DeploymentTarget.(target);
if ~isempty(package)
    b.packageTarget = compiler.internal.PackageType.(package);
end
txt = b.generateDeploymentScript();
fprintf('%s', strrep(char(txt), sprintf('\r\n'), newline));
if ~endsWith(char(txt), newline), fprintf('\n'); end
end

function t = getText(key)
% The catalog's text with its holes shown as {0}, {1}, {2}: message() takes the count it needs.
holes = {'{0}', '{1}', '{2}'};
for n = 0:3
    try
        t = k0_flat(getString(message(key, holes{1:n})));
        return
    catch
    end
end
t = '<no text>';
end

function v = setget(obj, name, value)
obj.(name) = value;
v = obj.(name);
end

function s = describeFile(pf, root)
s = sprintf('%s %s labels[%s]', class(pf), rel(pf.Path, root), strjoin(labelsOf(pf), ','));
end

function s = describeFiles(pfs, root)
s = arrayfun(@(f) describeFile(f, root), pfs, 'UniformOutput', false);
end

function s = describePath(pf, root)
s = sprintf('%s File=%s StoredLocation=%s', class(pf), rel(pf.File, root), k0_flat(string(pf.StoredLocation)));
end

function s = describeShortcut(sc, root)
s = sprintf('%s Name=%s Group=%s File=%s', class(sc), sc.Name, sc.Group, rel(sc.File, root));
end

function s = describeCategory(c)
s = sprintf('%s Name=%s DataType=%s SingleValued=%d labels[%s]', class(c), c.Name, c.DataType, c.SingleValued, strjoin({c.LabelDefinitions.Name}, ','));
end

function s = describeDefinition(d)
s = sprintf('%s Name=%s CategoryName=%s', class(d), d.Name, d.CategoryName);
end

function s = describeLabel(l, root)
s = arrayfun(@(e) sprintf('%s Name=%s CategoryName=%s DataType=%s Data=%s File=%s', class(e), e.Name, e.CategoryName, ...
    k0_flat(string(e.DataType)), k0_show(e.Data), rel(e.File, root)), l, 'UniformOutput', false);
end

function s = describeLabelStruct(st)
f = fieldnames(st);
parts = cellfun(@(n) sprintf('%s=%s', n, k0_flat(string(st.(n)))), f, 'UniformOutput', false);
s = strjoin(parts', ' ');
end

function c = labelsOf(pf)
if isempty(pf), c = {}; return; end
c = arrayfun(@(l) sprintf('%s:%s', l.CategoryName, l.Name), pf.Labels, 'UniformOutput', false);
end

function r = rel(p, root)
if iscell(p)
    r = cellfun(@(e) rel(e, root), p, 'UniformOutput', false);
    return
end
if isstring(p) && ~isscalar(p)
    r = arrayfun(@(e) string(rel(e, root)), p);
    return
end
r = char(p);
if startsWith(r, [root filesep]), r = r(numel(root) + 2:end); end
if strcmp(r, root), r = '<root>'; end
end

function c = relAll(files, root)
c = arrayfun(@(f) rel(f.Path, root), files, 'UniformOutput', false);
end

function c = relFiles(root)
d = dir(fullfile(root, '**', '*'));
d = d(~[d.isdir]);
c = sort(arrayfun(@(e) strrep(fullfile(e.folder, e.name), [root filesep], ''), d, 'UniformOutput', false))';
end

function ids(root)
% The definition files' names for the nodes whose place is fixed.
d = dir(fullfile(root, 'resources', 'project', '**', '*p.xml'));
for k = 1:numel(d)
    txt = fileread(fullfile(d(k).folder, d(k).name));
    loc = regexp(txt, 'location="([^"]*)"', 'tokens', 'once');
    typ = regexp(txt, 'type="([^"]*)"', 'tokens', 'once');
    if isempty(loc) || isempty(typ), continue; end
    if ~isempty(regexp(loc{1}, '[0-9a-f]{8}-[0-9a-f]{4}', 'once')) || contains(loc{1}, 'MathWorksDeploymentConfig{'), continue; end
    folder = strrep(d(k).folder, fullfile(root, 'resources', 'project'), '');
    folder = strrep(folder, filesep, '/');
    fprintf('id.%s%s/%s\t%s\n', strrep(loc{1}, ' ', '_'), folder, typ{1}, d(k).name(1:end - numel('p.xml')));
end
end

function tree = snapshot(label, root, tree)
% What changed under the project's definition files since the last snapshot: added, changed, removed.
d = dir(fullfile(root, '**', '*'));
d = d(~[d.isdir]);
names = sort(arrayfun(@(e) strrep(fullfile(e.folder, e.name), [root filesep], ''), d, 'UniformOutput', false));
now = containers.Map('KeyType', 'char', 'ValueType', 'any');
for k = 1:numel(names)
    n = names{k};
    fi = dir(fullfile(root, n));
    if startsWith(n, ['resources' filesep 'project']) || endsWith(n, '.prj')
        txt = fileread(fullfile(root, n));
        txt = strrep(txt, sprintf('\r\n'), newline);
        now(n) = txt;
    else
        now(n) = sprintf('<%d bytes>', fi.bytes);
    end
end
added = {}; changed = {}; removed = {};
ks = keys(now);
for k = 1:numel(ks)
    if ~isKey(tree, ks{k}), added{end + 1} = ks{k}; %#ok<AGROW>
    elseif ~strcmp(tree(ks{k}), now(ks{k})), changed{end + 1} = ks{k}; %#ok<AGROW>
    end
end
ks = keys(tree);
for k = 1:numel(ks)
    if ~isKey(now, ks{k}), removed{end + 1} = ks{k}; end %#ok<AGROW>
end
fprintf('tree.%s\t%d files, %d added, %d changed, %d removed\n', label, now.Count, numel(added), numel(changed), numel(removed));
for k = 1:numel(added), printFile('+', added{k}, now(added{k})); end
for k = 1:numel(changed), printFile('~', changed{k}, now(changed{k})); end
for k = 1:numel(removed), fprintf('  -%s\n', strrep(removed{k}, filesep, '/')); end
tree = now;
end

function printFile(mark, name, txt)
fprintf('  %s%s\n', mark, strrep(name, filesep, '/'));
if startsWith(txt, '<') && endsWith(txt, 'bytes>'), return; end
lines = splitlines(txt);
if ~isempty(lines) && isempty(lines{end}), lines(end) = []; end
for j = 1:numel(lines)
    fprintf('  |%s\n', lines{j});
end
end

function call(key, f)
% One call: what it printed, ok or its refusal, then its warning.
lastwarn('', '');
try
    txt = evalc('f();');
    if ~isempty(strtrim(txt))
        fprintf('%s.printed\n', key);
        printLines(txt);
    end
    fprintf('%s\tok\n', key);
catch ME
    fprintf('%s\tERR %s | %s\n', key, ME.identifier, k0_flat(ME.message));
end
k0_warned(key);
end

function t = firstLine(txt)
lines = splitlines(strtrim(regexprep(char(txt), '<a [^>]*>|</a>', '')));
t = strtrim(lines{1});
end

function printLines(txt)
txt = strrep(txt, sprintf('\r\n'), newline);
txt = regexprep(txt, '<a [^>]*>|</a>', '');
lines = splitlines(txt);
if ~isempty(lines) && isempty(lines{end}), lines(end) = []; end
for j = 1:numel(lines)
    fprintf('  |%s\n', lines{j});
end
end

function writeText(file, text)
fid = fopen(file, 'w');
fprintf(fid, '%s', text);
fclose(fid);
end
