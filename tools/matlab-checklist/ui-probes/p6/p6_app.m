function p6_app()
%P6_APP App Designer's rules for an app in a +package or @Class folder (packages and class folders
%plan, stage P6, decision PD4): PathUtil.getAppRunCommandFromFileName and getPathToApp over the
%paths its own comment lists and over .mlapp paths; then an .mlapp written through App Designer's
%serializer into +pkg (and a class text beside it as a plain .m app) run headless by its qualified
%name, which, exist, requiredFilesAndProducts of a caller, and the .mlapp as an mcc main.
%One key<TAB>value line per answer.
import appdesigner.internal.service.util.PathUtil
beep('off');
here = fileparts(mfilename('fullpath'));
root = fullfile(here, 'rt', 'app');
if exist(root, 'dir'), rmdir(root, 's'); end
mkdir(root);
work = fullfile(root, 'work');
mkdir(work);

paths = {'C:\work\+foo\@bar\bar.m', 'C:\work\+foo\+baz\@bar\bar.m', 'C:\work\+foo\+baz\@bar\bat.m', 'C:\work\+foo\baz.m', ...
    'C:\work\@bar\bar.m', 'C:\work\@bar\method.m', 'C:\work\@bar\blah\foo.m', 'C:\+work\blah\foo.m', 'C:\work\foo.m', ...
    'C:\work\+foo\bar.mlapp', 'C:\work\@foo\foo.mlapp', 'C:\work\+foo\+bar\@baz\baz.mlapp', 'C:\work\@foo\other.mlapp', ...
    'C:\work\+foo\private\bar.mlapp', 'C:\work\+foo\sub\bar.mlapp', 'C:\work\+\bar.mlapp', 'bar.mlapp', 'C:\work\+a+b\c.mlapp'};
for k = 1:numel(paths)
    try
        fprintf('cmd\t%s => %s\n', paths{k}, PathUtil.getAppRunCommandFromFileName(paths{k}));
    catch ME
        fprintf('cmd\t%s => ERR %s | %s\n', paths{k}, ME.identifier, flat(ME.message));
    end
    try
        fprintf('path\t%s => %s\n', paths{k}, PathUtil.getPathToApp(paths{k}));
    catch ME
        fprintf('path\t%s => ERR %s | %s\n', paths{k}, ME.identifier, flat(ME.message));
    end
end

% An app saved in a package: the class text App Designer writes (the file's stem is the class's
% name, with no package in it), as an .mlapp through the serializer and as a plain .m app.
pkgdir = fullfile(work, '+pkg');
mkdir(pkgdir);
text = apptext('papp');
w(fullfile(pkgdir, 'mapp.m'), strrep(text, 'papp', 'mapp'));
w(fullfile(work, 'usepapp.m'), sprintf('function usepapp()\na = pkg.papp;\ndelete(a);\nend\n'));
w(fullfile(work, 'usemapp.m'), sprintf('function usemapp()\na = pkg.mapp;\ndelete(a);\nend\n'));
try
    writeMlapp(text, fullfile(pkgdir, 'papp.mlapp'));
    fprintf('mlapp\tsaved\n');
catch ME
    fprintf('mlapp\tERR %s | %s\n', ME.identifier, flat(ME.message));
end
old = cd(work);
cleanup = onCleanup(@() cd(old));
for name = {'pkg.papp', 'pkg.mapp'}
    n = name{1};
    try
        a = eval(n);
        fprintf('run.%s\t%s valid=%d name=%s\n', n, class(a), isvalid(a), a.UIFigure.Name);
        delete(a);
    catch ME
        fprintf('run.%s\tERR %s | %s\n', n, ME.identifier, flat(ME.message));
    end
    r = which(n);
    fprintf('which.%s\t%s\n', n, rel(r, root));
    fprintf('exist.%s\t%d\n', n, exist(n));
    fprintf('existclass.%s\t%d\n', n, exist(n, 'class'));
end
try
    m = ?pkg.papp;
    fprintf('meta.papp\t%s super=%s\n', m.Name, strjoin({m.SuperclassList.Name}, ','));
catch ME
    fprintf('meta.papp\tERR %s | %s\n', ME.identifier, flat(ME.message));
end
for name = {'usepapp.m', 'usemapp.m', fullfile(pkgdir, 'papp.mlapp')}
    try
        f = matlab.codetools.requiredFilesAndProducts(name{1});
        fprintf('rfp.%s\t%s\n', name{1}, strjoin(cellfun(@(x) rel(x, root), sort(f), 'UniformOutput', false), ' ; '));
    catch ME
        fprintf('rfp.%s\tERR %s | %s\n', name{1}, ME.identifier, flat(ME.message));
    end
end
% mcc of the packaged .mlapp as a main (standalone), the archive's entries.
d = fullfile(root, 'build');
mkdir(d);
try
    evalc('mcc(''-m'', fullfile(pkgdir, ''papp.mlapp''), ''-C'', ''-d'', d)');
    fprintf('mcc.papp\tok\n');
catch ME
    fprintf('mcc.papp\tERR %s | %s\n', ME.identifier, flat(ME.message));
end
files = dir(d);
fprintf('mcc.papp.files\t%s\n', strjoin(sort({files(~[files.isdir]).name}), ' | '));
ctf = dir(fullfile(d, '*.ctf'));
if ~isempty(ctf)
    tmp = fullfile(root, 'unz');
    got = unzip(fullfile(d, ctf(1).name), tmp);
    got = sort(cellfun(@(g) strrep(g(numel(tmp) + 2:end), '\', '/'), got, 'UniformOutput', false));
    keep = got(startsWith(got, 'fsroot/') & ~startsWith(got, 'fsroot/.matlab/'));
    fprintf('mcc.papp.ctf\t%s\n', rel(strjoin(keep, ' | '), root));
    m = fileread(fullfile(tmp, '.META', 'manifest.xml'));
    tok = regexp(m, '<directory id="[^"]*" path="([^"$][^"]*)"', 'tokens');
    fprintf('mcc.papp.dirs\t%s\n', rel(strjoin([[tok{:}] {''}], ' | '), root));
    tok = regexp(m, '<function[^>]*>\s*<name>([^<]*)</name>', 'tokens');
    fprintf('mcc.papp.functions\t%s\n', strjoin([[tok{:}] {''}], ' | '));
    mainref = regexp(m, '<main-function ref="([^"]*)"', 'tokens', 'once');
    fprintf('mcc.papp.main\t%s\n', strjoin([mainref {''}], ''));
    rmdir(tmp, 's');
end
end

function text = apptext(name)
lines = {
    ['classdef ' name ' < matlab.apps.AppBase']
    ''
    '    % Properties that correspond to app components'
    '    properties (Access = public)'
    '        UIFigure     matlab.ui.Figure'
    '        CloseButton  matlab.ui.control.Button'
    '    end'
    ''
    '    % Callbacks that handle component events'
    '    methods (Access = private)'
    ''
    '        % Code that executes after component creation'
    '        function startupFcn(app)'
    '            app.UIFigure.Name = ''p6 app'';'
    '        end'
    ''
    '        % Button pushed function: CloseButton'
    '        function CloseButtonPushed(app, event)'
    '            delete(app);'
    '        end'
    '    end'
    ''
    '    % Component initialization'
    '    methods (Access = private)'
    ''
    '        % Create UIFigure and components'
    '        function createComponents(app)'
    ''
    '            % Create UIFigure and hide until all components are created'
    '            app.UIFigure = uifigure(''Visible'', ''off'');'
    '            app.UIFigure.Position = [100 100 260 120];'
    '            app.UIFigure.Name = ''MATLAB App'';'
    ''
    '            % Create CloseButton'
    '            app.CloseButton = uibutton(app.UIFigure, ''push'');'
    '            app.CloseButton.ButtonPushedFcn = createCallbackFcn(app, @CloseButtonPushed, true);'
    '            app.CloseButton.Position = [80 45 100 22];'
    '            app.CloseButton.Text = ''Close'';'
    ''
    '            % Show the figure after all components are created'
    '            app.UIFigure.Visible = ''on'';'
    '        end'
    '    end'
    ''
    '    % App creation and deletion'
    '    methods (Access = public)'
    ''
    '        % Construct app'
    ['        function app = ' name]
    ''
    '            % Create UIFigure and components'
    '            createComponents(app)'
    ''
    '            % Register the app with App Designer'
    '            registerApp(app, app.UIFigure)'
    ''
    '            % Execute the startup function'
    '            runStartupFcn(app, @startupFcn)'
    ''
    '            if nargout == 0'
    '                clear app'
    '            end'
    '        end'
    ''
    '        % Code that executes before app deletion'
    '        function delete(app)'
    ''
    '            % Delete UIFigure when app is deleted'
    '            delete(app.UIFigure)'
    '        end'
    '    end'
    'end'
    };
text = strjoin(lines', newline);
end

function writeMlapp(text, target)
% The app as App Designer saves it: the class text with its component tree, through the serializer
% App Designer's save goes through (as probe_k6_build made k6_app.mlapp).
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
fig = uifigure('Visible', 'off', 'Position', [100 100 260 120], 'Name', 'MATLAB App');
uibutton(fig, 'push', 'Text', 'Close', 'Position', [80 45 100 22]);
s = appdesigner.internal.serialization.MLAPPSerializer(target, fig);
s.OverwriteTargetFile = true;
s.MatlabCodeText = text;
s.EditableSectionCode = editable;
s.Callbacks = callbacks;
s.StartupCallback = startup;
s.InputParameters = '';
s.save();
delete(fig);
end

function w(file, text)
fid = fopen(file, 'w');
fprintf(fid, '%s', text);
fclose(fid);
end

function s = rel(s, root)
s = strrep(s, root, '<T>');
s = strrep(s, strrep(root, '\', '/'), '<T>');
s = strrep(s, matlabroot, '<MATLABROOT>');
end

function s = flat(s)
s = regexprep(char(s), '<a [^>]*>|</a>', '');
s = strrep(s, sprintf('\r'), '');
s = strrep(s, newline, ' / ');
end
