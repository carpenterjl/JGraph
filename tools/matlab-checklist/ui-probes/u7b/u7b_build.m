% U7b: builds the fixture app U7bApp.mlapp with R2025b's own serializer, headless, from the class
% text in U7bApp.txt. The file it writes is what App Designer's save writes: the code in
% matlab/document.xml and, in appdesigner/appModel.mat, the component tree (MCOS objects), the
% design-time copy of the code and the legacy AppData object. The result goes to rt/ (ignored);
% it is copied by hand into tests/JGraph.Tests/MatlabParity/fixtures/helpers when it changes.
if ~exist('rt', 'dir'), mkdir('rt'); end
text = strrep(fileread('U7bApp.txt'), sprintf('\r\n'), newline);
if text(end) == newline, text(end) = []; end
lines = splitlines(text)';

% The design-time copy, cut from the text by the anchors App Designer writes.
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
        inputs = regexp(lines{i}, '(?<=\(app, ).*(?=\)$)', 'match', 'once');
    else
        callbacks(end + 1) = struct('Name', tok{1}, 'Code', {body}); %#ok<SAGROW>
    end
end

% The component tree the code makes, as App Designer holds it.
fig = uifigure('Visible', 'off', 'Position', [100 100 320 160], 'Name', 'U7b App');
g = uigridlayout(fig, 'ColumnWidth', {'1x', '1x'}, 'RowHeight', {22, 22});
amp = uieditfield(g, 'numeric', 'Tag', 'amp', 'Value', 2);
amp.Layout.Row = 1; amp.Layout.Column = [1 2];
go = uibutton(g, 'push', 'Tag', 'go', 'Text', 'Go');
go.Layout.Row = 2; go.Layout.Column = 1;
clr = uibutton(g, 'push', 'Tag', 'clear', 'Text', 'Clear');
clr.Layout.Row = 2; clr.Layout.Column = 2;

fn = fullfile(pwd, 'rt', 'U7bApp.mlapp');
if exist(fn, 'file'), delete(fn); end
s = appdesigner.internal.serialization.MLAPPSerializer(fn, fig);
s.OverwriteTargetFile = true;
s.MatlabCodeText = text;
s.EditableSectionCode = editable;
s.Callbacks = callbacks;
s.StartupCallback = startup;
s.InputParameters = inputs;
try
    s.save();
    fprintf('saved %s\n', 'rt/U7bApp.mlapp');
catch e
    fprintf('SAVE ERROR %s | %s\n', e.identifier, e.message);
    for k = 1:numel(e.stack), fprintf('   at %s:%d\n', e.stack(k).name, e.stack(k).line); end
    return
end
delete(fig);

% What was written, read back the three ways App Designer and MATLAB read it.
r = appdesigner.internal.serialization.FileReader(fn);
c = r.readAppCodeData();
fprintf('code fields: %s\n', strjoin(fieldnames(c)', ','));
fprintf('callbacks: %s\n', strjoin({c.Callbacks.Name}, ','));
fprintf('editable lines %d, startup lines %d, inputs "%s"\n', numel(c.EditableSectionCode), numel(c.StartupCallback.Code), c.InputParameters);
fprintf('text round trip: %d\n', strcmp(char(matlab.internal.getCode(fn)), text));
try
    des = appdesigner.internal.serialization.DeserializerFactory.createDeserializer(fn);
    a = des.getAppData();
    fprintf('getAppData ok: %s; figure children %d\n', class(a), numel(a.components.UIFigure.Children));
catch e
    fprintf('deserializer ERROR %s | %s\n', e.identifier, e.message);
end
z = unzip(fn, fullfile(pwd, 'rt', 'U7bApp_parts'));
for k = 1:numel(z), d = dir(z{k}); fprintf('part %s %d bytes\n', strrep(erase(z{k}, [fullfile(pwd, 'rt', 'U7bApp_parts') filesep]), '\', '/'), d.bytes); end
w = whos('-file', fullfile(pwd, 'rt', 'U7bApp_parts', 'appdesigner', 'appModel.mat'));
for k = 1:numel(w), fprintf('mat variable %s : %s\n', w(k).name, w(k).class); end
addpath(fullfile(pwd, 'rt'));
app = U7bApp(5);
fprintf('runs: %s; amp %g\n', strjoin(app.Log, ','), app.AmpField.Value);
delete(app);
