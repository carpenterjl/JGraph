% DS2 (designer plan): what a component's shape allows, for the canvas's resize. For each class
% App Designer offers (ds1_catalog.json), made as ds1b_fresh makes it: its default Position, its
% hidden AspectRatioLimits and IsSizeFixed where it has them, and the Position it keeps after a
% set to [10 10 200 50] - whether the runtime itself holds a shape. Headless.
catalog = jsondecode(fileread(fullfile(pwd, '..', 'ds1', 'ds1_catalog.json')));
parents = struct('Tab', 'uitabgroup(fig)', 'TreeNode', 'uitree(fig)', 'PushTool', 'uitoolbar(fig)', ...
    'ToggleTool', 'uitoolbar(fig)', 'RadioButton', 'uibuttongroup(fig)', 'ToggleButton', 'uibuttongroup(fig)', ...
    'Menu', 'fig', 'ContextMenu', 'fig');
rows = {};
for k = 1:numel(catalog.classes)
    c = catalog.classes(k);
    if iscell(catalog.classes), c = catalog.classes{k}; end
    short = c.type(find(c.type == '.', 1, 'last') + 1:end);
    if strcmp(short, 'Figure'), continue; end
    fig = uifigure('Visible', 'off');
    if isfield(parents, short), p = eval(parents.(short)); else, p = fig; end %#ok<NASGU>
    obj = eval(strrep(c.creation, '%PARENT%', 'p'));
    row = struct('type', c.type, 'position', [], 'aspect', [], 'fixed', [], 'after', []);
    if isprop(obj, 'Position'), row.position = obj.Position; end
    try, row.aspect = obj.AspectRatioLimits; catch, end
    try, row.fixed = obj.IsSizeFixed; catch, end
    if isprop(obj, 'Position') && ~isa(obj, 'matlab.ui.container.Tab')
        try
            obj.Position = [10 10 200 50];
            drawnow;
            row.after = obj.Position;
        catch e
            row.after = e.identifier;
        end
    end
    rows{end + 1} = row; %#ok<SAGROW>
    delete(fig);
end
fid = fopen(fullfile(pwd, 'ds2_sizes.json'), 'w');
fwrite(fid, jsonencode(rows, 'PrettyPrint', true));
fclose(fid);
fprintf('%d classes\n', numel(rows));
for k = 1:numel(rows)
    r = rows{k};
    fprintf('%-45s pos=%s aspect=%s fixed=%s after=%s\n', r.type, mat2str(r.position), mat2str(r.aspect), ...
        mat2str(r.fixed), mat2str(r.after));
end
