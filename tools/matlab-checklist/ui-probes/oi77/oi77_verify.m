% oi77_verify - open item 77: R2025b opening the .fig files JGraph wrote (oi77_make, in rt\): the
% variables each holds, what openfig makes of each, the objects and their properties, the
% callbacks run, and a print of each figure (rt\jg_*_matlab.png) for the eye to set beside
% JGraph's own export. Headless (-noFigureWindows).
here = fileparts(mfilename('fullpath'));
rt = fullfile(here, 'rt');
set(groot, 'DefaultFigureVisible', 'off');

for name = {'jg_fh_named', 'jg_fh_cell', 'jg_plots', 'jg_gui', 'jg_annot', 'jg_odd', 'jg_fh_anon0', 'jg_fh_anon'}
    file = fullfile(rt, [name{1} '.fig']);
    fprintf('\n=== %s\n', name{1});
    if ~isfile(file), fprintf('  missing\n'); continue; end
    w = whos('-file', file);
    for i = 1:numel(w), fprintf('  var %s %s %s\n', w(i).name, w(i).class, mat2str(w(i).size)); end
    lastwarn('');
    try
        [txt, h] = evalc('openfig(file, ''invisible'')');
        if ~isempty(strtrim(txt)), fprintf('  printed: %s\n', strrep(strtrim(txt), newline, ' / ')); end
        [msg, id] = lastwarn;
        if ~isempty(msg), fprintf('  warning: %s | %s\n', id, strrep(msg, newline, ' / ')); end
    catch e
        fprintf('  error: %s | %s\n', e.identifier, strrep(e.message, newline, ' / '));
        continue
    end
    fprintf('  figure: Name=%s Tag=%s Color=%s Position=%s NumberTitle=%s MenuBar=%s Colormap=%s\n', h.Name, h.Tag, mat2str(h.Color, 3), mat2str(h.Position), char(h.NumberTitle), h.MenuBar, mat2str(size(h.Colormap)));
    all = findall(h);
    types = cellfun(@(x) char(x), get(all, 'Type'), 'UniformOutput', false);
    types = types(~strcmp(types, 'uimenu') | ~cellfun(@isempty, regexp(get(all, 'Tag'), '^m')));
    fprintf('  types (menus of the figure left out): %s\n', strjoin(types', ','));
    tags = get(all, 'Tag'); tags = tags(~cellfun(@isempty, tags) & cellfun(@isempty, regexp(tags, '^fig|^Standard|^Annotation\.|^DataManager|^print|^scribe')));
    fprintf('  tags: %s\n', strjoin(tags', ','));
    for a = findobj(h, 'Type', 'axes')'
        kinds = get(a.Children, 'Type');
        if isempty(kinds), kinds = {}; elseif ~iscell(kinds), kinds = {kinds}; end
        fprintf('  axes %s: Position=%s XLim=%s(%s) YLim=%s(%s) View=%s Box=%s Grid=%s FontSize=%g title=%s xlabel=%s ylabel=%s children=%s\n', a.Tag, mat2str(a.Position, 4), mat2str(a.XLim, 4), a.XLimMode, mat2str(a.YLim, 4), a.YLimMode, mat2str(a.View), char(a.Box), char(a.XGrid), a.FontSize, char(join(string(a.Title.String), '|')), char(join(string(a.XLabel.String), '|')), char(join(string(a.YLabel.String), '|')), strjoin(kinds', ','));
    end
    for ln = findobj(h, 'Type', 'line')'
        fprintf('  line %s: Color=%s LineStyle=%s Marker=%s LineWidth=%g DisplayName=%s XData=%s YData=%s\n', ln.Tag, mat2str(ln.Color, 3), ln.LineStyle, ln.Marker, ln.LineWidth, ln.DisplayName, mat2str(ln.XData, 3), mat2str(ln.YData, 3));
    end
    for tx = findobj(h, 'Type', 'text')'
        if ~isempty(tx.String), fprintf('  text: String=%s Position=%s Color=%s\n', char(join(string(tx.String), '|')), mat2str(tx.Position, 3), mat2str(tx.Color, 3)); end
    end
    for lg = findobj(h, 'Type', 'legend')'
        fprintf('  legend: String=%s Location=%s Axes=%s\n', strjoin(lg.String, '|'), lg.Location, lg.Axes.Tag);
    end
    for cb = findobj(h, 'Type', 'colorbar')'
        fprintf('  colorbar: Location=%s Limits=%s Axes=%s\n', cb.Location, mat2str(cb.Limits), cb.Axes.Tag);
    end
    for im = findobj(h, 'Type', 'image')'
        fprintf('  image: CData=%s CDataMapping=%s XData=%s YData=%s CLim=%s\n', mat2str(size(im.CData)), im.CDataMapping, mat2str(im.XData), mat2str(im.YData), mat2str(im.Parent.CLim));
    end
    for sf = findobj(h, 'Type', 'surface')'
        fprintf('  surface %s: ZData=%s FaceColor=%s\n', sf.Tag, mat2str(size(sf.ZData)), mat2str(sf.FaceColor));
    end
    for b = findobj(h, 'Type', 'bar')', fprintf('  bar %s: YData=%s FaceColor=%s\n', b.Tag, mat2str(b.YData), mat2str(b.FaceColor, 3)); end
    for s = findobj(h, 'Type', 'scatter')', fprintf('  scatter %s: YData=%s SizeData=%s MarkerFaceColor=%s\n', s.Tag, mat2str(s.YData), mat2str(s.SizeData), mat2str(s.MarkerFaceColor, 3)); end
    for s = findobj(h, 'Type', 'stair')', fprintf('  stairs %s: YData=%s Color=%s\n', s.Tag, mat2str(s.YData), mat2str(s.Color, 3)); end
    for pa = findall(h, 'Type', 'polaraxes')', fprintf('  polaraxes %s: ThetaZeroLocation=%s children=%s RData=%s\n', pa.Tag, pa.ThetaZeroLocation, strjoin(cellstr(get(pa.Children, 'Type'))', ','), mat2str(size(pa.Children(1).RData))); end
    for pt = findobj(h, 'Type', 'patch')', fprintf('  patch %s: FaceColor=%s FaceAlpha=%g Vertices=%s\n', pt.Tag, mat2str(pt.FaceColor), pt.FaceAlpha, mat2str(size(pt.Vertices))); end
    for r = findobj(h, 'Type', 'rectangle')', fprintf('  rectangle %s: Position=%s Curvature=%s FaceColor=%s\n', r.Tag, mat2str(r.Position), mat2str(r.Curvature), mat2str(r.FaceColor)); end
    for g = findobj(h, 'Type', 'hggroup')', fprintf('  hggroup %s: children=%s\n', g.Tag, strjoin(cellstr(get(g.Children, 'Tag'))', ',')); end
    an = findall(h, '-regexp', 'Type', 'textarrow|ellipse|textbox|arrow|doublearrow');
    for k = 1:numel(an), fprintf('  annotation %s: Type=%s Position=%s\n', an(k).Tag, an(k).Type, mat2str(an(k).Position, 3)); end
    for p = findobj(h, 'Type', 'uipanel')', fprintf('  uipanel %s: Title=%s Position=%s children=%s\n', p.Tag, p.Title, mat2str(p.Position, 3), strjoin(cellstr(get(p.Children, 'Tag'))', ',')); end
    for g = findobj(h, 'Type', 'uibuttongroup')', fprintf('  uibuttongroup %s: Title=%s SelectedObject=%s children=%s\n', g.Tag, g.Title, g.SelectedObject.Tag, strjoin(cellstr(get(g.Children, 'Tag'))', ',')); end
    for c = findobj(h, 'Type', 'uicontrol')'
        v = c.Value; if isempty(v), vs = '[]'; else, vs = mat2str(v); end
        s = c.String; if iscell(s), ss = strjoin(s', '|'); else, ss = char(join(string(s), '|')); end
        cb = c.Callback;
        if isa(cb, 'function_handle'), cbs = ['@fh ' func2str(cb)]; elseif iscell(cb), cbs = ['cell ' func2str(cb{1})]; elseif ischar(cb), cbs = ['char ' cb]; else, cbs = class(cb); end
        fprintf('  uicontrol %s: Style=%s String=%s Value=%s Units=%s Position=%s FontWeight=%s HAlign=%s Callback=%s\n', c.Tag, c.Style, ss, vs, c.Units, mat2str(c.Position, 3), c.FontWeight, c.HorizontalAlignment, cbs);
        if ~isempty(cb)
            try, out = evalc('hgfeval(cb, c, [])'); fprintf('    ran: %s\n', strtrim(out)); catch e, fprintf('    run failed: %s\n', e.message); end
        end
        if ~isempty(c.ContextMenu), fprintf('    ContextMenu=%s items=%s\n', c.ContextMenu.Tag, strjoin(cellstr(get(c.ContextMenu.Children, 'Tag'))', ',')); end
    end
    for mn = findall(h, 'Type', 'uimenu', '-regexp', 'Tag', '^m')', fprintf('  uimenu %s: Text=%s Accelerator=%s callback=%s\n', mn.Tag, mn.Text, mn.Accelerator, class(mn.MenuSelectedFcn)); end
    for t = findobj(h, 'Type', 'uitable')', fprintf('  uitable %s: Data=%s ColumnName=%s Position=%s\n', t.Tag, mat2str(t.Data), strjoin(cellstr(t.ColumnName)', ','), mat2str(t.Position)); end
    for tb = findobj(h, 'Type', 'uitoolbar')'
        if ~isempty(tb.Tag), fprintf('  uitoolbar %s: tools=%s\n', tb.Tag, strjoin(cellstr(get(tb.Children, 'Tag'))', ',')); end
    end
    for tt = findall(h, 'Type', 'uitoggletool', 'Tag', 'tt')', fprintf('  uitoggletool %s: State=%s Tooltip=%s\n', tt.Tag, char(tt.State), tt.Tooltip); end
    png = fullfile(rt, [name{1} '_matlab.png']);
    try
        print(h, '-dpng', '-r72', png);
        d = dir(png); fprintf('  print wrote %d bytes\n', d.bytes);
    catch e
        fprintf('  print: %s\n', e.message);
    end
    delete(h);
end
fprintf('done\n');
