function probe_extra(outdir)
set(groot, 'DefaultFigureVisible', 'off');
getdir = fullfile(outdir, 'get');
fid = fopen(fullfile(outdir, 'extra.txt'), 'w');
cleaner = onCleanup(@() fclose(fid));
uf = uifigure('Visible', 'off');
bg = uibuttongroup(uf); r1 = uiradiobutton(bg); r2 = uiradiobutton(bg);
fprintf(fid, 'radio group: r1.Value=%d r2.Value=%d SelectedObject is r1: %d\n', r1.Value, r2.Value, isequal(bg.SelectedObject, r1));
r2.Value = true;
fprintf(fid, 'after r2.Value=true: r1.Value=%d SelectedObject is r2: %d\n', r1.Value, isequal(bg.SelectedObject, r2));
dumpGet(r1, fullfile(getdir, 'uiradiobutton.txt'));
bg2 = uibuttongroup(uf); t1 = uitogglebutton(bg2);
dumpGet(t1, fullfile(getdir, 'uitogglebutton.txt'));
try, uiradiobutton(bg2); catch e, fprintf(fid, 'radio into toggle group: %s | %s\n', e.identifier, strrep(e.message, newline, ' / ')); end
try, r1.Value = false; fprintf(fid, 'r1.Value=false on selected radio -> r1.Value=%d r2.Value=%d\n', r1.Value, r2.Value); catch e, fprintf(fid, 'r1.Value=false: %s | %s\n', e.identifier, e.message); end
s = uistyle;
pf = fopen(fullfile(getdir, 'uistyle.txt'), 'w');
fprintf(pf, 'CLASS %s\n', class(s));
pn = properties(s);
for k = 1:numel(pn), fprintf(pf, '%s = %s\n', pn{k}, v2s(s.(pn{k}))); end
fclose(pf);
% legacy uicontrol radio in buttongroup
f = figure('Visible', 'off');
lbg = uibuttongroup(f);
a = uicontrol(lbg, 'Style', 'radiobutton', 'String', 'A');
b = uicontrol(lbg, 'Style', 'radiobutton', 'String', 'B', 'Position', [10 40 80 20]);
fprintf(fid, 'legacy buttongroup: SelectedObject String=%s a.Value=%g b.Value=%g\n', lbg.SelectedObject.String, a.Value, b.Value);
% tab group
tg = uitabgroup(uf); t1 = uitab(tg, 'Title', 'One'); t2 = uitab(tg, 'Title', 'Two');
fprintf(fid, 'tabgroup SelectedTab Title=%s TabLocation=%s\n', tg.SelectedTab.Title, tg.TabLocation);
% tree
tr = uitree(uf); n1 = uitreenode(tr, 'Text', 'a'); n2 = uitreenode(n1, 'Text', 'b');
fprintf(fid, 'tree SelectedNodes empty=%d Multiselect=%s; methods(Tree): %s\n', isempty(tr.SelectedNodes), char(tr.Multiselect), strjoin(methods(tr)', ' '));
fprintf(fid, 'methods(TreeNode): %s\n', strjoin(methods(n1)', ' '));
ct = uitree(uf, 'checkbox'); c1 = uitreenode(ct, 'Text', 'x');
ct.CheckedNodes = c1; fprintf(fid, 'checkbox tree CheckedNodes set ok, numel=%d\n', numel(ct.CheckedNodes));
% methods of common components
cls = {uibutton(uf), uitable(uf), uilistbox(uf), uidropdown(uf), uihtml(uf), uiaxes(uf), uf};
for k = 1:numel(cls)
    m = methods(cls{k});
    fprintf(fid, 'methods(%s): %s\n', class(cls{k}), strjoin(m', ' '));
end
% table interaction props
tb = uitable(uf, 'Data', magic(3));
fprintf(fid, 'uitable(uifigure) SelectionType=%s Multiselect=%s RowStriping=%s ColumnSortable=%s ColumnFormat=%s\n', ...
    tb.SelectionType, char(tb.Multiselect), char(tb.RowStriping), v2s(tb.ColumnSortable), v2s(tb.ColumnFormat));
lt = uitable(f, 'Data', magic(3));
fprintf(fid, 'uitable(figure) Units=%s Position=%s ColumnName=%s\n', lt.Units, mat2str(lt.Position), v2s(lt.ColumnName));
% html
h = uihtml(uf);
fprintf(fid, 'uihtml methods: %s\n', strjoin(methods(h)', ' '));
% AppBase
fprintf(fid, 'AppBase methods: %s\n', strjoin(methods('matlab.apps.AppBase')', ' '));
mc = meta.class.fromName('matlab.apps.AppBase');
fprintf(fid, 'AppBase methods (all access): %s\n', strjoin(unique({mc.MethodList.Name}), ' '));
mc = meta.class.fromName('matlab.ui.componentcontainer.ComponentContainer');
ms = mc.MethodList;
out = {};
for k = 1:numel(ms)
    a = ms(k).Access; if ~ischar(a), a = 'restricted'; end
    out{end+1} = sprintf('%s[%s%s]', ms(k).Name, a, repmat(',abstract', 1, ms(k).Abstract)); %#ok<AGROW>
end
fprintf(fid, 'ComponentContainer methods: %s\n', strjoin(unique(out), ' '));
% appdesigner availability in batch (do not open)
fprintf(fid, 'matlab.ui.internal.hasDisplay=%s isDesktopAvailable=%s\n', v2s(matlab.ui.internal.hasDisplay), v2s(matlab.ui.internal.isDesktopAvailable));
% javacomponent / actxcontrol behaviour (call into invisible figure)
fprintf(fid, 'feature(webui)=%s\n', v2s(feature('webui')));
ids = {{'MATLAB:ui:javacomponent:BridgeForWebFigures'}, {'MATLAB:ui:javacomponent:FunctionToBeRemoved'}, ...
    {'MATLAB:actxcontrol:FunctionHasBeenRemoved', 'ACTXCONTROL'}, {'MATLAB:guide:GUIDEHasBeenRemoved', '', '', ''}, ...
    {'MATLAB:ui:uifigure:UnsupportedAppDesignerFunctionality', 'uifigure'}};
for k = 1:numel(ids)
    try, fprintf(fid, 'msg %s: %s\n', ids{k}{1}, strrep(getString(message(ids{k}{:})), newline, ' / ')); catch e, fprintf(fid, 'msg %s: <%s>\n', ids{k}{1}, e.message); end
end
try, guide; catch e, fprintf(fid, 'guide: %s | %s\n', e.identifier, strrep(e.message, newline, ' / ')); end
fprintf(fid, 'figure JavaFrame isprop: %d\n', isprop(f, 'JavaFrame'));
delete(f); delete(uf);
end

function dumpGet(h, fname)
s = get(h);
fid = fopen(fname, 'w');
fprintf(fid, 'CLASS %s\n', class(h));
fn = fieldnames(s);
for k = 1:numel(fn), fprintf(fid, '%s = %s\n', fn{k}, v2s(s.(fn{k}))); end
fclose(fid);
end
