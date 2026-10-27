function probe_k18_package(work)
%PROBE_K18_PACKAGE Packages stage K18's installers (compiler and SDK plan) with
%compiler.package.installer, compiler.package.InstallerOptions and compiler.runtime.customInstaller,
%and asks mcrinstaller, compiler.runtime.version and the helpers the installer is built on. Run
%under matlab -batch -noFigureWindows over a standalone build of src\k6\k6_hello.m; no installer is
%run here (record-k18.ps1 reads what each carries with k18-payload.py). One key<TAB>value line per
%question, a display or a file's text as indented lines under its key.
beep('off'); % the refusals below would each beep
here = fileparts(mfilename('fullpath'));
addpath(fullfile(here, 'src'));
if exist(work, 'dir'), rmdir(work, 's'); end
mkdir(work);
src = fullfile(work, 'src');
mkdir(src);
copyfile(fullfile(here, 'src', 'k6', 'k6_hello.m'), src);
old = cd(src);
cleanup = onCleanup(@() cd(old));

% The images: an icon, a logo and a splash of sizes the packager must resize, and an animated GIF.
[x, y] = meshgrid(linspace(0, 1, 64));
imwrite(uint8(255 * cat(3, x, y, 1 - x)), fullfile(src, 'k18_icon.png'));
[x, y] = meshgrid(linspace(0, 1, 300), linspace(0, 1, 120));
imwrite(uint8(255 * cat(3, y, x, 0.5 + 0 * x)), fullfile(src, 'k18_logo.png'));
[x, y] = meshgrid(linspace(0, 1, 250));
imwrite(uint8(255 * cat(3, x, 0.5 + 0 * x, y)), fullfile(src, 'k18_splash.png'));
imwrite(uint8(zeros(8, 8)), gray(2), fullfile(src, 'k18_anim.gif'), 'LoopCount', Inf);
imwrite(uint8(ones(8, 8)), gray(2), fullfile(src, 'k18_anim.gif'), 'WriteMode', 'append');
writeText(fullfile(src, 'k18data.txt'), sprintf('data\n'));
mkdir(fullfile(src, 'k18folder'));
writeText(fullfile(src, 'k18folder', 'inside.txt'), sprintf('inside\n'));
writeText(fullfile(src, 'k18empty.txt'), '');
writeText(fullfile(src, 'k18bad.txt'), 'hello');
writeText(fullfile(src, 'k18one.txt'), sprintf('35010\t'));

% The builds the installers package: a console program, a windowed one and a Python package.
b = fullfile(work, 'b');
t = tic;
r = compiler.build.standaloneApplication('k6_hello.m', 'ExecutableName', 'k18hello', 'OutputDir', b, ...
    'ExecutableIcon', fullfile(src, 'k18_icon.png'));
fprintf('build.seconds\t%.1f\n', toc(t));
fprintf('build.Files\t%s\n', strjoin(strrep(r.Files(:)', [b filesep], '<OUT>\'), ' | '));
printFile('build.requiredMCRProducts', fullfile(b, 'requiredMCRProducts.txt'));
bw = fullfile(work, 'bw');
rw = compiler.build.standaloneWindowsApplication('k6_hello.m', 'ExecutableName', 'k18win', 'OutputDir', bw);
bp = fullfile(work, 'bp');
rp = compiler.build.pythonPackage('k6_hello.m', 'PackageName', 'k18py', 'OutputDir', bp);
fprintf('build.py.Files\t%s\n', strjoin(strrep(rp.Files(:)', [bp filesep], '<OUT>\'), ' | '));
exe = fullfile(b, 'k18hello.exe');
txt = fullfile(b, 'requiredMCRProducts.txt');
copyfile(exe, fullfile(src, 'k18hello.exe'));

%% The options class.
show('opts.default', compiler.package.InstallerOptions('ApplicationName', 'k18app'));
k0_try('opts.noargs', @() compiler.package.InstallerOptions());
k0_try('opts.noname', @() compiler.package.InstallerOptions('Version', '2.0'));
k0_try('opts.odd', @() compiler.package.InstallerOptions('ApplicationName'));
k0_try('opts.unknown', @() compiler.package.InstallerOptions('ApplicationName', 'k18app', 'Bogus', 1));
k0_try('opts.nameonly', @() compiler.package.InstallerOptions('k18app'));
k0_try('opts.partial', @() compiler.package.InstallerOptions('ApplicationName', 'k18app', 'Author', 'x'));
k0_try('opts.case', @() compiler.package.InstallerOptions('applicationname', 'k18app', 'VERSION', '2.0'));
k0_try('opts.twice', @() compiler.package.InstallerOptions('ApplicationName', 'k18app', 'Version', '2.0', 'Version', '3.0'));
many = repmat({'Version', '1.0'}, 1, 20);
k0_try('opts.toomany', @() compiler.package.InstallerOptions('ApplicationName', 'k18app', many{:}));
show('opts.results', compiler.package.InstallerOptions(r));
show('opts.results.win', compiler.package.InstallerOptions(rw));
show('opts.results.py', compiler.package.InstallerOptions(rp));
show('opts.results.nv', compiler.package.InstallerOptions(r, 'ApplicationName', 'Other', 'Shortcut', fullfile(src, 'k18data.txt'), ...
    'AddRemoveProgramsIcon', fullfile(src, 'k18_icon.png')));
k0_try('opts.results.two', @() compiler.package.InstallerOptions([r rw]));
k0_try('opts.results.first', @() compiler.package.InstallerOptions(r, 'Version', '2.0'));
o = compiler.package.InstallerOptions(r);
show('opts.results.properties', properties(o));
fprintf('opts.results.class\t%s\n', class(o));

% Every property set with values it takes and values it refuses.
o = compiler.package.InstallerOptions('ApplicationName', 'k18app');
sets = {
    'ApplicationName', {'My App', "Str App", 'a/b', 'a\b', 'a:b', 'a*b', '', 5, {'a'}, sprintf('a\tb'), ['ab'; 'cd'], "", 'a b  c'}
    'InstallerName', {'Setup Me', 'x/y', '', 7, "named", 'a|b'}
    'Version', {'1', '2.3.4.5.6', 'a.b', '1.0-beta', '', 3, "2.0", '1..2', ' 1.0', '1.0 ', '1,2', '007.08'}
    'RuntimeDelivery', {'web', 'installer', 'none', 'WEB', 'Web', "installer", 'offline', 5, '', {'web'}}
    'PackageType', {'auto', 'zip', 'ZIP', 'Auto', 'tar', '', 5}
    'OptionalDependencies', {'all', 'none', 'ALL', 'some', 1, "none", ''}
    'Verbose', {'on', 'off', true, 1, 'maybe', 2, "on", 'ON', 0}
    'InstallerSplash', {fullfile(src, 'k18_splash.png'), 'k18_icon.png', 'nosuch.png', '', fullfile(src, 'k18_anim.gif'), fullfile(src, 'k18data.txt'), 5, ""}
    'InstallerIcon', {fullfile(src, 'k18_icon.png'), 'k18_logo.png', 'nosuch.png', '', fullfile(src, 'k18_anim.gif'), fullfile(src, 'k18data.txt'), 5}
    'InstallerLogo', {fullfile(src, 'k18_logo.png'), 'k18_splash.png', 'nosuch.png', '', fullfile(src, 'k18_anim.gif'), 5}
    'AddRemoveProgramsIcon', {'', fullfile(src, 'k18_icon.png'), 'k18_logo.png', 'nosuch.png', fullfile(src, 'k18_anim.gif'), "", 5, [], fullfile(src, 'k18data.txt')}
    'Shortcut', {'', fullfile(src, 'k18data.txt'), 'nosuch', fullfile(src, 'k18folder'), 'k18data.txt', "", 5, []}
    'AdditionalFiles', {{}, 'k18data.txt', {fullfile(src, 'k18data.txt'), fullfile(src, 'k18folder')}, 'nosuch', "", ["k18data.txt", "k18bad.txt"], 5, {5}, '', []}
    'OutputDir', {'out18', 'C:\k18\x\y', '.', '.\rel', fullfile(src, 'k18data.txt'), 'a<b', 5, '', "strdir", 'sub/deep'}
    'DefaultInstallationDir', {'C:\Apps\K18', '', 5, "%ProgramFiles%\x", 'rel\dir', {'a'}}
    'AuthorName', {'Frog', 5, "", {'a'}, '', ['ab'; 'cd']}
    'AuthorEmail', {'frog@pond.org', 5, ''}
    'AuthorCompany', {'Boston Common', 5, ''}
    'Summary', {'The best', 5, sprintf('two\nlines')}
    'Description', {'Long text', 5, ''}
    'InstallationNotes', {sprintf('note 1\nnote 2'), 5}
    };
for k = 1:size(sets, 1)
    [prop, values] = sets{k, :};
    for j = 1:numel(values)
        key = sprintf('set.%s.%d', prop, j);
        fprintf('%s.value\t%s\n', key, describe(values{j}));
        lastwarn('', '');
        try
            v = setAndRead(o, prop, values{j});
            fprintf('%s\t%s\n', key, k0_flat(v));
        catch ME
            fprintf('%s\tERR %s | %s\n', key, ME.identifier, k0_flat(ME.message));
        end
        k0_warned(key);
    end
end

% The dependent properties: what each answers before and after a set, and after the name changes.
o = compiler.package.InstallerOptions('ApplicationName', 'My App');
fprintf('derived.InstallerName\t%s\n', o.InstallerName);
fprintf('derived.OutputDir\t%s\n', o.OutputDir);
fprintf('derived.DefaultInstallationDir\t%s\n', o.DefaultInstallationDir);
o.OutputDir = 'chosen';
fprintf('derived.OutputDir.set\t%s\n', o.OutputDir);
o.DefaultInstallationDir = 'D:\There';
o.InstallerName = 'MySetup';
o.ApplicationName = 'Renamed';
fprintf('derived.after.rename\t%s | %s | %s\n', o.InstallerName, o.OutputDir, o.DefaultInstallationDir);
o2 = compiler.package.InstallerOptions('ApplicationName', 'Renamed');
fprintf('derived.fresh\t%s | %s | %s\n', o2.InstallerName, o2.OutputDir, o2.DefaultInstallationDir);
show('derived.display', o);
s = struct(o); %#ok<NASGU>
k0_try('derived.saveload', @() saveLoad(o));

%% compiler.package.installer.
d = fullfile(work, 'web');
package('web', @() compiler.package.installer(r, 'OutputDir', d, 'Verbose', 'on'), d);
d = fullfile(work, 'quiet');
package('quiet', @() compiler.package.installer(r, 'OutputDir', d), d);
d = fullfile(work, 'named');
package('named', @() compiler.package.installer(r, 'OutputDir', d, 'Verbose', 'on', 'InstallerName', 'k18setup', ...
    'AuthorName', 'Frog', 'AuthorEmail', 'frog@pond.org', 'AuthorCompany', 'Boston Common', 'Summary', 'The best app ever', ...
    'Description', sprintf('Makes a magic square for you.\nAnd more.'), 'InstallationNotes', sprintf('Needs a pond.\nAnd lilies.'), ...
    'Version', '2.5', 'DefaultInstallationDir', 'C:\Apps\K18', 'InstallerIcon', fullfile(src, 'k18_icon.png'), ...
    'InstallerLogo', fullfile(src, 'k18_logo.png'), 'InstallerSplash', fullfile(src, 'k18_splash.png'), ...
    'AddRemoveProgramsIcon', fullfile(src, 'k18_logo.png'), 'AdditionalFiles', {fullfile(src, 'k18data.txt'), fullfile(src, 'k18folder')}), d);
d = fullfile(work, 'zip');
package('zip', @() compiler.package.installer(r, 'OutputDir', d, 'PackageType', 'zip', 'Verbose', 'on'), d);
d = fullfile(work, 'offline');
package('installer', @() compiler.package.installer(r, 'OutputDir', d, 'RuntimeDelivery', 'installer', 'Verbose', 'on'), d);
d = fullfile(work, 'none');
package('none', @() compiler.package.installer(r, 'OutputDir', d, 'RuntimeDelivery', 'none', 'Verbose', 'on'), d);
d = fullfile(work, 'nonezip');
package('none.zip', @() compiler.package.installer(r, 'OutputDir', d, 'RuntimeDelivery', 'none', 'PackageType', 'zip'), d);
d = fullfile(work, 'optnone');
package('optional.none', @() compiler.package.installer(r, 'OutputDir', d, 'OptionalDependencies', 'none'), d);
d = fullfile(work, 'opts');
o = compiler.package.InstallerOptions(r, 'OutputDir', d, 'InstallerName', 'FromOptions', 'Version', '3.1.4');
package('options', @() compiler.package.installer(r, 'Options', o), d);
package('options.extra', @() compiler.package.installer(r, 'Options', o, 'Verbose', 'on'), d);
package('options.wrongclass', @() compiler.package.installer(r, 'Options', 5), d);
package('options.other', @() compiler.package.installer(r, 'Options', compiler.build.StandaloneApplicationOptions('k6_hello.m')), d);
d = fullfile(work, 'win');
package('results.win', @() compiler.package.installer(rw, 'OutputDir', d), d);
d = fullfile(work, 'py');
package('results.py', @() compiler.package.installer(rp, 'OutputDir', d), d);
d = fullfile(work, 'files');
package('files.exe', @() compiler.package.installer({exe}, txt, 'ApplicationName', 'k18f', 'OutputDir', d), d);
d = fullfile(work, 'filesshort');
package('files.shortcut', @() compiler.package.installer(exe, txt, 'ApplicationName', 'k18fs', 'OutputDir', d, 'Shortcut', exe), d);
d = fullfile(work, 'filesstr');
package('files.string', @() compiler.package.installer(string(exe), string(txt), "ApplicationName", "k18str", "OutputDir", d, "Shortcut", exe), d);
d = fullfile(work, 'filesfolder');
package('files.folder', @() compiler.package.installer({fullfile(src, 'k18folder')}, txt, 'ApplicationName', 'k18fd', 'OutputDir', d), d);
d = fullfile(work, 'filesinside');
package('files.folder.shortcut', @() compiler.package.installer({fullfile(src, 'k18folder'), exe}, txt, 'ApplicationName', 'k18fi', ...
    'OutputDir', d, 'Shortcut', fullfile(src, 'k18folder', 'inside.txt')), d);
d = fullfile(work, 'filesone');
package('files.onetxt', @() compiler.package.installer(exe, fullfile(src, 'k18one.txt'), 'ApplicationName', 'k18one', 'OutputDir', d, 'Shortcut', exe), d);
d = fullfile(work, 'filesbad');
package('files.noname', @() compiler.package.installer({exe}, txt, 'OutputDir', d), d);
package('files.missing', @() compiler.package.installer({fullfile(src, 'nosuch.exe')}, txt, 'ApplicationName', 'k18m', 'OutputDir', d), d);
package('files.repeat', @() compiler.package.installer({exe, fullfile(src, 'k18hello.exe')}, txt, 'ApplicationName', 'k18r', 'OutputDir', d), d);
package('files.badtxt', @() compiler.package.installer(exe, fullfile(src, 'k18bad.txt'), 'ApplicationName', 'k18b', 'OutputDir', d, 'Shortcut', exe), d);
package('files.emptytxt', @() compiler.package.installer(exe, fullfile(src, 'k18empty.txt'), 'ApplicationName', 'k18e', 'OutputDir', d, 'Shortcut', exe), d);
package('files.notxt', @() compiler.package.installer(exe, fullfile(src, 'nosuch.txt'), 'ApplicationName', 'k18n', 'OutputDir', d, 'Shortcut', exe), d);
package('files.txtmissingarg', @() compiler.package.installer(exe), d);
package('files.cellnum', @() compiler.package.installer({5}, txt, 'ApplicationName', 'k18c', 'OutputDir', d), d);
package('files.empty', @() compiler.package.installer({}, txt, 'ApplicationName', 'k18c', 'OutputDir', d), d);
package('files.number', @() compiler.package.installer(5, txt, 'ApplicationName', 'k18c', 'OutputDir', d), d);
package('files.results.txt', @() compiler.package.installer(exe, r, 'ApplicationName', 'k18rt', 'OutputDir', d, 'Shortcut', exe), d);
package('shortcut.notpackaged', @() compiler.package.installer({exe}, txt, 'ApplicationName', 'k18s', 'OutputDir', d, 'Shortcut', fullfile(src, 'k18data.txt')), d);
package('shortcut.byname', @() compiler.package.installer({exe}, txt, 'ApplicationName', 'k18s', 'OutputDir', d, 'Shortcut', 'k18hello.exe'), d);
package('results.noshortcut', @() compiler.package.installer(r, 'OutputDir', d, 'Shortcut', ''), d);
package('results.exe.additional', @() compiler.package.installer(r, 'OutputDir', d, 'AdditionalFiles', fullfile(src, 'k18hello.exe')), d);
package('nargs', @() compiler.package.installer(r, 'OutputDir', d, many{:}), d);
package('nargs.odd', @() compiler.package.installer(r, 'OutputDir'), d);
package('unknown', @() compiler.package.installer(r, 'OutputDir', d, 'Bogus', 1), d);
d = fullfile(work, 'gone');
copyfile(fullfile(src, 'k18_icon.png'), fullfile(src, 'k18_gone.png'));
o = compiler.package.InstallerOptions(r, 'OutputDir', d, 'InstallerIcon', fullfile(src, 'k18_gone.png'));
delete(fullfile(src, 'k18_gone.png'));
package('icon.deleted', @() compiler.package.installer(r, 'Options', o), d);
copyfile(fullfile(src, 'k18_logo.png'), fullfile(src, 'k18_gone.png'));
o = compiler.package.InstallerOptions(r, 'OutputDir', d, 'AddRemoveProgramsIcon', fullfile(src, 'k18_gone.png'));
delete(fullfile(src, 'k18_gone.png'));
package('addremove.deleted', @() compiler.package.installer(r, 'Options', o), d);
d = fullfile(work, 'outfile');
package('outdir.file', @() compiler.package.installer(r, 'OutputDir', fullfile(src, 'k18data.txt')), d);
package('outdir.relative', @() compiler.package.installer(r, 'OutputDir', 'relout'), fullfile(src, 'relout'));
package('outdir.default', @() compiler.package.installer(r), fullfile(src, 'k18helloinstaller'));
package('outdir.default.named', @() compiler.package.installer(r, 'ApplicationName', 'Has Space'), fullfile(src, 'Has_Spaceinstaller'));
d = fullfile(work, 'verbosezip');
package('verbose.logical', @() compiler.package.installer(r, 'OutputDir', d, 'Verbose', true), d);

%% compiler.runtime.customInstaller.
d = fullfile(work, 'custom');
package('custom.web', @() compiler.runtime.customInstaller('k18rt', r, 'OutputDir', d), d);
package('custom.default.outdir', @() compiler.runtime.customInstaller('k18rt2', r), fullfile(src, 'k18rt2'));
package('custom.none', @() compiler.runtime.customInstaller('k18rt', r, 'OutputDir', d, 'RuntimeDelivery', 'none'), d);
package('custom.zipweb', @() compiler.runtime.customInstaller('k18rt', r, 'OutputDir', d, 'PackageType', 'zip'), d);
package('custom.installer', @() compiler.runtime.customInstaller('k18rt', r, 'OutputDir', d, 'RuntimeDelivery', 'installer'), d);
package('custom.zip.installer', @() compiler.runtime.customInstaller('k18rt', r, 'OutputDir', d, 'RuntimeDelivery', 'installer', 'PackageType', 'zip'), d);
package('custom.files', @() compiler.runtime.customInstaller('k18rt3', txt, 'OutputDir', d), d);
package('custom.files.two', @() compiler.runtime.customInstaller('k18rt4', {txt, fullfile(bw, 'requiredMCRProducts.txt')}, 'OutputDir', d), d);
package('custom.files.string', @() compiler.runtime.customInstaller("k18rt5", [string(txt), string(fullfile(bp, 'requiredMCRProducts.txt'))], 'OutputDir', d), d);
package('custom.multi', @() compiler.runtime.customInstaller('k18rt6', [r rw], 'OutputDir', d), d);
package('custom.badname.space', @() compiler.runtime.customInstaller('k18 rt', r, 'OutputDir', d), d);
package('custom.badname.slash', @() compiler.runtime.customInstaller('k18/rt', r, 'OutputDir', d), d);
package('custom.badname.empty', @() compiler.runtime.customInstaller('', r, 'OutputDir', d), d);
package('custom.badname.number', @() compiler.runtime.customInstaller(5, r, 'OutputDir', d), d);
package('custom.optional.none', @() compiler.runtime.customInstaller('k18rt7', r, 'OutputDir', d, 'OptionalDependencies', 'none'), d);
package('custom.optional.bad', @() compiler.runtime.customInstaller('k18rt', r, 'OutputDir', d, 'OptionalDependencies', 'some'), d);
package('custom.badtype', @() compiler.runtime.customInstaller('k18rt', 5, 'OutputDir', d), d);
package('custom.badtxt', @() compiler.runtime.customInstaller('k18rt', fullfile(src, 'k18bad.txt'), 'OutputDir', d), d);
package('custom.emptytxt', @() compiler.runtime.customInstaller('k18rt', fullfile(src, 'k18empty.txt'), 'OutputDir', d), d);
package('custom.notxt', @() compiler.runtime.customInstaller('k18rt', fullfile(src, 'nosuch.txt'), 'OutputDir', d), d);
package('custom.nargs', @() compiler.runtime.customInstaller('k18rt', r, 'OutputDir', d, 'PackageType', 'auto', 'RuntimeDelivery', 'web', 'Verbose', 'on'), d);
package('custom.unknown', @() compiler.runtime.customInstaller('k18rt', r, 'OutputDir', d, 'Bogus', 1), d);
package('custom.outdir.file', @() compiler.runtime.customInstaller('k18rt', r, 'OutputDir', fullfile(src, 'k18data.txt')), d);
package('custom.oneinput', @() compiler.runtime.customInstaller('k18rt'), d);

%% The runtime's functions and the helpers.
k0_try('mcrinstaller.printed', @() strtrim(evalc('mcrinstaller')));
[inst, ma, mi, up, pl] = mcrinstaller;
fprintf('mcrinstaller.outputs\t%s | %s | %s | %s | %s\n', describe(inst), describe(ma), describe(mi), describe(up), describe(pl));
k0_try('mcrinstaller.nargout', @() nargout('mcrinstaller'));
k0_try('runtime.version', @() compiler.runtime.version);
k0_try('runtime.version.class', @() class(compiler.runtime.version));
k0_try('runtime.version.two', @() nargout2(@compiler.runtime.version));
for name = {'compiler.runtime.download', 'compiler.runtime.version', 'compiler.runtime.installer', 'compiler.package.installer', ...
        'compiler.package.InstallerOptions', 'compiler.runtime.customInstaller', 'mcrinstaller'}
    k0_try(['nargin.' name{1}], @() nargin(name{1}));
    k0_try(['nargout.' name{1}], @() nargout(name{1}));
    k0_try(['exist.' name{1}], @() exist(name{1})); %#ok<EXIST>
end
k0_try('url', @() compiler.internal.runtime.utils.getInstallagentDowndloadURL());
k0_try('notes', @() compiler.internal.getDefaultInstallNotes());
k0_try('scrub', @() compiler.internal.utils.scrubName('a b:c/d\e<f>g"h*i|j?k&l'));
k0_try('ext', @() compiler.internal.package.installer.addInstallerExtension('x'));
k0_try('imgformats', @() strjoin(cellstr(compiler.internal.utils.CLIConstants.AllowedImageFormats), ' '));
k0_try('optionalproducts', @() strjoin(cellstr(enumeration('compiler.internal.utils.OptionalProductsValues')), ' '));
k0_try('deliveries', @() strjoin(cellstr(enumeration('compiler.internal.package.shared.RuntimeDeliveryValues')), ' '));
k0_try('packagetypes', @() strjoin(cellstr(enumeration('compiler.internal.package.shared.PackageTypes')), ' '));
k0_try('expectedMcrVersion', @() compiler.internal.runtime.utils.expectedMcrVersion);
k0_try('installerkey', @() compiler.internal.utils.CLIConstants.RuntimeInstallerKeyCurrentPlatform);
k0_try('runtime.version.text', @() evalc('compiler.runtime.version'));
end

function writeText(file, text)
fid = fopen(file, 'w');
fprintf(fid, '%s', text);
fclose(fid);
end

function nargout2(f)
[a, b] = f(); %#ok<ASGLU>
end

function v = setAndRead(o, prop, value)
o.(prop) = value;
v = o.(prop);
if ~ischar(v), v = strtrim(evalc('disp(v)')); end
end

function t = describe(v)
if ischar(v)
    if size(v, 1) > 1, t = sprintf('char %dx%d', size(v, 1), size(v, 2)); else, t = ['''' v '''']; end
elseif isstring(v)
    t = sprintf('string[%s]', strjoin(cellstr(v), ','));
elseif iscell(v)
    t = sprintf('cell{%s}', strjoin(cellfun(@describe, v, 'UniformOutput', false), ','));
elseif isempty(v)
    t = sprintf('%s empty', class(v));
elseif isnumeric(v) || islogical(v)
    t = sprintf('%s %s', class(v), mat2str(v));
else
    t = class(v);
end
end

function v = saveLoad(o)
f = [tempname '.mat'];
save(f, 'o');
s = load(f);
delete(f);
v = sprintf('%s | %s | %s | %s', class(s.o), s.o.ApplicationName, s.o.InstallerName, s.o.OutputDir);
end

function package(key, f, d)
% One packaging call: what it printed, its answer or refusal, its warning, and the folder's files.
lastwarn('', '');
t = tic;
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
listFiles(key, d);
fprintf('%s.seconds\t%.1f\n', key, toc(t));
end

function listFiles(key, d)
if ~exist(d, 'dir')
    fprintf('%s.files\t(no folder)\n', key);
    return
end
listing = dir(fullfile(d, '**', '*'));
listing = listing(~[listing.isdir]);
names = {};
for k = 1:numel(listing)
    rel = strrep(fullfile(listing(k).folder, listing(k).name), [d filesep], '');
    names{end + 1} = sprintf('%s (%d)', rel, listing(k).bytes); %#ok<AGROW>
end
fprintf('%s.files\t%s\n', key, strjoin(sort(names), ' | '));
end

function printFile(key, file)
if ~exist(file, 'file')
    fprintf('%s\tmissing\n', key);
    return
end
bytes = fileread(file);
fprintf('%s\t%d chars, CRLF %d, ends with newline %d\n', key, numel(bytes), count(bytes, sprintf('\r\n')), endsWith(bytes, newline));
printLines(bytes);
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

function show(key, v)
txt = evalc('disp(v)');
txt = regexprep(txt, '<a [^>]*>|</a>', '');
fprintf('%s\n', key);
lines = splitlines(txt);
for j = 1:numel(lines)
    if strlength(strtrim(lines{j})) > 0, fprintf('  %s\n', lines{j}); end
end
end
