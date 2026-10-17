% dev_arrays.m -- an array of device objects as R2025b's object array (open item 36, ADR 0237):
% the brackets and rearranging verbs, subscripts and growth, loops and comma lists, the generic
% verbs, eq, what holds the ports open, and the display, on the com0com pair COM20<->COM21 in
% R2025b and the simulated ports jgraph.internal.devicesim registers in JGraph. The preferences are
% cleared first so a growth's gap asks for a default serialport no saved settings can make.
%
% The div rows: a class method on an array errors through its own argument validation in R2025b
% where JGraph refuses in one voice; the full displays keep JGraph's flat property layout (ADR
% 0174) under R2025b's aligned columns.
try %#ok<TRYNC> % JGraph: the simulated ports; R2025b: the real pair
    jgraph.internal.devicesim('COM20');
    jgraph.internal.devicesim('COM21');
end
internal.Serialport.clearPreferences();
a = serialport("COM20", 9600);
b = serialport("COM21", 9600);
f = [a b];

% --- the brackets and shapes ------------------------------------------------------------------------
ix_chk('hcat', dsc(f));
ix_chk('vcat', dsc([a; b]));
ix_chk('hcat3', dsc([a b a]));
ix_chk('hcat_row_scalar', dsc([f a]));
ix_chk('vcat_rows', dsc([f; f]));
ix_chk('transpose', dsc(f'));
ix_chk('empty_join', [dsc([a []]) ' and ' dsc([[] a])]);
ix_chk('shape', sprintf('%d %d %d %d %d %d %d %d', isempty(f), isscalar(f), isrow(f), isvector(f), iscolumn(f), ismatrix(f), ndims(f), length(f)));
ix_chk('class_row', sprintf('%s %d %d %d', class(f), isa(f, 'handle'), isa(f, 'internal.Serialport'), isobject(f)));
ix_chk('same_twice', dsc([a a]));
ix_chk('join_cell', dsc([{a} {b}]));
ix_chk('join_number', dv_err(@() [a 1]));
ix_chk('join_text', dv_err(@() [a 'x']));
ix_chk('join_struct', dv_err(@() [a struct('x', 1)]));
ix_chk('join_number_first', dv_err(@() [1 a]));

% --- subscripts -------------------------------------------------------------------------------------
ix_chk('index_scalar', sprintf('%s %s %s', f(2).Port, f(end).Port, dsc(f(1))));
ix_chk('index_vector', [dsc(f([2 1])) ' ' char(first_port(f([2 1])))]);
ix_chk('index_range', dsc(f(2:end)));
ix_chk('index_colon', dsc(f(:)));
ix_chk('index_logical', dsc(f([false true])));
ix_chk('index_two', dsc(f(1, 2)));
ix_chk('index_out', dv_err(@() f(3)));
ix_chk('index_zero', dv_err(@() f(0)));
ix_chk('index_empty', dsc(f([])));
ix_chk('end_arith', f(end - 1).Port);
ix_chk('scalar_index_twice', [dsc(a([1 1])) ' ' twice_same(a)]);
ix_chk('scalar_transpose', dsc(a'));
ix_chk('scalar_colon', dsc(a(:)));
ix_chk('scalar_logical', dsc(a(true)));

% --- loops and comma lists --------------------------------------------------------------------------
ix_chk('loop', loop_ports(f));
ix_chk('loop_column', loop_ports([a; b]));
ix_chk('dot_on_row', one_out(f));
ix_chk('dot_list', numel({f.Port}));
ix_chk('bracket_list', dsc([f.Port]));

% --- eq and isequal ---------------------------------------------------------------------------------
ix_chk('eq', mat2str(f == a));
ix_chk('eq_rows', mat2str(f == f));
ix_chk('ne_scalar', mat2str(f ~= a));
ix_chk('eq_number', mat2str(f == 1));
ix_chk('eq_text', mat2str(f == 'x'));
ix_chk('eq_mismatch', dv_err(@() f(:) == f));
ix_chk('eq_empty', dsc(f([]) == a));
ix_chk('eq_square', mat2str([f; f] == [f; f]));
ix_chk('isequal_rows', sprintf('%d %d %d', isequal(f(1), a), isequal(f, [a b]), isequal(f, [b a])));
ix_chk('isvalid_row', mat2str(isvalid(f)));

% --- the generic verbs ------------------------------------------------------------------------------
ix_chk('get_struct', dsc(get(f)));
ix_chk('get_one_name', shown_cell(get(f, 'Port')));
ix_chk('get_two_names', dsc(get(f, {'Port', 'Timeout'})));
set(f, 'Timeout', 5);
ix_chk('set_both', sprintf('%g %g', a.Timeout, b.Timeout));
ix_chk('properties_row', dsc(properties(f)));
ix_chk('methods_row', dsc(methods(f)));
ix_chk('isprop_row', isprop(f, 'Port'));
ix_chk('isprop_none', isprop(f, 'Bogus'));

% --- the rearranging verbs --------------------------------------------------------------------------
ix_chk('repmat_scalar', dsc(repmat(a, 1, 3)));
ix_chk('horzcat_fn', dsc(horzcat(a, b)));
ix_chk('vertcat_fn', dsc(vertcat(a, b)));
ix_chk('cat_fn', dsc(cat(2, a, b)));
ix_chk('reshape_fn', dsc(reshape(f, 2, 1)));
ix_chk('fliplr_fn', char(first_port(fliplr(f))));
ix_chk('flip_fn', char(first_port(flip(f))));
ix_chk('num2cell_fn', [dsc(num2cell(f)) ' ' cell_first(f, a)]);
ix_chk('arrayfun_ports', strjoin(arrayfun(@(p) p.Port, f), ','));
ix_chk('arrayfun_objects', dsc(arrayfun(@(p) p, f, 'UniformOutput', false)));

% --- growth and writes ------------------------------------------------------------------------------
ix_chk('grow_far', dv_err(@() grow_far(f, a)));
ix_chk('member_grow', dv_err(@() member_grow(f)));
ix_chk('member_set_one', member_set_one(f));
ix_chk('member_set_all', dv_err(@() member_set_all(f)));
ix_chk('assign_same', assign_same(f, b));
ix_chk('assign_delete', shrink(f));

% --- conversions and methods on the row -------------------------------------------------------------
ix_chk('char_row', dv_err(@() char(f)));
ix_chk('string_row', dv_err(@() string(f)));
ix_chk('double_row', dv_err(@() double(f)));
ix_chk('cellstr_row', dv_err(@() cellstr(f)));
ix_chk('flush_row', dv_err(@() flush_all(f)), 'div=ADR0237');
ix_chk('cfg_row', dv_err(@() cfg_all(f)), 'div=ADR0237');

% --- display ----------------------------------------------------------------------------------------
ix_chk('disp_head', first_line(evalc('disp(f)')));
ix_chk('disp_full', flatten(evalc('disp(f)')), 'div=ADR0237');
ix_chk('disp_empty', flatten(evalc('disp(f([]))')));

% --- the other transports ---------------------------------------------------------------------------
u0 = udpport;
port = u0.LocalPort;
clear u0
echotcpip("on", port);
t1 = tcpclient("127.0.0.1", port);
t2 = tcpclient("127.0.0.1", port);
ix_chk('tcp_hcat', dsc([t1 t2]));
ix_chk('tcp_find', dsc(tcpclientfind));
ix_chk('tcp_disp_head', first_line(evalc('disp([t1 t2])')));
u1 = udpport;
u2 = udpport;
ix_chk('udp_hcat', dsc([u1 u2]));
ix_chk('mixed', dv_err(@() [a t1]));
ix_chk('mixed_udp', dv_err(@() [t1 u1]));
ix_chk('udp_grow', dv_err(@() grow_far([u1 u2], u2)));
ix_chk('foreign_write', dv_err(@() assign_same(f, t1)));
ix_chk('eq_foreign', mat2str(a == t1));
ix_chk('eq_foreign_row', mat2str(f == t1));
clear t1 t2 u1 u2
echotcpip("off");

% --- the audio classes override their brackets away -------------------------------------------------
p = audioplayer(zeros(4410, 1), 44100);
q = audioplayer(zeros(4410, 1), 44100);
ix_chk('ap_hcat', dv_err(@() [p q]));
ix_chk('ap_horzcat_fn', dv_err(@() horzcat(p, q)));
ix_chk('ap_repmat', dsc(repmat(p, 1, 2)));
ix_chk('ap_cat_fn', dsc(cat(2, p, q)));
ix_chk('ap_index_twice', dsc(p([1 1])));
ix_chk('ap_empty_join', dv_err(@() [p []]));
r = audiorecorder;
ix_chk('ar_mixed', dv_err(@() [p r]));
ix_chk('ar_number', dv_err(@() [r 1]));
clear p q r

% --- what holds the ports open ----------------------------------------------------------------------
held = serialportfind;
clear a b f
ix_chk('held_after_clear', sprintf('%d', numel(serialportfind)));
ix_chk('held_ports_open', mat2str(isvalid(held)));
clear held
ix_chk('released', ix_show(serialportfind));
a = serialport("COM20", 9600);
c = {[a a]};
clear a
ix_chk('cell_holds', sprintf('%d', numel(serialportfind)));
clear c
ix_chk('cell_released', ix_show(serialportfind));

% --- delete on the row ------------------------------------------------------------------------------
a = serialport("COM20", 9600);
b = serialport("COM21", 9600);
f = [a b];
ix_chk('delete_row_out', dv_err(@() delete_out(f)));
delete(b);
ix_chk('isvalid_mixed', mat2str(isvalid(f)));
ix_chk('disp_mixed', flatten(evalc('disp(f)')), 'div=ADR0237');
delete_all(f);
ix_chk('deleted_all', sprintf('%d %d %d', isvalid(a), isvalid(b), numel(serialportfind)));
ix_chk('disp_deleted', flatten(evalc('disp(f)')));
ix_chk('index_deleted', class(f(2)));
ix_chk('dot_deleted', dv_err(@() f(2).Port));
clear

function text = dsc(v)
text = sprintf('%s %s', class(v), mat2str(size(v)));
end

function v = one_out(f)
v = f.Port; % one output wanted: the first of the comma list
end

function x = delete_out(f)
x = delete(f); %#ok<NASGU> % an output asked of delete refuses before anything is deleted
end

function text = flatten(shown)
text = regexprep(shown, '<[^>]*>', '');
end

function text = first_line(shown)
parts = strsplit(regexprep(shown, '<[^>]*>', ''), newline);
text = strtrim(parts{1});
end

function port = first_port(g)
port = g(1).Port;
end

function text = twice_same(a)
g = a([1 1]);
text = sprintf('%d %d', g(1) == a, g(2) == a);
end

function text = loop_ports(f)
text = '';
for p = f
    text = [text sprintf('[%s %s]', dsc(p), strjoin(arrayfun(@(q) char(q.Port), p, 'UniformOutput', false), '+'))]; %#ok<AGROW>
end
end

function text = shown_cell(c)
text = sprintf('%s %s %s', dsc(c), class(c{1}), strjoin(cellfun(@char, c, 'UniformOutput', false), ','));
end

function text = cell_first(f, a)
nc = num2cell(f);
text = sprintf('%s %d', class(nc{1}), nc{1} == a);
end

function grow_far(f, a)
g = f;
g(5) = a; %#ok<NASGU>
end

function member_grow(f)
g = f;
g(3).Timeout = 5; %#ok<NASGU>
end

function text = member_set_one(f)
g = f;
g(2).Timeout = 7;
text = sprintf('%s %g %g %g', dsc(g), g(1).Timeout, g(2).Timeout, f(2).Timeout);
end

function member_set_all(f)
g = f;
g.Timeout = 9; %#ok<NASGU,STRNU>
end

function text = assign_same(f, b)
g = f;
g(1) = b;
text = sprintf('%s %s %d', dsc(g), char(g(1).Port), g(1) == b);
end

function text = shrink(f)
f(1) = [];
text = [dsc(f) ' ' char(f(1).Port)];
end

function flush_all(f)
flush(f);
end

function cfg_all(f)
configureTerminator(f, "CR");
end

function delete_all(f)
delete(f);
end
