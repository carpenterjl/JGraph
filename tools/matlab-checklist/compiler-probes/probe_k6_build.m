function probe_k6_build(work)
%PROBE_K6_BUILD Builds stage K6's standalone programs (compiler and SDK plan) with mcc -m and -e and
%with compiler.build.standaloneApplication and standaloneWindowsApplication, and prints what each
%build wrote: its files, readme.txt, the splash image, the options and results displays, and the
%refusals. Run under matlab -batch -noFigureWindows; nothing built is run here (record-k6.ps1 runs
%the programs and reads their resources). One key<TAB>value line per question, a display or a
%file's text as indented lines under its key.
beep('off'); % the refusals below would each beep
here = fileparts(mfilename('fullpath'));
addpath(fullfile(here, 'src'));
if exist(work, 'dir'), rmdir(work, 's'); end
mkdir(work);
src = fullfile(work, 'src');
mkdir(src);
for f = {'k6_hello.m', 'k6_fig.m', 'k6_err.m', 'k6_wait.m', 'k6_app.m', 'k6_help.txt'}
    copyfile(fullfile(here, 'src', 'k6', f{1}), src);
end
old = cd(src);
cleanup = onCleanup(@() cd(old));

% The images: an icon and a splash of sizes R2025b must resize, and the app as an .mlapp.
[x, y] = meshgrid(linspace(0, 1, 64));
imwrite(uint8(255 * cat(3, x, y, 1 - x)), fullfile(src, 'k6_icon.png'));
[x, y] = meshgrid(linspace(0, 1, 200), linspace(0, 1, 100));
imwrite(uint8(255 * cat(3, y, x, 0.5 + 0 * x)), fullfile(src, 'k6_splash.png'));
writeMlapp(fullfile(src, 'k6_app.m'), fullfile(src, 'mlapp', 'k6_app.mlapp'));
icoSizes = [256 128 48 32 24 16];
icoParts = cell(1, numel(icoSizes));
for n = 1:numel(icoSizes)
    icoParts{n} = fullfile(work, sprintf('ico_%d.png', icoSizes(n)));
    imwrite(imresize(imread(fullfile(src, 'k6_icon.png')), [icoSizes(n) icoSizes(n)]), icoParts{n});
end
compiler.internal.makeico(fullfile(src, 'k6_icon.ico'), icoParts{:});
info = imfinfo(fullfile(matlabroot, 'toolbox', 'compiler', 'packagingResources', 'default_splash.png'));
fprintf('default_splash\t%dx%d %s %d bits\n', info.Width, info.Height, info.Format, info.BitDepth);
info = imfinfo(fullfile(matlabroot, 'toolbox', 'compiler', 'packagingResources', 'default_icon_48.png'));
fprintf('default_icon_48\t%dx%d %s %d bits\n', info.Width, info.Height, info.Format, info.BitDepth);

% mcc builds, each into a folder of its own.
builds = {
    'm_plain',       {'-m', 'k6_hello.m'}
    'e_plain',       {'-e', 'k6_hello.m'}
    'm_named',       {'-m', 'k6_hello.m', '-o', 'k6_named'}
    'm_version',     {'-W', 'main:k6_ver,version=2.3', '-T', 'link:exe', 'k6_hello.m'}
    'm_version4',    {'-W', 'main:k6_ver4,version=2.3.4.5', '-T', 'link:exe', 'k6_hello.m'}
    'e_winmain',     {'-W', 'WinMain:k6_wm,version=4.5.6.7', '-T', 'link:exe', 'k6_hello.m'}
    'm_icon_ico',    {'-m', 'k6_hello.m', '-r', fullfile(src, 'k6_icon.ico')}
    'm_icon_png',    {'-m', 'k6_hello.m', '-r', fullfile(src, 'k6_icon.png')}
    'm_icon_missing',{'-m', 'k6_hello.m', '-r', fullfile(src, 'k6_nosuch.ico')}
    'm_help',        {'-m', 'k6_hello.m', '-h', fullfile(src, 'k6_help.txt'), '-o', 'k6_helped'}
    'm_help_missing',{'-m', 'k6_hello.m', '-h', fullfile(src, 'k6_nosuch.txt')}
    'm_separate',    {'-m', 'k6_hello.m', '-C', '-o', 'k6_sep'}
    'm_fig',         {'-m', 'k6_fig.m'}
    'e_fig',         {'-e', 'k6_fig.m'}
    'm_err',         {'-m', 'k6_err.m'}
    'e_err',         {'-e', 'k6_err.m'}
    'e_wait',        {'-e', 'k6_wait.m'}
    'e_app_m',       {'-e', 'k6_app.m'}
    'e_app_mlapp',   {'-e', fullfile(src, 'mlapp', 'k6_app.mlapp')}
    'm_app_mlapp',   {'-m', fullfile(src, 'mlapp', 'k6_app.mlapp'), '-o', 'k6_app_console'}
    'o_spaced',      {'-m', 'k6_hello.m', '-o', 'k6 spaced'}
    'o_slash',       {'-m', 'k6_hello.m', '-o', 'k6/sub'}
    'o_ext',         {'-m', 'k6_hello.m', '-o', 'k6_ext.exe'}
    'm_and_W',       {'-m', '-W', 'main:k6_mw', 'k6_hello.m'}
    'e_and_W_main',  {'-e', '-W', 'main:k6_ew', 'k6_hello.m'}
    'winmain_lib',   {'-W', 'WinMain:k6_wl', '-T', 'link:lib', 'k6_hello.m'}
    };
for k = 1:size(builds, 1)
    [key, args] = builds{k, :};
    d = fullfile(work, key);
    mkdir(d);
    lastwarn('', '');
    try
        txt = quietMcc([args, {'-d', d}]);
        fprintf('mcc.%s\tok (%s)\n', key, k0_flat(strtrim(txt)));
        listFiles(['mcc.' key], d);
    catch ME
        fprintf('mcc.%s\tERR %s | %s\n', key, ME.identifier, k0_flat(ME.message));
    end
    k0_warned(['mcc.' key]);
end
k0_try('mcc.d.missing', @() quietMcc({'-m', 'k6_hello.m', '-d', fullfile(work, 'no', 'such')}));
for key = {'m_plain', 'e_plain', 'm_help'}
    printFile(['readme.' key{1}], fullfile(work, key{1}, 'readme.txt'));
end

% compiler.build: a console program with an icon, a version and help, built verbosely.
d = fullfile(work, 'api_m');
o = compiler.build.StandaloneApplicationOptions('k6_hello.m', 'OutputDir', d, 'ExecutableName', 'k6_api', ...
    'ExecutableIcon', fullfile(src, 'k6_icon.png'), 'ExecutableVersion', '3.4', ...
    'CustomHelpTextFile', fullfile(src, 'k6_help.txt'), 'Verbose', 'on');
show('api_m.options', o);
apiBuild('api_m', @() compiler.build.standaloneApplication(o), d);
printFile('readme.api_m', fullfile(d, 'readme.txt'));

% Windowed programs: a splash of another size, and the default splash.
d = fullfile(work, 'api_e');
o = compiler.build.StandaloneApplicationOptions('k6_wait.m', 'OutputDir', d, ...
    'ExecutableSplashScreen', fullfile(src, 'k6_splash.png'), 'Verbose', 'on');
apiBuild('api_e', @() compiler.build.standaloneWindowsApplication(o), d);
d = fullfile(work, 'api_e_default');
apiBuild('api_e_default', @() compiler.build.standaloneWindowsApplication('k6_hello.m', 'OutputDir', d), d);
printFile('readme.api_e_default', fullfile(d, 'readme.txt'));
d = fullfile(work, 'api_e_app');
apiBuild('api_e_app', @() compiler.build.standaloneWindowsApplication(fullfile(src, 'mlapp', 'k6_app.mlapp'), 'OutputDir', d), d);
d = fullfile(work, 'api_m_separate');
apiBuild('api_m_separate', @() compiler.build.standaloneApplication('k6_hello.m', 'OutputDir', d, 'EmbedArchive', 'off'), d);
d = fullfile(work, 'api_m_numeric');
apiBuild('api_m_numeric', @() compiler.build.standaloneApplication('k6_hello.m', 'OutputDir', d, 'TreatInputsAsNumeric', 'on', 'Verbose', 'on'), d);

% Images compiler.build refuses, and where.
k0_do('api.icon.ico.set', @() setIcon(fullfile(src, 'k6_icon.ico')));
apiBuild('api_icon_ico', @() compiler.build.standaloneApplication('k6_hello.m', 'OutputDir', fullfile(work, 'api_icon_ico'), ...
    'ExecutableIcon', fullfile(src, 'k6_icon.ico')), fullfile(work, 'api_icon_ico'));
apiBuild('api_splash_txt', @() compiler.build.standaloneWindowsApplication('k6_hello.m', 'OutputDir', fullfile(work, 'api_splash_txt'), ...
    'ExecutableSplashScreen', fullfile(src, 'k6_help.txt')), fullfile(work, 'api_splash_txt'));
k0_try('api.wrong.options', @() compiler.build.standaloneApplication(compiler.build.DotNETAssemblyOptions('k6_hello.m')));
k0_try('api.two.options', @() compiler.build.standaloneApplication(o, o));
k0_try('api.no.args', @() compiler.build.standaloneApplication());
k0_try('api.nargout2', @() twoOut());
k0_try('api.mlx', @() compiler.build.StandaloneApplicationOptions(fullfile(src, 'k6_help.txt')));
o = compiler.build.StandaloneApplicationOptions('k6_hello.m');
k0_try('opts.ExecutableName.default', @() o.ExecutableName);
k0_try('opts.OutputDir.default', @() o.OutputDir);
o2 = compiler.build.StandaloneApplicationOptions(fullfile(src, 'mlapp', 'k6_app.mlapp'));
k0_try('opts.mlapp.ExecutableName', @() o2.ExecutableName);
o3 = compiler.build.StandaloneApplicationOptions('k6_hello.m', 'ExecutableName', 'k6 two words');
k0_try('opts.spaced.OutputDir', @() o3.OutputDir);
end

function setIcon(file)
o = compiler.build.StandaloneApplicationOptions('k6_hello.m');
o.ExecutableIcon = file;
end

function twoOut()
[a, b] = compiler.build.standaloneApplication('k6_hello.m'); %#ok<ASGLU>
end

function apiBuild(key, f, d)
lastwarn('', '');
try
    txt = evalc('r = f();');
    if ~isempty(strtrim(txt))
        fprintf('%s.printed\n', key);
        printLines(txt);
    end
    show([key '.results'], r);
    fprintf('%s.BuildType\t%s\n', key, r.BuildType);
    files = cellfun(@(p) strrep(p, [d filesep], '<OUT>\'), r.Files, 'UniformOutput', false);
    fprintf('%s.Files\t%s\n', key, strjoin(files(:)', ' | '));
    listFiles(key, d);
    splash = fullfile(d, 'splash.png');
    if exist(splash, 'file')
        info = imfinfo(splash);
        fprintf('%s.splash\t%dx%d %s %d bits %s\n', key, info.Width, info.Height, info.Format, info.BitDepth, info.ColorType);
    end
catch ME
    fprintf('%s\tERR %s | %s\n', key, ME.identifier, k0_flat(ME.message));
end
k0_warned(key);
end

function listFiles(key, d)
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

function txt = quietMcc(args)
% evalc in a named function: a lambda's workspace holds only what its body names.
txt = evalc('mcc(args{:})');
end

function writeMlapp(classFile, target)
% The app as App Designer saves it: the class text with its component tree, through the serializer
% App Designer's save goes through (as the U7b fixtures were made).
text = strrep(fileread(classFile), sprintf('\r\n'), newline);
if text(end) == newline, text(end) = []; end
lines = splitlines(text)';
iPropsEnd = find(strcmp(strtrim(lines), 'end'), 1);
iCb = find(strcmp(strtrim(lines), '% Callbacks that handle component events'));
iInit = find(strcmp(strtrim(lines), '% Component initialization'));
editable = lines(iPropsEnd + 2 : iCb - 2);
callbacks = struct('Name', {}, 'Code', {});
startup = [];
for i = iCb : iInit
    tok = regexp(lines{i}, '^\s*function (\w+)\(app', 'tokens', 'once');
    if isempty(tok), continue; end
    indent = regexp(lines{i}, '^\s*', 'match', 'once');
    last = i + find(strcmp(lines(i+1:end), [indent 'end']), 1);
    body = lines(i + 1 : last - 1);
    if contains(lines{i - 1}, 'Code that executes after component creation')
        startup = struct('Name', tok{1}, 'Code', {body});
    else
        callbacks(end + 1) = struct('Name', tok{1}, 'Code', {body}); %#ok<AGROW>
    end
end
fig = uifigure('Visible', 'off', 'Position', [100 100 260 120], 'Name', 'k6 app');
uibutton(fig, 'push', 'Text', 'Close', 'Position', [80 45 100 22]);
folder = fileparts(target);
if ~exist(folder, 'dir'), mkdir(folder); end
s = appdesigner.internal.serialization.MLAPPSerializer(target, fig);
s.OverwriteTargetFile = true;
s.MatlabCodeText = text;
s.EditableSectionCode = editable;
s.Callbacks = callbacks;
s.StartupCallback = startup;
s.InputParameters = '';
try
    s.save();
    fprintf('mlapp\tsaved\n');
catch e
    fprintf('mlapp\tERR %s | %s\n', e.identifier, k0_flat(e.message));
end
delete(fig);
end
