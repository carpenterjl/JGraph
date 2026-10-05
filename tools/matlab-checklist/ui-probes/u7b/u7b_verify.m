% U7b: what R2025b makes of .mlapp files JGraph saved an edit into. The files are in rt/edited
% (ignored), written by the JGraph test AppDesignerU7bTests with JGRAPH_U7B_OUT naming that folder:
% one folder per shipped app, holding the app with a comment line put first in a callback, in the
% startup function and in the editable section (edits.txt says which it has), and sample/ holding
% this project's own U7bApp with real edits. Each is read the ways App Designer and MATLAB read an
% app: readAppCodeData, the runnable text, readAppDesignerData and App Designer's own full load.
root = fullfile(pwd, 'rt', 'edited');
d = dir(root); d = d([d.isdir] & ~startsWith({d.name}, '.') & ~strcmp({d.name}, 'sample'));
nOk = 0; nFiles = 0; bad = {};
for k = 1:numel(d)
    folder = fullfile(root, d(k).name);
    f = dir(fullfile(folder, '*.mlapp'));
    if isempty(f), continue; end
    nFiles = nFiles + 1;
    fn = fullfile(folder, f(1).name);
    edits = strtrim(splitlines(strtrim(fileread(fullfile(folder, 'edits.txt')))));
    why = {};
    try
        r = appdesigner.internal.serialization.FileReader(fn);
        c = r.readAppCodeData();
        txt = char(matlab.internal.getCode(fn));
        for e = 1:numel(edits)
            parts = strsplit(edits{e}, ' ');
            switch parts{1}
                case 'callback'
                    j = find(strcmp({c.Callbacks.Name}, parts{2}));
                    if numel(j) ~= 1 || ~strcmp(strtrim(c.Callbacks(j).Code{1}), '% jgraph-edited-callback'), why{end+1} = 'callback edit missing from the copy'; end %#ok<AGROW>
                    if ~contains(txt, '% jgraph-edited-callback'), why{end+1} = 'callback edit missing from the text'; end %#ok<AGROW>
                case 'startup'
                    if ~strcmp(c.StartupCallback.Name, parts{2}) || ~strcmp(strtrim(c.StartupCallback.Code{1}), '% jgraph-edited-startup'), why{end+1} = 'startup edit missing from the copy'; end %#ok<AGROW>
                    if ~contains(txt, '% jgraph-edited-startup'), why{end+1} = 'startup edit missing from the text'; end %#ok<AGROW>
                case 'section'
                    if ~any(strcmp(strtrim(c.EditableSectionCode), '% jgraph-edited-section')), why{end+1} = 'section edit missing from the copy'; end %#ok<AGROW>
                    if ~contains(txt, '% jgraph-edited-section'), why{end+1} = 'section edit missing from the text'; end %#ok<AGROW>
            end
        end
        if isempty(edits) || isempty(edits{1}), why{end+1} = 'no edits listed'; end
        a = r.readAppDesignerData();
        if ~all(isfield(a, {'components', 'code', 'appData'})), why{end+1} = 'readAppDesignerData lacks a field'; end
        if ~isa(a.components.UIFigure, 'matlab.ui.Figure'), why{end+1} = 'component tree is not a figure'; end
        delete(a.components.UIFigure);
        des = appdesigner.internal.serialization.DeserializerFactory.createDeserializer(fn);
        full = des.getAppData();
        if ~isstruct(full), why{end+1} = 'getAppData is not a struct'; end
        try, delete(full.components.UIFigure); catch, end
        m = r.readAppMetadata();
        if ~isstruct(m), why{end+1} = 'metadata not read'; end
    catch err
        why{end+1} = sprintf('ERROR %s', err.identifier); %#ok<AGROW>
    end
    if isempty(why), nOk = nOk + 1; else, bad{end+1} = sprintf('%s: %s', f(1).name, strjoin(why, '; ')); end %#ok<SAGROW>
end
fprintf('%d of %d edited shipped apps read by R2025b with every edit in the copy and the text\n', nOk, nFiles);
fprintf('%s\n', bad{:});

% --- this project's own app, with real edits: read, and run -------------------------------------
fn = fullfile(root, 'sample', 'U7bApp.mlapp');
r = appdesigner.internal.serialization.FileReader(fn);
c = r.readAppCodeData();
fprintf('sample fields: %s\n', strjoin(fieldnames(c)', ','));
fprintf('sample callback first line: %s\n', strtrim(c.Callbacks(1).Code{1}));
fprintf('sample callback lines: %d %d\n', numel(c.Callbacks(1).Code), numel(c.Callbacks(2).Code));
fprintf('sample section has the property: %d (%d lines)\n', any(contains(c.EditableSectionCode, 'Extra = 7')), numel(c.EditableSectionCode));
fprintf('sample startup first line: %s\n', strtrim(c.StartupCallback.Code{1}));
fprintf('sample inputs: %s\n', c.InputParameters);
fprintf('sample code value classes: %s %s %s %s\n', class(c.EditableSectionCode), class(c.Callbacks), class(c.Callbacks(1).Code), class(c.StartupCallback.Code));
fprintf('sample code value sizes: %s %s %s\n', mat2str(size(c.EditableSectionCode)), mat2str(size(c.Callbacks)), mat2str(size(c.Callbacks(1).Code)));
des = appdesigner.internal.serialization.DeserializerFactory.createDeserializer(fn);
full = des.getAppData();
fprintf('sample full load: %s, %d component(s) under the figure; code fields %s\n', class(full), numel(full.components.UIFigure.Children), strjoin(fieldnames(full.code)', ','));
fprintf('sample full load has the copy''s callbacks, section, startup: %d %d %d\n', isequal(full.code.Callbacks, c.Callbacks), isequal(full.code.EditableSectionCode, c.EditableSectionCode), isequal(full.code.StartupCallback, c.StartupCallback));
fprintf('sample full load callbacks: fields %s; names and code as the copy: %d %d\n', strjoin(fieldnames(full.code.Callbacks)', ','), isequal({full.code.Callbacks.Name}, {c.Callbacks.Name}), isequal({full.code.Callbacks.Code}, {c.Callbacks.Code}));
% The same comparison on the file as R2025b's serializer wrote it, before any edit (u7b_build).
orig = fullfile(pwd, 'rt', 'U7bApp.mlapp');
oc = appdesigner.internal.serialization.FileReader(orig).readAppCodeData();
of = appdesigner.internal.serialization.DeserializerFactory.createDeserializer(orig).getAppData();
fprintf('unedited full load has the copy''s callbacks, section, startup: %d %d %d\n', isequal(of.code.Callbacks, oc.Callbacks), isequal(of.code.EditableSectionCode, oc.EditableSectionCode), isequal(of.code.StartupCallback, oc.StartupCallback));
delete(of.components.UIFigure);
line = c.EditableSectionCode{find(contains(c.EditableSectionCode, 'Extra = 7'), 1)};
fprintf('sample section line ends with code points: %s\n', mat2str(double(line(end-6:end))));
txt = char(matlab.internal.getCode(fn));
fprintf('sample text holds the same line: %d\n', contains(txt, [line newline]));
delete(full.components.UIFigure);
addpath(fullfile(root, 'sample'));
fprintf('which: %s\n', erase(which('U7bApp'), [root filesep]));
app = U7bApp(5);
fprintf('runs: %s; amp %g; extra %g\n', strjoin(app.Log, ','), app.AmpField.Value, app.Extra);
app.GoButton.ButtonPushedFcn(app.GoButton, []);
fprintf('after the button: %s\n', strjoin(app.Log, ','));
delete(app);
