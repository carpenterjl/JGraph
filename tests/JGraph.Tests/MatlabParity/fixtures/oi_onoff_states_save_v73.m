% record: -noFigureWindows
% Open items 131 and 132 (ADR 0235), in the MATLAB dialect: every on/off property of forty kinds of
% graphics object and app component answering a matlab.lang.OnOffSwitchState (HandleVisibility and a
% uicontrol's or uitable's Enable staying char), and how one behaves where a char used to be -
% compared, converted, printed, set, switched on; a bare string, datetime, duration and
% calendarDuration saved and loaded; save -v7.3 of numbers of every class, complex, logical, char,
% empties, cells, structs, struct arrays and sparse, its header, -append and whos. Lines marked
% div=ADR0235 are what this build refuses in a version 7.3 file (strings, datetimes and function
% handles, kept in R2025b's object subsystem) and its own words (ADR 0156).
f = figure('Visible', 'off');
ax = axes(f);
hl = plot(ax, 1:3, [2 1 3]);
hold(ax, 'on');
hs = scatter(ax, 1:3, 1:3);
hb = bar(ax, 1:3);
ha = area(ax, 1:3);
he = errorbar(ax, 1:3, 1:3, [1 1 1]);
hst = stem(ax, 1:3);
hsr = stairs(ax, 1:3);
hq = quiver(ax, 1:2, 1:2, [1 1], [1 1]);
hp = patch(ax, [0 1 1], [0 0 1], 'r');
ht = text(ax, 1, 1, 'x');
hr = rectangle(ax, 'Position', [0 0 1 1]);
hli = line(ax, [0 1], [0 1]);
hc = colorbar(ax);
lg = legend(ax, 'a');
hg = hggroup(ax);
hx = xline(ax, 1);
hy = yline(ax, 1);
hold(ax, 'off');
ax2 = axes(figure('Visible', 'off'));
hsu = surf(ax2, peaks(5));
hold(ax2, 'on');
hm = mesh(ax2, peaks(5));
[~, hco] = contour(ax2, peaks(5));
hlt = light(ax2);
hold(ax2, 'off');
ax3 = axes(figure('Visible', 'off'));
him = image(ax3, magic(3));
ax4 = axes(figure('Visible', 'off'));
hh = histogram(ax4, [1 2 2 3]);
ax5 = axes(figure('Visible', 'off'));
hh2 = histogram2(ax5, [1 2 2 3], [1 1 2 3]);
ax6 = axes(figure('Visible', 'off'));
hpie = pie(ax6, [1 2]);
f7 = figure('Visible', 'off');
uc = uicontrol(f7, 'Style', 'pushbutton');
up = uipanel(f7);
um = uimenu(f7);
ubg = uibuttongroup(f7);
utb = uitoolbar(f7);
upt = uipushtool(utb);
utt = uitoggletool(utb);
ucm = uicontextmenu(f7);
uf = uifigure('Visible', 'off');
g = uigridlayout(uf);
ub = uibutton(g); ut = uitable(g); usp = uispinner(g); utr = uitree(g); ulb = uilistbox(g);
u9b_chk('oc_figure', @() onoffclasses(f));
u9b_chk('oc_axes', @() onoffclasses(ax));
u9b_chk('oc_line', @() onoffclasses(hl));
u9b_chk('oc_scatter', @() onoffclasses(hs));
u9b_chk('oc_bar', @() onoffclasses(hb));
u9b_chk('oc_area', @() onoffclasses(ha));
u9b_chk('oc_errorbar', @() onoffclasses(he));
u9b_chk('oc_stem', @() onoffclasses(hst));
u9b_chk('oc_stairs', @() onoffclasses(hsr));
u9b_chk('oc_quiver', @() onoffclasses(hq));
u9b_chk('oc_patch', @() onoffclasses(hp));
u9b_chk('oc_text', @() onoffclasses(ht));
u9b_chk('oc_rectangle', @() onoffclasses(hr));
u9b_chk('oc_colorbar', @() onoffclasses(hc));
u9b_chk('oc_legend', @() onoffclasses(lg));
u9b_chk('oc_hggroup', @() onoffclasses(hg));
u9b_chk('oc_xline', @() onoffclasses(hx));
u9b_chk('oc_surface', @() onoffclasses(hsu));
u9b_chk('oc_contour', @() onoffclasses(hco));
u9b_chk('oc_light', @() onoffclasses(hlt));
u9b_chk('oc_image', @() onoffclasses(him));
u9b_chk('oc_histogram', @() onoffclasses(hh));
u9b_chk('oc_histogram2', @() onoffclasses(hh2));
u9b_chk('oc_uicontrol', @() onoffclasses(uc));
u9b_chk('oc_uipanel', @() onoffclasses(up));
u9b_chk('oc_uimenu', @() onoffclasses(um));
u9b_chk('oc_uitoggletool', @() onoffclasses(utt));
u9b_chk('oc_xruler', @() onoffclasses(ax.XAxis));
u9b_chk('oc_title', @() onoffclasses(ax.Title));
u9b_chk('oc_root', @() onoffclasses(groot));
u9b_chk('oc_uifigure', @() onoffclasses(uf));
u9b_chk('oc_uibutton', @() onoffclasses(ub));
u9b_chk('oc_uitable', @() onoffclasses(ut));
u9b_chk('oc_uispinner', @() onoffclasses(usp));
u9b_chk('oc_uitree', @() onoffclasses(utr));
u9b_chk('oc_uilistbox', @() onoffclasses(ulb));
u9b_chk('ob_class', @() class(ax.Visible));
u9b_chk('ob_get_class', @() class(get(ax, 'Visible')));
u9b_chk('ob_get_cell', @() cellfun(@class, get(ax, {'Visible', 'Box'}), 'UniformOutput', false));
u9b_chk('ob_get_struct', @() class(getfield(get(ax), 'Visible')));
u9b_chk('ob_get_array', @() cellfun(@class, get([hl hli], 'Visible'), 'UniformOutput', false));
u9b_chk('ob_dot_array', @() class(dotarr([hl hli])));
u9b_chk('ob_dot_array_size', @() size(dotarr([hl hli])));
u9b_chk('ob_isequal', @() isequal(ax.Visible, 'on'));
u9b_chk('ob_isequal_true', @() isequal(ax.Visible, true));
u9b_chk('ob_eq_char', @() ax.Visible == 'on');
u9b_chk('ob_eq_off', @() ax.Visible == 'off');
u9b_chk('ob_eq_str', @() ax.Visible == "on");
u9b_chk('ob_ne', @() ax.Visible ~= 'on');
u9b_chk('ob_strcmp', @() strcmp(ax.Visible, 'on'));
u9b_chk('ob_strcmpi', @() strcmpi(ax.Visible, 'ON'));
u9b_chk('ob_ismember', @() ismember(ax.Visible, {'on', 'off'}));
u9b_chk('ob_contains', @() msgof(@() contains(ax.Visible, 'on')));
u9b_chk('ob_cat_char', @() msgof(@() ['v=' ax.Visible]));
u9b_chk('ob_cat_char2', @() msgof(@() [ax.Visible 'x']));
u9b_chk('ob_sprintf', @() sprintf('%s', ax.Visible));
u9b_chk('ob_sprintf_d', @() sprintf('%d', ax.Visible));
u9b_chk('ob_upper', @() msgof(@() upper(ax.Visible)));
u9b_chk('ob_string', @() string(ax.Visible));
u9b_chk('ob_char', @() char(ax.Visible));
u9b_chk('ob_logical', @() logical(ax.Visible));
u9b_chk('ob_not', @() ~ax.Visible);
u9b_chk('ob_plus', @() msgof(@() ax.Visible + 1));
u9b_chk('ob_double', @() double(ax.Visible));
u9b_chk('ob_if', @() ifon(ax.Visible));
u9b_chk('ob_switch', @() swon(ax.Visible));
u9b_chk('ob_switch_cell', @() swcell(ax.Visible));
u9b_chk('ob_num2str', @() msgof(@() num2str(ax.Visible)));
u9b_chk('ob_isempty', @() isempty(ax.Visible));
u9b_chk('ob_numel', @() numel(ax.Visible));
u9b_chk('ob_ischar', @() ischar(ax.Visible));
u9b_chk('ob_islogical', @() islogical(ax.Visible));
u9b_chk('ob_isa', @() isa(ax.Visible, 'logical'));
u9b_chk('ob_set_true', @() setv(ax, true));
u9b_chk('ob_set_one', @() setv(ax, 1));
u9b_chk('ob_set_zero', @() setv(ax, 0));
u9b_chk('ob_set_str', @() setv(ax, "off"));
u9b_chk('ob_set_two', @() msgof(@() setv(ax, 2)));
u9b_chk('ob_set_member', @() setv(ax, matlab.lang.OnOffSwitchState.off));
u9b_chkdiv('ob_findobj', @() oi_try( @() numel(findobj(ax, 'Visible', 'on'))), 'ADR0235');
u9b_chk('ob_grid', @() class(ax.XGrid));
u9b_chk('ob_hold', @() class(ax.NextPlot));
u9b_chk('ob_figure_visible', @() class(f.Visible));
u9b_chk('ob_handlevis', @() class(hl.HandleVisibility));
u9b_chk('ob_clipping', @() class(hl.Clipping));
u9b_chk('ob_uic_enable', @() class(uc.Enable));
u9b_chk('ob_cellfun_strcmp', @() cellfun(@(v) strcmp(v, 'on'), get([hl hli], 'Visible')));
u9b_chk('ob_struct_field', @() class(struct('v', ax.Visible).v));
u9b_chk('ob_cell_store', @() class({ax.Visible}));
u9b_chk('ob_cat_two', @() class([ax.Visible ax.Box]));
u9b_chk('ob_lower', @() msgof(@() lower(ax.Visible)));
u9b_chk('ob_mat2str', @() msgof(@() mat2str(ax.Visible)));
u9b_chk('ob_isequal_two', @() isequal(ax.Visible, ax.Box));
u9b_chk('ob_tf_any', @() any([ax.Visible ax.Box]));
u9b_chk('sv_string', @() svround("hello"));
u9b_chk('sv_string_arr', @() svround(["a" missing; "c" "d"]));
u9b_chk('sv_datetime', @() svround(datetime(2020, 1, 2, 3, 4, 5)));
u9b_chk('sv_datetime_zone', @() svround(datetime(2020, 1, 2, 'TimeZone', 'UTC', 'Format', 'yyyy-MM-dd HH:mm z')));
u9b_chk('sv_duration', @() svround(minutes([1.5 2])));
u9b_chk('sv_duration_fmt', @() svround(duration(1, 2, 3, 'Format', 'hh:mm')));
u9b_chk('sv_nat', @() svround(NaT));
u9b_chk('sv_cell_string', @() svround({"a", 1}));
u9b_chk('sv_struct_dt', @() svround(struct('t', datetime(2020, 1, 1))));
u9b_chk('sv_categorical', @() svround(categorical({'a', 'b'})));
u9b_chk('sv_caldur', @() svround(calmonths(2)));
u9b_chk('sv_whos_string', @() svwhos("hello"));
u9b_chk('sv_whos_dt', @() svwhos(datetime(2020, 1, 1)));
u9b_chk('sv_whos_dur', @() svwhos(seconds([1 2 3])));
u9b_chk('sv_v7', @() svver('-v7'));
u9b_chk('sv_v6', @() svver('-v6'));
u9b_chkdiv('sv_v4', @() oi_try( @() msgof(@() svver('-v4'))), 'ADR0235');
u9b_chk('v73_double', @() v73round(magic(3)));
u9b_chk('v73_scalar', @() v73round(5));
u9b_chk('v73_row', @() v73round(1:4));
u9b_chk('v73_nd', @() v73round(reshape(1:24, 2, 3, 4)));
u9b_chk('v73_single', @() v73round(single([1.5 2])));
u9b_chk('v73_int8', @() v73round(int8([-1 2])));
u9b_chk('v73_uint64', @() v73round(uint64(18446744073709551615)));
u9b_chk('v73_complex', @() v73round([1+2i 3]));
u9b_chk('v73_logical', @() v73round([true false; false true]));
u9b_chk('v73_char', @() v73round('hello'));
u9b_chk('v73_charmat', @() v73round(['ab'; 'cd']));
u9b_chk('v73_empty', @() v73round([]));
u9b_chk('v73_empty_char', @() v73round(''));
u9b_chk('v73_zeros3', @() v73round(zeros(0, 3)));
u9b_chk('v73_cell', @() v73round({1, 'a'; [1 2], {2}}));
u9b_chk('v73_empty_cell', @() v73round({}));
u9b_chk('v73_struct', @() v73round(struct('a', 1, 'b', 'x')));
u9b_chk('v73_struct_arr', @() v73round(struct('a', {1, 2, 3})));
u9b_chk('v73_struct_nested', @() v73round(struct('s', struct('t', {1; 2}))));
u9b_chk('v73_empty_struct', @() v73round(struct('a', {})));
u9b_chk('v73_sparse', @() v73round(sparse([1 0; 0 2])));
u9b_chkdiv('v73_string', @() oi_try( @() v73round("hi")), 'ADR0235');
u9b_chkdiv('v73_datetime', @() oi_try( @() v73round(datetime(2020, 1, 1))), 'ADR0235');
u9b_chkdiv('v73_handle', @() oi_try( @() v73round(@sin)), 'ADR0235');
u9b_chk('v73_unicode', @() v73round('héllo'));
u9b_chk('v73_whos', @() v73whos());
u9b_chk('v73_header', @() v73head());
u9b_chk('v73_append', @() v73append());
u9b_chk('v73_many', @() v73many());
u9b_chk('v73_load_vars', @() v73loadsome());



function out = ifon(v)
if v, out = 'yes'; else, out = 'no'; end
end

function out = swon(v)
switch v
    case 'on'
        out = 'on-case';
    case 'off'
        out = 'off-case';
    otherwise
        out = 'other';
end
end

function out = swcell(v)
switch v
    case {'on', 'yes'}
        out = 'on-case';
    otherwise
        out = 'other';
end
end

function out = setv(ax, v)
ax.Visible = v;
out = {class(ax.Visible), char(ax.Visible)};
ax.Visible = 'on';
end

function out = svround(v)
f = [tempname '.mat'];
save(f, 'v');
w = load(f);
delete(f);
out = {class(w.v), isequal(w.v, v), size(w.v)};
end

function out = svwhos(v)
f = [tempname '.mat'];
save(f, 'v');
w = whos('-file', f);
delete(f);
out = {w.class, w.size};
end

function out = svver(flag)
f = [tempname '.mat'];
v = "x";
save(f, 'v', flag);
w = load(f);
delete(f);
out = {class(w.v)};
end

function out = v73round(v)
f = [tempname '.mat'];
save(f, 'v', '-v7.3');
w = load(f);
delete(f);
out = {class(w.v), isequal(w.v, v), size(w.v)};
end

function out = v73whos()
f = [tempname '.mat'];
a = magic(3); b = 'xy'; c = {1}; s.q = 1; %#ok<STRNU>
save(f, 'a', 'b', 'c', 's', '-v7.3');
w = whos('-file', f);
delete(f);
out = {w.name; w.class};
end

function out = v73head()
f = [tempname '.mat'];
a = 1; %#ok<NASGU>
save(f, 'a', '-v7.3');
fid = fopen(f, 'r');
b = fread(fid, 128, '*uint8')';
fclose(fid);
delete(f);
out = {char(b(1:19)), b(125:128)};
end

function out = v73append()
f = [tempname '.mat'];
a = 1; b = 2; %#ok<NASGU>
save(f, 'a', '-v7.3');
save(f, 'b', '-append');
w = load(f);
delete(f);
out = sort(fieldnames(w))';
end

function out = v73many()
f = [tempname '.mat'];
s = struct();
for k = 1:40
    s.(sprintf('v%02d', k)) = k;
end
save(f, '-struct', 's', '-v7.3');
w = load(f);
delete(f);
out = {numel(fieldnames(w)), w.v37};
end

function out = v73loadsome()
f = [tempname '.mat'];
a = 1; b = 'x'; c = 3; %#ok<NASGU>
save(f, 'a', 'b', 'c', '-v7.3');
w = load(f, 'b', 'c');
delete(f);
out = fieldnames(w)';
end

function m = msgof(f)
try
    f();
    m = 'no error';
catch e
    m = [e.identifier ' | ' e.message];
end
end

function v = dotarr(h)
v = [h.Visible];
end

function out = onoffclasses(h)
% Each on/off property of h: S for an OnOffSwitchState, C for the char 'on' or 'off' (item 132).
p = get(h);
names = fieldnames(p);
parts = {};
for k = 1:numel(names)
    v = p.(names{k});
    if isa(v, 'matlab.lang.OnOffSwitchState')
        parts{end + 1} = [names{k} ':S']; %#ok<AGROW>
    elseif ischar(v) && any(strcmp(v, {'on', 'off'}))
        parts{end + 1} = [names{k} ':C']; %#ok<AGROW>
    end
end
out = strjoin(sort(parts), ',');
end

function r = oi_try(fn)
% The value fn answers, or the identifier of the error it raises, as one line (a divergence row).
try
    r = fn();
catch err
    r = err.identifier;
end
end
