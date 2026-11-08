function build_k22(work)
%BUILD_K22 Builds the acceptance set of the compiler and SDK plan (stage K22) from one source tree,
%one build per target, under R2025b's matlab -batch -noFigureWindows (accept-k22.ps1 -Engine R2025b)
%and unchanged under JGraph's jgraph -batch (-Engine JGraph). One key<TAB>value line per build: ok
%with the deliverables it left (sorted, the folders relative to the build's), or ERR with the
%identifier and the message. Nothing built is run here; the clients are accept-k22.ps1's.
beep('off');
here = fileparts(mfilename('fullpath'));
if exist(work, 'dir'), rmdir(work, 's'); end
mkdir(work);
src = fullfile(work, 'src');
mkdir(src);
copyfile(fullfile(here, 'src', 'k22', '*'), src);
copyfile(fullfile(here, 'src', 'k0_quiet.m'), src);
old = cd(src);
cleanup = onCleanup(@() cd(old));
fn = @(names) cellfun(@(n) fullfile(src, [n '.m']), names, 'UniformOutput', false);
lib = fn({'acc_add', 'acc_stats', 'acc_greet', 'acc_scale', 'acc_count', 'acc_fail', 'acc_quiet'});

built('console', @() compiler.build.standaloneApplication(fullfile(src, 'acc_tool.m'), 'ExecutableName', 'acc_tool', ...
    'AdditionalFiles', {fullfile(src, 'acc_data.txt')}, 'OutputDir', fullfile(work, 'console')), fullfile(work, 'console'));
built('windowed', @() compiler.build.standaloneWindowsApplication(fullfile(src, 'acc_app.m'), 'ExecutableName', 'acc_app', ...
    'OutputDir', fullfile(work, 'windowed')), fullfile(work, 'windowed'));
% The console program over the +acc package and the @AccSeries class folder (packages and class
% folders plan, stage P6): the analysis follows the dotted name and takes the folder whole.
built('pkg', @() compiler.build.standaloneApplication(fullfile(src, 'acc_pkgtool.m'), 'ExecutableName', 'acc_pkgtool', ...
    'OutputDir', fullfile(work, 'pkg')), fullfile(work, 'pkg'));
built('net', @() compiler.build.dotNETAssembly(lib, 'AssemblyName', 'AccNet', 'ClassName', 'AccClass', ...
    'FrameworkVersion', '5.0', 'OutputDir', fullfile(work, 'net')), fullfile(work, 'net'));
built('data', @() compiler.build.dotNETAssembly(lib, 'Interface', 'matlab-data', 'AssemblyName', 'AccData', ...
    'EmbedArchive', 'off', 'OutputDir', fullfile(work, 'data')), fullfile(work, 'data'));
built('c', @() compiler.build.cSharedLibrary(lib, 'LibraryName', 'acclib', 'OutputDir', fullfile(work, 'c')), fullfile(work, 'c'));
built('cpp', @() compiler.build.cppSharedLibrary(lib, 'LibraryName', 'acccpp', 'OutputDir', fullfile(work, 'cpp')), fullfile(work, 'cpp'));
built('python', @() compiler.build.pythonPackage(lib, 'PackageName', 'accpkg', 'OutputDir', fullfile(work, 'python')), fullfile(work, 'python'));
rServer = built('server', @() compiler.build.productionServerArchive(lib, 'ArchiveName', 'accarch', ...
    'FunctionSignatures', fullfile(src, 'acc_sig.json'), 'OutputDir', fullfile(work, 'server')), fullfile(work, 'server'));
% The Excel add-in (stage K17: both engines build it and register it for the current user; the module
% is written too, and JGraph writes the add-in workbook beside it where R2025b's .xla needs an Excel
% that trusts its VBA project), the Excel client for the Production Server from the archive's results,
% and the client's installer pointed at the server accept-k22.ps1 runs (K17g).
built('excel', @() compiler.build.excelAddIn(lib, 'AddInName', 'accxl', 'ClassName', 'AccXl', ...
    'GenerateVisualBasicFile', 'on', 'OutputDir', fullfile(work, 'excel')), fullfile(work, 'excel'));
if ~isempty(rServer)
    rClient = built('xlclient', @() compiler.build.excelClientForProductionServer(rServer, 'AddInName', 'accxlc', ...
        'ClassName', 'AccXlc', 'OutputDir', fullfile(work, 'xlclient')), fullfile(work, 'xlclient'));
    if ~isempty(rClient)
        built('xlinstaller', @() packaged(@() compiler.package.excelClientForProductionServer(rClient, ...
            'ServerURL', 'http://localhost:9981', 'OutputDir', fullfile(work, 'xlinstaller'))), fullfile(work, 'xlinstaller'));
    end
end
end

function r = packaged(f)
% compiler.package.excelClientForProductionServer answers nothing; the build line shows the kind.
f();
r = struct('BuildType', 'excelClientForProductionServerInstaller');
end

function r = built(key, f, folder)
r = [];
try
    r = f();
    fprintf('build.%s\tok %s\n', key, r.BuildType);
catch ME
    fprintf('build.%s\tERR %s | %s\n', key, ME.identifier, flat(ME.message));
end
files = {};
if exist(folder, 'dir')
    files = walk(folder, '');
end
keep = {'.exe', '.dll', '.ctf', '.h', '.lib', '.hpp', '.py', '.bas', '.xla', '.xlam', '.txt', '.toml', '.json'};
shown = {};
for k = 1:numel(files)
    [~, name, ext] = fileparts(files{k});
    if any(strcmp(ext, keep)) && ~strcmp(ext, '.log') && ~endsWith(files{k}, '.deps.json') && ~strcmp([name ext], 'buildresult.json')
        shown{end + 1} = files{k}; %#ok<AGROW>
    end
end
shown = sort(shown);
fprintf('files.%s\t%s\n', key, strjoin(shown, ' | '));
end

function out = walk(folder, rel)
% Every file under the folder, as a sorted list of relative paths with forward slashes (JGraph's
% dir does not search **).
out = {};
d = dir(folder);
for k = 1:numel(d)
    if strcmp(d(k).name, '.') || strcmp(d(k).name, '..'), continue; end
    name = d(k).name;
    if isempty(rel), r = name; else, r = [rel '/' name]; end
    if d(k).isdir
        out = [out, walk(fullfile(folder, name), r)]; %#ok<AGROW>
    else
        out{end + 1} = r; %#ok<AGROW>
    end
end
end

function s = flat(s)
s = regexprep(char(s), '<a [^>]*>|</a>', '');
s = strrep(s, sprintf('\r'), '');
s = strrep(s, newline, ' / ');
end
