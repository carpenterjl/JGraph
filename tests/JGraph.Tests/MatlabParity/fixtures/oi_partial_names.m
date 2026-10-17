% record: -noFigureWindows
% Open item 86 (ADR 0238): the start of a property name R2025b's get, set, findobj and the creating
% functions take in place of the whole name, and the starts they refuse. The rule was read off
% probe_86 (open-items scratch): the names of the object's class that begin with the text are
% ranked, a listed settable name above a read-only one or a ...Mode twin, those above the hidden
% names, and the start means the one name in the best rank that has any. The dot, isprop, a struct
% given to set and findobj's -property take whole names only. Lines marked div=ADR0238 are where
% R2025b answers a hidden internal this build does not keep.
f = figure('Visible', 'off', 'Position', [100 100 560 420]);
ax = axes(f);
ln = plot(ax, 1:3);
tx = text(ax, 1, 1, 'a');
pa = patch(ax, [0 1 1], [0 0 1], 'r');
sf = surface(axes(figure('Visible', 'off')), magic(3));
br = bar(axes(figure('Visible', 'off')), 1:3);
uc = uicontrol(f, 'Style', 'text');

% --- get takes a start, in any case, and refuses an ambiguous or unknown one
u9b_chk('get_pos', @() isequal(get(ax, 'Pos'), ax.Position));
u9b_chk('get_case', @() isequal(get(ax, 'pOS'), ax.Position));
u9b_chk('get_cell', @() isequal(get(ax, {'Pos', 'XLi'}), {ax.Position, ax.XLim}));
u9b_chk('get_cell_bad', @() get(ax, {'Pos', 'Tit'}));
u9b_chk('get_xli', @() get(ax, 'XLi'));
u9b_chk('get_xlimm', @() get(ax, 'XLimM'));
u9b_chk('get_xlimi', @() get(ax, 'XLimi'));
u9b_chk('get_positionc', @() get(ax, 'PositionC'));
u9b_chk('get_positionconstraint', @() get(ax, 'PositionConstraint'));
u9b_chk('get_tit', @() get(ax, 'Tit'));
u9b_chk('get_col_axes', @() get(ax, 'Col'));
u9b_chk('get_bo', @() get(ax, 'Bo'));
u9b_chk('get_u_axes', @() get(ax, 'U'));
u9b_chk('get_ya', @() get(ax, 'YA'));
u9b_chk('get_xa', @() get(ax, 'XA'));
u9b_chk('get_n', @() get(ax, 'N'));
u9b_chk('get_pa', @() isequal(get(ax, 'Pa'), f));
u9b_chk('get_ta', @() get(ax, 'Ta'));
u9b_chk('get_ty', @() get(ax, 'Ty'));
u9b_chk('get_xd_axes', @() get(ax, 'XD'));
u9b_chk('get_camerap', @() isequal(get(ax, 'CameraP'), ax.CameraPosition));
u9b_chk('get_tickd', @() isequal(get(ax, 'TickD'), ax.TickDir));
u9b_chk('get_lay', @() get(ax, 'Lay'));
u9b_chk('get_fonts', @() get(ax, 'FontS'));
u9b_chk('get_fontsi', @() isequal(get(ax, 'FontSi'), ax.FontSize));
u9b_chk('get_empty', @() get(ax, ''));
u9b_chk('get_space', @() get(ax, 'Pos '));

% --- a line, a text, a patch, a surface, a bar, a figure, the root
u9b_chk('line_col', @() isequal(get(ln, 'Col'), ln.Color));
u9b_chk('line_lines', @() get(ln, 'LineS'));
u9b_chk('line_linew', @() isequal(get(ln, 'LineW'), ln.LineWidth));
u9b_chk('line_mark', @() get(ln, 'Mark'));
u9b_chk('line_markers', @() get(ln, 'MarkerS'));
u9b_chk('line_markere', @() get(ln, 'MarkerE'));
u9b_chk('line_xd', @() get(ln, 'XD'));
u9b_chk('line_xdatam', @() get(ln, 'XDataM'));
u9b_chk('line_u', @() get(ln, 'U'));
u9b_chk('line_dis', @() get(ln, 'Dis'));
u9b_chk('line_seriesi', @() get(ln, 'SeriesI'));
u9b_chk('text_fonts', @() get(tx, 'FontS'));
u9b_chk('text_str', @() get(tx, 'Str'));
u9b_chk('text_hor', @() get(tx, 'Hor'));
u9b_chk('text_t', @() get(tx, 'T'));
u9b_chk('patch_facec', @() get(pa, 'FaceC'));
u9b_chk('patch_facev', @() get(pa, 'FaceV'));
u9b_chk('patch_t', @() get(pa, 'T'));
u9b_chk('surface_cdatam', @() get(sf, 'CDataM'));
u9b_chk('surface_xd', @() size(get(sf, 'XD')));
u9b_chk('bar_barw', @() get(br, 'BarW'));
u9b_chk('bar_facec', @() isequal(get(br, 'FaceC'), br.FaceColor));
u9b_chk('bar_cdatamo', @() get(br, 'CDataMo'));
u9b_chk('fig_nam', @() get(f, 'Nam'));
u9b_chk('fig_num', @() get(f, 'Nu'));
u9b_chk('fig_men', @() get(f, 'Men'));
u9b_chk('fig_uni', @() get(f, 'Uni'));
u9b_chk('fig_colorm', @() size(get(f, 'Colorm'), 2));
u9b_chk('fig_col', @() get(f, 'Col'));
u9b_chk('fig_win', @() get(f, 'Win'));
u9b_chk('fig_windowst', @() get(f, 'WindowSt'));
u9b_chk('fig_pos', @() numel(get(f, 'Pos')));
u9b_chk('fig_paperp', @() numel(get(f, 'PaperP')));
u9b_chk('root_screens', @() numel(get(0, 'ScreenS')));
u9b_chk('root_un', @() get(0, 'Un'));
u9b_chk('root_scr', @() get(0, 'Scr'));
u9b_chk('uicontrol_str', @() get(uc, 'Str'));
u9b_chk('uicontrol_sty', @() get(uc, 'Sty'));
u9b_chk('uicontrol_pos', @() numel(get(uc, 'Pos')));
u9b_chk('uicontrol_fon', @() get(uc, 'Fon'));

% --- set, in its forms
u9b_chk('set_pos', @() pn_setget(ax, 'Pos', [0.2 0.2 0.5 0.5], 'Position'));
u9b_chk('set_cellcell', @() pn_setget_cells(ax, {'Pos'}, {[0.3 0.3 0.4 0.4]}, 'Position'));
u9b_chk('set_many', @() pn_setmany(ax));
u9b_chk('set_linew', @() pn_setget(ln, 'LineW', 3, 'LineWidth'));
u9b_chk('set_tit', @() pn_setget(ax, 'Tit', 1, 'Title'));
u9b_chk('set_mark', @() pn_setget(ln, 'Mark', 'o', 'Marker'));
u9b_chk('set_struct_start', @() pn_setstruct(ax, struct('Pos', [0.1 0.1 0.6 0.6])));
u9b_chk('set_struct_whole', @() pn_setstruct(ax, struct('Position', [0.1 0.1 0.6 0.6])));
u9b_chk('set_listing_pos', @() set(ax, 'Pos'));
u9b_chk('set_listing_box', @() set(ax, 'Bo'));
u9b_chk('set_listing_boxs', @() set(ax, 'BoxS'));
u9b_chk('set_ya', @() pn_setget(ax, 'YA', 'right', 'YAxisLocation'));
u9b_chk('set_uicontrol_str', @() pn_setget(uc, 'Str', 'y', 'String'));

% --- the creating functions, the dot, isprop and findobj
u9b_chk('plot_linew', @() plot(ax, 1:3, 'LineW', 2).LineWidth);
u9b_chk('plot_col', @() plot(ax, 1:3, 'Col', 'r').Color);
u9b_chk('plot_mark', @() plot(ax, 1:3, 'Mark', 'o').Marker);
u9b_chk('plot_spec_then_start', @() plot(ax, 1:3, 'co', 'LineW', 2).LineWidth);
u9b_chk('plot_markers', @() plot(ax, 1:3, 'MarkerS', 9).MarkerSize);
u9b_chk('line_col_made', @() line(ax, [0 1], [0 1], 'Col', 'g').Color);
u9b_chk('text_fontsi_made', @() text(ax, 1, 1, 'a', 'FontSi', 12).FontSize);
u9b_chk('text_fonts_made', @() text(ax, 1, 1, 'a', 'FontS', 12).FontSize);
u9b_chk('figure_nam', @() figure('Nam', 'x', 'Vis', 'off').Name);
u9b_chk('figure_visi', @() figure('Visi', 'off').Visible);
u9b_chk('axes_pos', @() axes(f, 'Pos', [0 0 0.5 0.5]).Position);
u9b_chk('axes_xli', @() axes(f, 'XLi', [2 5]).XLim);
u9b_chk('axes_col', @() axes(f, 'Col', 'r').Color);
u9b_chk('uicontrol_str_made', @() uicontrol(f, 'Str', 'x').String);
u9b_chk('uicontrol_sty_made', @() uicontrol(f, 'Sty', 'text').Style);
u9b_chk('dot_get', @() ax.Pos);
u9b_chk('dot_set', @() pn_dotset(ax));
u9b_chk('isprop_start', @() isprop(ax, 'Pos'));
u9b_chk('isprop_case', @() isprop(ax, 'position'));
u9b_chk('findobj_typ', @() numel(findobj(f, 'Typ', 'uicontrol')));
u9b_chk('findobj_sty', @() numel(findobj(f, 'Sty', 'text')));
u9b_chk('findobj_prop', @() numel(findobj(f, '-property', 'Pos')));
u9b_chk('findobj_prop_whole', @() numel(findobj(f, '-property', 'Position')) > 0);

% --- what R2025b answers from its hidden internals this build does not keep (divergences)
u9b_chk('hidden_start', @() class(get(ax, 'Title_'))); % a start of a hidden name is refused in both engines, in the same words
u9b_chkdiv('hidden_only', @() pn_class_or_id(@() get(ax, 'UIContextM')), 'ADR0238');
close all force

function v = pn_setget(h, name, value, whole)
set(h, name, value);
v = pn_round(get(h, whole));
end

function v = pn_setget_cells(h, names, values, whole)
set(h, names, values);
v = pn_round(get(h, whole));
end

function v = pn_setstruct(h, s)
set(h, s);
v = pn_round(get(h, 'Position'));
end

function v = pn_round(v)
% A position written in normalized units comes back through pixels with each engine's own noise.
if isnumeric(v)
    v = round(v, 6);
end
end

function v = pn_setmany(ax)
set(ax, 'XLi', [1 5], 'YLi', [2 6]);
v = [ax.XLim ax.YLim];
end

function v = pn_dotset(ax)
ax.Pos = [0 0 1 1];
v = ax.Position;
end

function s = pn_class_or_id(fn)
% The class of what a read answers, or the identifier of its refusal, on one line either way.
try
    s = class(fn());
catch e
    s = e.identifier;
end
end
