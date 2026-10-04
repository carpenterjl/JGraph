function probe_meta(outdir)
%PROBE_META package listing + metaclass dumps (no instantiation)
metadir = fullfile(outdir, 'meta');
if ~exist(metadir, 'dir'), mkdir(metadir); end

pkgs = {'matlab.ui', 'matlab.ui.control', 'matlab.ui.container', ...
    'matlab.ui.container.toolbar', 'matlab.ui.container.internal', ...
    'matlab.ui.eventdata', 'matlab.ui.dialog', 'matlab.ui.style', ...
    'matlab.ui.componentcontainer', 'matlab.ui.componentcontainer.mixin', ...
    'matlab.apps', 'matlab.ui.controls', 'matlab.ui.control.internal', ...
    'matlab.ui.eventdata.internal', 'matlab.ui.internal', 'matlab.graphics.controls'};
fid = fopen(fullfile(outdir, 'packages.txt'), 'w');
allClasses = {};
for i = 1:numel(pkgs)
    p = meta.package.fromName(pkgs{i});
    if isempty(p)
        fprintf(fid, '== %s : <no such package>\n', pkgs{i});
        continue;
    end
    names = sort(arrayfun(@(c) c.Name, p.ClassList, 'UniformOutput', false));
    fprintf(fid, '== %s : %d classes\n', pkgs{i}, numel(names));
    fprintf(fid, '   %s\n', names{:});
    subs = sort(arrayfun(@(c) c.Name, p.PackageList, 'UniformOutput', false));
    if ~isempty(subs), fprintf(fid, '   [subpackages] %s\n', strjoin(subs, ' ')); end
    fns = sort(arrayfun(@(c) c.Name, p.FunctionList, 'UniformOutput', false));
    if ~isempty(fns), fprintf(fid, '   [functions] %s\n', strjoin(fns, ' ')); end
    if ~contains(pkgs{i}, 'internal')
        allClasses = [allClasses; names(:)]; %#ok<AGROW>
    end
end
fclose(fid);

extra = {'matlab.ui.Figure', 'matlab.ui.Root', 'matlab.ui.control.UIControl', ...
    'matlab.ui.control.UIAxes', 'matlab.graphics.axis.Axes', ...
    'matlab.ui.componentcontainer.ComponentContainer', 'matlab.apps.AppBase', ...
    'matlab.ui.dialog.ProgressDialog', 'matlab.ui.style.Style', ...
    'matlab.ui.eventdata.ActionData', 'matlab.ui.eventdata.CellEditData', ...
    'matlab.ui.eventdata.CellSelectionChangeData', 'matlab.ui.eventdata.SelectionChangedData', ...
    'matlab.ui.eventdata.KeyData', 'matlab.ui.eventdata.ScrollWheelData', ...
    'matlab.ui.eventdata.WindowMouseData', 'matlab.ui.eventdata.SizeChangedData', ...
    'matlab.ui.eventdata.UIClientComponentKeyEvent', 'matlab.ui.eventdata.MouseData', ...
    'matlab.ui.eventdata.ValueChangedData', 'matlab.ui.eventdata.ValueChangingData', ...
    'matlab.ui.eventdata.ButtonPushedData', 'matlab.ui.eventdata.ClickedData', ...
    'matlab.ui.eventdata.CloseRequestData', 'matlab.ui.eventdata.ContextMenuOpeningData'};
allClasses = unique([allClasses; extra(:)]);

sfid = fopen(fullfile(outdir, 'meta_summary.txt'), 'w');
for i = 1:numel(allClasses)
    cn = allClasses{i};
    mc = meta.class.fromName(cn);
    if isempty(mc)
        fprintf(sfid, '%s : <no metaclass>\n', cn);
        continue;
    end
    dumpClass(mc, fullfile(metadir, [cn '.txt']), sfid);
end
fclose(sfid);
end

function dumpClass(mc, fname, sfid)
fid = fopen(fname, 'w');
fprintf(fid, 'CLASS %s\n', mc.Name);
fprintf(fid, 'Superclasses: %s\n', strjoin(arrayfun(@(c) c.Name, mc.SuperclassList, 'UniformOutput', false)', ', '));
fprintf(fid, 'Sealed=%d Abstract=%d Hidden=%d HandleCompatible=%d\n', mc.Sealed, mc.Abstract, mc.Hidden, mc.HandleCompatible);
props = mc.PropertyList;
[~, ix] = sort({props.Name}); props = props(ix);
pub = {}; hid = {}; cbs = {};
fprintf(fid, '\n-- PUBLIC PROPERTIES (GetAccess public, not Hidden)\n');
fprintf(fid, 'Name | SetAccess | Dependent | Default(meta) | DefiningClass\n');
for k = 1:numel(props)
    p = props(k);
    ga = p.GetAccess; if ~ischar(ga), ga = 'restricted'; end
    sa = p.SetAccess; if ~ischar(sa), sa = 'restricted'; end
    if ~strcmp(ga, 'public'), continue; end
    if p.Hidden
        hid{end+1} = p.Name; %#ok<AGROW>
        continue;
    end
    pub{end+1} = p.Name; %#ok<AGROW>
    if endsWith(p.Name, 'Fcn') || endsWith(p.Name, 'Callback')
        cbs{end+1} = p.Name; %#ok<AGROW>
    end
    if p.HasDefault, d = v2s(p.DefaultValue); else, d = '-'; end
    fprintf(fid, '%s | %s | %d | %s | %s\n', p.Name, sa, p.Dependent, d, p.DefiningClass.Name);
end
fprintf(fid, '\n-- HIDDEN PUBLIC-GET PROPERTIES (%d)\n%s\n', numel(hid), strjoin(hid, ' '));
ev = mc.EventList;
evn = {};
for k = 1:numel(ev)
    la = ev(k).ListenAccess; if ~ischar(la), la = 'restricted'; end
    if strcmp(la, 'public') && ~ev(k).Hidden, evn{end+1} = ev(k).Name; end %#ok<AGROW>
end
evn = sort(evn);
fprintf(fid, '\n-- PUBLIC EVENTS (%d)\n%s\n', numel(evn), strjoin(evn, ' '));
ms = mc.MethodList;
mn = {};
for k = 1:numel(ms)
    a = ms(k).Access; if ~ischar(a), a = 'restricted'; end
    if strcmp(a, 'public') && ~ms(k).Hidden
        dc = ms(k).DefiningClass.Name;
        if any(strcmp(dc, {'handle', 'matlab.mixin.SetGet', 'matlab.mixin.SetGetExactNames', ...
                'matlab.mixin.CustomDisplay', 'matlab.mixin.Heterogeneous', 'JavaVisible', ...
                'matlab.mixin.internal.CompactDisplay', 'matlab.mixin.CustomCompactDisplayProvider'}))
            continue;
        end
        mn{end+1} = sprintf('%s(%s)', ms(k).Name, dc); %#ok<AGROW>
    end
end
mn = unique(mn);
fprintf(fid, '\n-- PUBLIC METHODS excluding handle/SetGet/display mixins (%d)\n%s\n', numel(mn), strjoin(mn, '\n'));
fclose(fid);
fprintf(sfid, '%s : %d public props, %d hidden; callbacks: %s; events: %s\n', mc.Name, numel(pub), numel(hid), strjoin(cbs, ' '), strjoin(evn, ' '));
end
