% Open item 57: builds the fixture app U7bApp73.mlapp with R2025b's own serializer, headless, as
% u7b_build does for U7bApp.mlapp, but with MATLAB's MAT-file save format set to version 7.3 so
% that appdesigner/appModel.mat comes out as HDF5 - the form one shipped app
% (serdes/SignalIntegrityLink.mlapp) keeps its model in, and the form any app saved under that
% preference keeps it in. The result goes to rt/ (ignored); it is copied by hand into
% tests/JGraph.Tests/MatlabParity/fixtures/helpers when it changes.
if ~exist('rt', 'dir'), mkdir('rt'); end
text = strrep(fileread(fullfile('..', 'u7b', 'U7bApp.txt')), sprintf('\r\n'), newline);
if text(end) == newline, text(end) = []; end
text = strrep(strrep(text, 'U7bApp', 'U7bApp73'), 'U7b App', 'U7b App 73');
lines = splitlines(text)';

% The design-time copy, cut from the text by the anchors App Designer writes (as u7b_build cuts it).
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

fig = uifigure('Visible', 'off', 'Position', [100 100 320 160], 'Name', 'U7b App 73');
g = uigridlayout(fig, 'ColumnWidth', {'1x', '1x'}, 'RowHeight', {22, 22});
amp = uieditfield(g, 'numeric', 'Tag', 'amp', 'Value', 2);
amp.Layout.Row = 1; amp.Layout.Column = [1 2];
go = uibutton(g, 'push', 'Tag', 'go', 'Text', 'Go');
go.Layout.Row = 2; go.Layout.Column = 1;
clr = uibutton(g, 'push', 'Tag', 'clear', 'Text', 'Clear');
clr.Layout.Row = 2; clr.Layout.Column = 2;

% The preference App Designer's save follows for its model.
s = settings;
s.matlab.general.matfile.SaveFormat.TemporaryValue = 'v7.3';
fprintf('save format: %s\n', s.matlab.general.matfile.SaveFormat.ActiveValue);

fn = fullfile(pwd, 'rt', 'U7bApp73.mlapp');
if exist(fn, 'file'), delete(fn); end
ser = appdesigner.internal.serialization.MLAPPSerializer(fn, fig);
ser.OverwriteTargetFile = true;
ser.MatlabCodeText = text;
ser.EditableSectionCode = editable;
ser.Callbacks = callbacks;
ser.StartupCallback = startup;
ser.InputParameters = inputs;
try
    ser.save();
    fprintf('saved %s\n', 'rt/U7bApp73.mlapp');
catch e
    fprintf('SAVE ERROR %s | %s\n', e.identifier, e.message);
    for k = 1:numel(e.stack), fprintf('   at %s:%d\n', e.stack(k).name, e.stack(k).line); end
    return
end
delete(fig);

% What was written: the model's header word, its HDF5 shape, and the three ways it is read back.
parts = fullfile(pwd, 'rt', 'U7bApp73_parts');
if exist(parts, 'dir'), rmdir(parts, 's'); end
z = unzip(fn, parts);
for k = 1:numel(z), d = dir(z{k}); fprintf('part %s %d bytes\n', strrep(erase(z{k}, [parts filesep]), '\', '/'), d.bytes); end
model = fullfile(parts, 'appdesigner', 'appModel.mat');
fid = fopen(model, 'r'); head = fread(fid, [1 19], 'uint8=>char'); fclose(fid);
fprintf('model header: %s\n', head);
if startsWith(head, 'MATLAB 7.3')
    info = h5info(model);
    for gg = info.Groups', fprintf('G %s\n', gg.Name); end
    for dd = info.Datasets', fprintf('D %s dims=%s\n', dd.Name, mat2str(dd.Dataspace.Size)); end
    c = h5info(model, '/code');
    for a = c.Attributes', if strcmp(a.Name, 'MATLAB_fields'), fprintf('code fields: %s\n', strjoin(a.Value', ',')); end, end
    for dd = c.Datasets', fprintf('D /code/%s dims=%s type=%s\n', dd.Name, mat2str(dd.Dataspace.Size), dd.Datatype.Class); end
    for gg = c.Groups', fprintf('G %s\n', gg.Name); for dd = gg.Datasets', fprintf('   D %s dims=%s type=%s\n', dd.Name, mat2str(dd.Dataspace.Size), dd.Datatype.Class); end, end
    r = h5info(model, '/#refs#'); fprintf('refs: %d datasets, %d groups\n', numel(r.Datasets), numel(r.Groups));
end
w = whos('-file', model);
for k = 1:numel(w), fprintf('mat variable %s : %s %s\n', w(k).name, w(k).class, mat2str(w(k).size)); end
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
    delete(a.components.UIFigure);
catch e
    fprintf('deserializer ERROR %s | %s\n', e.identifier, e.message);
end
addpath(fullfile(pwd, 'rt'));
app = U7bApp73(5);
fprintf('runs: %s; amp %g\n', strjoin(app.Log, ','), app.AmpField.Value);
delete(app);
