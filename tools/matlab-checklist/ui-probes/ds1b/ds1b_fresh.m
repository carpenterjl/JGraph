% DS1b (designer plan): one fresh component of each class App Designer offers, made by the creation
% call App Designer generates (ds1_catalog.json) in the parent it needs, each figure saved
% (uncompressed, version 7) to rt/fresh/<Class>__ref.mat for ds1b_blank.py. Headless.
out = fullfile(pwd, 'rt', 'fresh'); if ~exist(out, 'dir'), mkdir(out); end
catalog = jsondecode(fileread(fullfile(pwd, '..', 'ds1', 'ds1_catalog.json')));
parents = struct('Tab', 'uitabgroup(fig)', 'TreeNode', 'uitree(fig)', 'PushTool', 'uitoolbar(fig)', ...
    'ToggleTool', 'uitoolbar(fig)', 'RadioButton', 'uibuttongroup(fig)', 'ToggleButton', 'uibuttongroup(fig)');
for k = 1:numel(catalog.classes)
    c = catalog.classes(k);
    if iscell(catalog.classes), c = catalog.classes{k}; end
    short = c.type(find(c.type == '.', 1, 'last') + 1:end);
    if strcmp(short, 'Figure')
        fig = eval(c.creation);
    else
        fig = uifigure('Visible', 'off');
        if isfield(parents, short), p = eval(parents.(short)); else, p = fig; end %#ok<NASGU>
        eval([strrep(c.creation, '%PARENT%', 'p') ';']);
    end
    components = struct('UIFigure', fig); %#ok<NASGU>
    save(fullfile(out, [short '__ref.mat']), 'components', '-v7', '-nocompression');
    delete(fig);
end
fprintf('%d classes saved\n', numel(catalog.classes));

% The parts of an axes App Designer's label properties set (TitleString and the rest): an axes
% given a title, subtitle and three axis labels, as App Designer's lines give them.
fig = uifigure('Visible', 'off');
ax = uiaxes(fig);
title(ax, 'Title'); subtitle(ax, 'Subtitle'); xlabel(ax, 'X'); ylabel(ax, 'Y'); zlabel(ax, 'Z');
% ToolbarVisible 'off': App Designer gives the axes a toolbar of its own, which a tree saves
% (DesignTimeUIAxesController); the default toolbar is not saved.
ax.Toolbar = axtoolbar(ax, 'default', 'Visible', 'off');
components = struct('UIFigure', fig); %#ok<NASGU>
save(fullfile(out, 'UIAxesParts.mat'), 'components', '-v7', '-nocompression');
delete(fig);
