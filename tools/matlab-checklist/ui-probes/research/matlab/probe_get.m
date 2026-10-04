function probe_get(outdir)
%PROBE_GET instantiate invisible components and dump get()
getdir = fullfile(outdir, 'get');
if ~exist(getdir, 'dir'), mkdir(getdir); end
set(groot, 'DefaultFigureVisible', 'off');
log = fopen(fullfile(outdir, 'get_log.txt'), 'w');
cleaner = onCleanup(@() fclose(log));

% ---------- uifigure side
uf = [];
try
    uf = uifigure('Visible', 'off');
    fprintf(log, 'uifigure created OK, class %s\n', class(uf));
    dumpGet(uf, fullfile(getdir, 'uifigure.txt'), log);
catch e
    fprintf(log, 'uifigure FAILED: %s | %s\n', e.identifier, e.message);
end

if ~isempty(uf)
    specs = {
        'uibutton_push',        @() uibutton(uf)
        'uibutton_state',       @() uibutton(uf, 'state')
        'uilabel',              @() uilabel(uf)
        'uieditfield_text',     @() uieditfield(uf)
        'uieditfield_numeric',  @() uieditfield(uf, 'numeric')
        'uitextarea',           @() uitextarea(uf)
        'uidropdown',           @() uidropdown(uf)
        'uilistbox',            @() uilistbox(uf)
        'uicheckbox',           @() uicheckbox(uf)
        'uislider',             @() uislider(uf)
        'uislider_range',       @() uislider(uf, 'range')
        'uispinner',            @() uispinner(uf)
        'uiknob_continuous',    @() uiknob(uf)
        'uiknob_discrete',      @() uiknob(uf, 'discrete')
        'uiswitch_slider',      @() uiswitch(uf)
        'uiswitch_rocker',      @() uiswitch(uf, 'rocker')
        'uiswitch_toggle',      @() uiswitch(uf, 'toggle')
        'uigauge_circular',     @() uigauge(uf)
        'uigauge_linear',       @() uigauge(uf, 'linear')
        'uigauge_ninetydegree', @() uigauge(uf, 'ninetydegree')
        'uigauge_semicircular', @() uigauge(uf, 'semicircular')
        'uilamp',               @() uilamp(uf)
        'uidatepicker',         @() uidatepicker(uf)
        'uicolorpicker',        @() uicolorpicker(uf)
        'uihyperlink',          @() uihyperlink(uf)
        'uiimage',              @() uiimage(uf)
        'uihtml',               @() uihtml(uf)
        'uitree',               @() uitree(uf)
        'uitree_checkbox',      @() uitree(uf, 'checkbox')
        'uitable_uifigure',     @() uitable(uf)
        'uiaxes',               @() uiaxes(uf)
        'uipanel_uifigure',     @() uipanel(uf)
        'uitabgroup_uifigure',  @() uitabgroup(uf)
        'uigridlayout',         @() uigridlayout(uf)
        'uibuttongroup_uifigure', @() uibuttongroup(uf)
        'uimenu_uifigure',      @() uimenu(uf)
        'uicontextmenu_uifigure', @() uicontextmenu(uf)
        'uitoolbar_uifigure',   @() uitoolbar(uf)
        'uicontrol_in_uifigure', @() uicontrol(uf)
        };
    for i = 1:size(specs, 1)
        try
            c = specs{i, 2}();
            fprintf(log, '%s -> %s\n', specs{i, 1}, class(c));
            dumpGet(c, fullfile(getdir, [specs{i, 1} '.txt']), log);
            delete(c);
        catch e
            fprintf(log, '%s FAILED: %s | %s\n', specs{i, 1}, e.identifier, e.message);
        end
    end
    % children that need a specific parent
    try
        tg = uitabgroup(uf); t = uitab(tg);
        fprintf(log, 'uitab_uifigure -> %s\n', class(t));
        dumpGet(t, fullfile(getdir, 'uitab_uifigure.txt'), log); delete(tg);
    catch e, fprintf(log, 'uitab FAILED: %s | %s\n', e.identifier, e.message); end
    try
        bg = uibuttongroup(uf); r = uiradiobutton(bg); tb = uitogglebutton(bg);
        fprintf(log, 'uiradiobutton -> %s ; uitogglebutton -> %s\n', class(r), class(tb));
        dumpGet(r, fullfile(getdir, 'uiradiobutton.txt'), log);
        dumpGet(tb, fullfile(getdir, 'uitogglebutton.txt'), log);
        fprintf(log, 'buttongroup SelectedObject after radio+toggle: %s Text=%s\n', class(bg.SelectedObject), bg.SelectedObject.Text);
        delete(bg);
    catch e, fprintf(log, 'radio/toggle FAILED: %s | %s\n', e.identifier, e.message); end
    try
        tr = uitree(uf); n = uitreenode(tr);
        fprintf(log, 'uitreenode -> %s\n', class(n));
        dumpGet(n, fullfile(getdir, 'uitreenode.txt'), log); delete(tr);
    catch e, fprintf(log, 'uitreenode FAILED: %s | %s\n', e.identifier, e.message); end
    try
        tb = uitoolbar(uf); pt = uipushtool(tb); tt = uitoggletool(tb);
        dumpGet(pt, fullfile(getdir, 'uipushtool_uifigure.txt'), log);
        dumpGet(tt, fullfile(getdir, 'uitoggletool_uifigure.txt'), log); delete(tb);
    catch e, fprintf(log, 'uifigure toolbar tools FAILED: %s | %s\n', e.identifier, e.message); end
    try
        s = uistyle;
        fprintf(log, 'uistyle -> %s\n', class(s));
        dumpGet(s, fullfile(getdir, 'uistyle.txt'), log);
    catch e, fprintf(log, 'uistyle FAILED: %s | %s\n', e.identifier, e.message); end
    delete(uf);
end

% ---------- legacy figure side
try
    f = figure('Visible', 'off');
    fprintf(log, 'figure -> %s\n', class(f));
    dumpGet(f, fullfile(getdir, 'figure.txt'), log);
    styles = {'pushbutton', 'togglebutton', 'checkbox', 'radiobutton', 'edit', 'text', ...
        'slider', 'frame', 'listbox', 'popupmenu'};
    for i = 1:numel(styles)
        try
            c = uicontrol(f, 'Style', styles{i});
            dumpGet(c, fullfile(getdir, ['uicontrol_' styles{i} '.txt']), log);
            delete(c);
        catch e
            fprintf(log, 'uicontrol %s FAILED: %s | %s\n', styles{i}, e.identifier, e.message);
        end
    end
    try
        c = uicontrol(f, 'Style', 'bogus');
        delete(c);
    catch e
        fprintf(log, 'uicontrol Style bogus: %s | %s\n', e.identifier, e.message);
    end
    legacy = {
        'uipanel',       @() uipanel(f)
        'uibuttongroup', @() uibuttongroup(f)
        'uitable',       @() uitable(f)
        'uitabgroup',    @() uitabgroup(f)
        'uimenu',        @() uimenu(f)
        'uicontextmenu', @() uicontextmenu(f)
        'uitoolbar',     @() uitoolbar(f)
        'axes',          @() axes(f)
        'uibutton_in_figure', @() uibutton(f)
        'uigridlayout_in_figure', @() uigridlayout(f)
        };
    for i = 1:size(legacy, 1)
        try
            c = legacy{i, 2}();
            fprintf(log, '%s -> %s\n', legacy{i, 1}, class(c));
            dumpGet(c, fullfile(getdir, [legacy{i, 1} '.txt']), log);
            delete(c);
        catch e
            fprintf(log, '%s FAILED: %s | %s\n', legacy{i, 1}, e.identifier, e.message);
        end
    end
    try
        tg = uitabgroup(f); t = uitab(tg); dumpGet(t, fullfile(getdir, 'uitab.txt'), log); delete(tg);
        tb = uitoolbar(f); pt = uipushtool(tb); tt = uitoggletool(tb);
        dumpGet(pt, fullfile(getdir, 'uipushtool.txt'), log);
        dumpGet(tt, fullfile(getdir, 'uitoggletool.txt'), log); delete(tb);
    catch e, fprintf(log, 'legacy tab/tools FAILED: %s | %s\n', e.identifier, e.message); end
    delete(f);
catch e
    fprintf(log, 'figure side FAILED: %s | %s\n', e.identifier, e.message);
end
try
    r = groot;
    dumpGet(r, fullfile(getdir, 'groot.txt'), log);
catch e, fprintf(log, 'groot FAILED %s\n', e.message); end
end

function dumpGet(h, fname, log)
try
    s = get(h);
catch e
    fprintf(log, '  get(%s) failed: %s\n', class(h), e.message);
    return;
end
fid = fopen(fname, 'w');
fprintf(fid, 'CLASS %s\n', class(h));
fn = fieldnames(s);
for k = 1:numel(fn)
    fprintf(fid, '%s = %s\n', fn{k}, v2s(s.(fn{k})));
end
fclose(fid);
end
