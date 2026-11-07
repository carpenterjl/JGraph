function p7_project(work)
%P7_PROJECT The acceptance library as a MATLAB project (packages and class folders plan, stage
%P7), in either engine: a project created under <work>\proj, the library copied in, its folders
%and the two scripts added, its files listed, the library on the project path, the project closed
%and opened again. One key<TAB>value line each.
beep('off');
here = fileparts(mfilename('fullpath'));
root = fullfile(work, 'proj');
if exist(root, 'dir'), rmdir(root, 's'); end
mkdir(root);
proj = matlab.project.createProject(root);
fprintf('created\t%s %s\n', class(proj), proj.Name);
mkdir(fullfile(root, 'lib'));
copyfile(fullfile(here, 'lib', '*'), fullfile(root, 'lib'));
copyfile(fullfile(here, 'p7_tool.m'), root);
copyfile(fullfile(here, 'p7_main.m'), root);
added = addFolderIncludingChildFiles(proj, fullfile(root, 'lib'));
fprintf('added\t%d %s\n', numel(added), class(added));
addFile(proj, 'p7_tool.m');
addFile(proj, 'p7_main.m');
addPath(proj, fullfile(root, 'lib'));
files = proj.Files;
paths = arrayfun(@(f) short(f.Path, root), files, 'UniformOutput', false);
paths = sort(paths(:)');
fprintf('files.n\t%d\n', numel(paths));
for k = 1:numel(paths), fprintf('files[%d]\t%s\n', k, paths{k}); end
fprintf('inproject\t%d %d %d\n', matlab.project.isFileInProject(fullfile(root, 'lib', '+signal', 'Trace.m')), ...
    matlab.project.isFileInProject(fullfile(root, 'lib', '@Series', 'private', 'weight.m')), ...
    matlab.project.isFileInProject(fullfile(root, 'nosuch.m')));
fprintf('path.n\t%d\n', numel(proj.ProjectPath));
fprintf('resolves.open\t%d %d\n', exist('signal.Trace'), exist('Series'));
close(proj);
fprintf('resolves.closed\t%d %d\n', exist('signal.Trace'), exist('Series'));
rehash;
fprintf('resolves.rehash\t%d %d\n', exist('signal.Trace'), exist('Series'));
again = openProject(root);
fprintf('reopened\t%s %d %d\n', again.Name, numel(again.Files), numel(again.ProjectPath));
t = signal.Trace.fromFile(fullfile(here, 'rt', 'trace.txt'));
fprintf('runs\t%s %g\n', t.Name, total(Series.ofTrace(t)));
close(again);
end

function s = short(p, root)
s = strrep(strrep(char(p), [root filesep], ''), '\', '/');
end
