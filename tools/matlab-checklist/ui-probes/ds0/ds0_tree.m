% DS0 (designer plan): what a component tree is as MAT-file data, so the designer's writer can be
% sized. Saves (uncompressed, version 7) the U7b app's tree as App Designer holds it - with each
% component's DesignTimeProperties - and the same tree without them, and the tree of one shipped app
% as App Designer's full load returns it; ds0_tree_dump.py then lists each subsystem object's class
% and the property names it saved. rt/ (ignored) holds the files.
if ~exist('rt', 'dir'), mkdir('rt'); end
out = fullfile(pwd, 'rt', 'tree');
if ~exist(out, 'dir'), mkdir(out); end

for withDtp = [true false]
    fig = uifigure('Visible', 'off', 'Position', [100 100 320 160], 'Name', 'U7b App');
    g = uigridlayout(fig, 'ColumnWidth', {'1x', '1x'}, 'RowHeight', {22, 22});
    amp = uieditfield(g, 'numeric', 'Value', 2);
    amp.Layout.Row = 1; amp.Layout.Column = [1 2];
    go = uibutton(g, 'push', 'Text', 'Go', 'ButtonPushedFcn', 'GoButtonPushed');
    go.Layout.Row = 2; go.Layout.Column = 1;
    comps = {fig, 'UIFigure'; g, 'GridLayout'; amp, 'AmpField'; go, 'GoButton'};
    if withDtp
        for c = 1:size(comps, 1)
            addprop(comps{c, 1}, 'DesignTimeProperties');
            comps{c, 1}.DesignTimeProperties = struct('CodeName', comps{c, 2}, 'GroupId', '', 'ComponentCode', {{}});
        end
        name = 'u7b_dtp.mat';
    else
        name = 'u7b_plain.mat';
    end
    components = struct('UIFigure', fig); %#ok<NASGU>
    save(fullfile(out, name), 'components', '-v7', '-nocompression');
    d = dir(fullfile(out, name));
    fprintf('%s: %d bytes\n', name, d.bytes);
    % a lone figure, and a lone button in it, for the smallest cases
    if ~withDtp
        f2 = uifigure('Visible', 'off'); components = struct('UIFigure', f2); %#ok<NASGU>
        save(fullfile(out, 'empty_fig.mat'), 'components', '-v7', '-nocompression');
        uibutton(f2); save(fullfile(out, 'one_button.mat'), 'components', '-v7', '-nocompression');
        delete(f2);
    end
    delete(fig);
end

% a shipped app's tree, as App Designer's full load returns it
files = dir(fullfile(matlabroot, 'toolbox', '**', 'DataExport.mlapp'));
des = appdesigner.internal.serialization.DeserializerFactory.createDeserializer(fullfile(files(1).folder, files(1).name));
a = des.getAppData();
components = a.components; %#ok<NASGU>
save(fullfile(out, 'shipped_dataexport.mat'), 'components', '-v7', '-nocompression');
fprintf('shipped_dataexport.mat: %d components\n', numel(findall(a.components.UIFigure)));
delete(a.components.UIFigure);

% what loading the saved trees gives back
for n = {'u7b_dtp.mat', 'u7b_plain.mat', 'one_button.mat'}
    S = load(fullfile(out, n{1}));
    f = S.components.UIFigure;
    fprintf('%s reloads: %s, %d objects, %d with DesignTimeProperties\n', n{1}, class(f), numel(findall(f)), numel(findall(f, '-property', 'DesignTimeProperties')));
    delete(f);
end
