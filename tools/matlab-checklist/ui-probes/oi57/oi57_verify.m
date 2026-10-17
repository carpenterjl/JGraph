% Open items 57 and 58: what R2025b makes of (57) an .mlapp whose version 7.3 model JGraph spliced
% an edit into, and (58) an .mlapp JGraph made from a class that never was one.
%
% The edited apps are in rt/edited (ignored), written by the JGraph tests AppDesignerU7bTests and
% AppDesignerV73ModelTests with JGRAPH_U7B_OUT naming that folder: one numbered folder per shipped
% app (the one with a version 7.3 model is found by name), and sample73/ holding this project's own
% U7bApp73 with real edits. Each is read every way App Designer and MATLAB read an app, its model
% is loaded whole (the MCOS component tree included) and inspected as HDF5, and the sample is run.
root = fullfile(pwd, 'rt', 'edited');
helpers = fullfile(pwd, '..', '..', '..', '..', 'tests', 'JGraph.Tests', 'MatlabParity', 'fixtures', 'helpers');

% --- the shipped app with a version 7.3 model, edited by JGraph ----------------------------------
d = dir(fullfile(root, '*', 'SignalIntegrityLink.mlapp'));
if isempty(d)
    fprintf('shipped: no edited SignalIntegrityLink.mlapp under rt/edited\n');
else
    folder = d(1).folder;
    fn = fullfile(folder, d(1).name);
    edits = strtrim(splitlines(strtrim(fileread(fullfile(folder, 'edits.txt')))));
    check73('shipped', fn, edits);
end

% --- this project's own app with a version 7.3 model, with real edits ---------------------------------
fn = fullfile(root, 'sample73', 'U7bApp73.mlapp');
check73('sample73', fn, {});
r = appdesigner.internal.serialization.FileReader(fn);
c = r.readAppCodeData();
fprintf('sample73 callback first line: %s\n', strtrim(c.Callbacks(1).Code{1}));
fprintf('sample73 callback lines: %d %d; classes %s %s\n', numel(c.Callbacks(1).Code), numel(c.Callbacks(2).Code), class(c.Callbacks(1).Code), class(c.Callbacks(2).Code));
fprintf('sample73 section has the property: %d (%d lines)\n', any(contains(c.EditableSectionCode, 'Extra = 7')), numel(c.EditableSectionCode));
line = c.EditableSectionCode{find(contains(c.EditableSectionCode, 'Extra = 7'), 1)};
fprintf('sample73 section line ends with code points: %s\n', mat2str(double(line(end-6:end))));
fprintf('sample73 startup first line: %s\n', strtrim(c.StartupCallback.Code{1}));
fprintf('sample73 inputs: %s\n', c.InputParameters);
txt = char(matlab.internal.getCode(fn));
fprintf('sample73 text holds the same line: %d\n', contains(txt, [line newline]));
addpath(fullfile(root, 'sample73'));
fprintf('which: %s\n', erase(which('U7bApp73'), [root filesep]));
app = U7bApp73(5);
fprintf('runs: %s; amp %g; extra %g\n', strjoin(app.Log, ','), app.AmpField.Value, app.Extra);
app.GoButton.ButtonPushedFcn(app.GoButton, []);
fprintf('after the button: %s\n', strjoin(app.Log, ','));
delete(app);

% --- item 58: a package of code alone, as App Designer and MATLAB see it ------------------------------
fn = fullfile(helpers, 'U7MlApp.mlapp');
fprintf('code-only exist: %d; which: %s\n', exist(fn, 'file'), erase(which('U7MlApp'), [helpers filesep]));
try
    r = appdesigner.internal.serialization.FileReader(fn);
    try, c = r.readAppCodeData(); fprintf('code-only readAppCodeData: %s\n', class(c)); catch e, fprintf('code-only readAppCodeData ERROR %s | %s\n', e.identifier, strrep(e.message, newline, ' ')); end
    try, a = r.readAppDesignerData(); fprintf('code-only readAppDesignerData: %s\n', class(a)); catch e, fprintf('code-only readAppDesignerData ERROR %s | %s\n', e.identifier, strrep(e.message, newline, ' ')); end
    try, m = r.readAppMetadata(); fprintf('code-only readAppMetadata: %s\n', class(m)); catch e, fprintf('code-only readAppMetadata ERROR %s | %s\n', e.identifier, strrep(e.message, newline, ' ')); end
catch e
    fprintf('code-only FileReader ERROR %s | %s\n', e.identifier, strrep(e.message, newline, ' '));
end
try
    des = appdesigner.internal.serialization.DeserializerFactory.createDeserializer(fn);
    full = des.getAppData();
    fprintf('code-only getAppData: %s\n', class(full));
catch e
    fprintf('code-only deserializer ERROR %s | %s\n', e.identifier, strrep(e.message, newline, ' '));
end
try
    fprintf('code-only getCode first line: %s\n', extractBefore(char(matlab.internal.getCode(fn)), newline));
catch e
    fprintf('code-only getCode ERROR %s | %s\n', e.identifier, strrep(e.message, newline, ' '));
end
addpath(helpers);
try
    global vlog_text %#ok<GVMIS>
    vlog_text = '';
    app = U7MlApp(3, 'ramp');
    fprintf('code-only runs: %s amp %g\n', class(app), app.AmpField.Value);
    delete(app);
catch e
    fprintf('code-only run ERROR %s | %s\n', e.identifier, strrep(e.message, newline, ' '));
end

function check73(label, fn, edits)
    % Reads one app with a version 7.3 model the ways App Designer and MATLAB read it, and its model as a MAT-file and as HDF5.
    why = {};
    parts = fullfile(tempname, 'parts');
    unzip(fn, parts);
    model = fullfile(parts, 'appdesigner', 'appModel.mat');
    fid = fopen(model, 'r'); head = fread(fid, [1 19], 'uint8=>char'); fseek(fid, 0, 'eof'); bytes = ftell(fid); fseek(fid, 512 + 40, 'bof'); eof = fread(fid, 1, 'uint64'); fclose(fid);
    fprintf('%s model: %s; %d bytes, stored eof %d\n', label, head, bytes, eof);
    try
        w = whos('-file', model);
        fprintf('%s whos: %s\n', label, strjoin(arrayfun(@(v) sprintf('%s(%s)', v.name, v.class), w, 'UniformOutput', false)', ' '));
        S = load(model);
        fprintf('%s load: %s; code fields %s\n', label, strjoin(fieldnames(S)', ','), strjoin(fieldnames(S.code)', ','));
        fprintf('%s load classes: components.UIFigure %s, appData %s\n', label, class(S.components.UIFigure), class(S.appData));
        try, delete(S.components.UIFigure); catch, end
        info = h5info(model);
        fprintf('%s h5 root: %s | %s\n', label, strjoin({info.Groups.Name}, ','), strjoin({info.Datasets.Name}, ','));
        c = h5info(model, '/code');
        fa = c.Attributes(strcmp({c.Attributes.Name}, 'MATLAB_fields'));
        fprintf('%s h5 code fields: %s; datasets %s; groups %s\n', label, strjoin(fa.Value', ','), strjoin({c.Datasets.Name}, ','), strjoin(arrayfun(@(g) g.Name, c.Groups, 'UniformOutput', false)', ','));
    catch e
        why{end+1} = sprintf('MAT/HDF5 ERROR %s | %s', e.identifier, strrep(e.message, newline, ' ')); %#ok<AGROW>
    end
    try
        r = appdesigner.internal.serialization.FileReader(fn);
        c = r.readAppCodeData();
        txt = char(matlab.internal.getCode(fn));
        for e = 1:numel(edits)
            p = strsplit(edits{e}, ' ');
            switch p{1}
                case 'callback'
                    j = find(strcmp({c.Callbacks.Name}, p{2}));
                    if numel(j) ~= 1 || ~strcmp(strtrim(c.Callbacks(j).Code{1}), '% jgraph-edited-callback'), why{end+1} = 'callback edit missing from the copy'; end %#ok<AGROW>
                    if ~contains(txt, '% jgraph-edited-callback'), why{end+1} = 'callback edit missing from the text'; end %#ok<AGROW>
                case 'startup'
                    if ~strcmp(c.StartupCallback.Name, p{2}) || ~strcmp(strtrim(c.StartupCallback.Code{1}), '% jgraph-edited-startup'), why{end+1} = 'startup edit missing from the copy'; end %#ok<AGROW>
                    if ~contains(txt, '% jgraph-edited-startup'), why{end+1} = 'startup edit missing from the text'; end %#ok<AGROW>
                case 'section'
                    if ~any(strcmp(strtrim(c.EditableSectionCode), '% jgraph-edited-section')), why{end+1} = 'section edit missing from the copy'; end %#ok<AGROW>
                    if ~contains(txt, '% jgraph-edited-section'), why{end+1} = 'section edit missing from the text'; end %#ok<AGROW>
            end
        end
        fprintf('%s readAppCodeData fields: %s; callbacks %d; section lines %d\n', label, strjoin(fieldnames(c)', ','), numel(c.Callbacks), numel(c.EditableSectionCode));
        a = r.readAppDesignerData();
        if ~all(isfield(a, {'components', 'code', 'appData'})), why{end+1} = 'readAppDesignerData lacks a field'; end
        if ~isa(a.components.UIFigure, 'matlab.ui.Figure'), why{end+1} = 'component tree is not a figure'; end
        fprintf('%s readAppDesignerData: %s; figure children %d; code as the copy: %d\n', label, strjoin(fieldnames(a)', ','), numel(a.components.UIFigure.Children), isequal(a.code, c));
        delete(a.components.UIFigure);
        des = appdesigner.internal.serialization.DeserializerFactory.createDeserializer(fn);
        full = des.getAppData();
        if ~isstruct(full), why{end+1} = 'getAppData is not a struct'; end
        fprintf('%s getAppData: %s; callbacks as the copy: %d\n', label, class(full), isequal({full.code.Callbacks.Name}, {c.Callbacks.Name}));
        try, delete(full.components.UIFigure); catch, end
        m = r.readAppMetadata();
        if ~isstruct(m), why{end+1} = 'metadata not read'; end
    catch err
        why{end+1} = sprintf('ERROR %s | %s', err.identifier, strrep(err.message, newline, ' ')); %#ok<AGROW>
    end
    if isempty(why), fprintf('%s: OK, read by R2025b every way with every edit in place\n', label); else, fprintf('%s: %s\n', label, strjoin(why, '; ')); end
    rmdir(fileparts(parts), 's');
end
