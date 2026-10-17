% probe_dev_arrays2.m -- the rows probe_dev_arrays left out (open item 36): the display text of an
% object array, the generic verbs and methods on one, eq at its edges, growth and member writes,
% conversions, and what holds the ports open. Needs the com0com pair COM20<->COM21 and nothing else;
% it opens no real device. Helpers must avoid evalc in a lambda (the string sees the lambda's
% workspace, not the caller's), so every display row goes through show_* functions.
internal.Serialport.clearPreferences();
a = serialport("COM20", 9600);
b = serialport("COM21", 9600);
f = [a b];

% --- display ---------------------------------------------------------------------------------------
row('disp_row', @() show_disp(f));
row('echo_row', @() show_echo(f));
row('disp_square', @() show_disp([f; f]));
row('disp_empty', @() show_disp(f([])));
row('echo_empty', @() show_echo(f([])));
row('disp_column', @() show_disp(f'));

% --- a scalar indexed as an array ------------------------------------------------------------------
row('scalar_index_twice', @() describe(a([1 1])));
row('scalar_transpose', @() describe(a'));
row('scalar_colon', @() describe(a(:)));
row('scalar_logical', @() describe(a(true)));
row('scalar_row_same', @() twice_same(a));

% --- the generic verbs on the row ------------------------------------------------------------------
row('get_struct', @() describe(get(f)));
row('get_two_names', @() describe(get(f, {'Port', 'Timeout'})));
row('get_one_name', @() show_cell(get(f, 'Port')));
row('set_row', @() set_row(f));
row('properties_row', @() describe(properties(f)));
row('isprop_row', @() sprintf('%d %d', isprop(f, 'Port'), isprop(f, 'Bogus')));
row('methods_row', @() describe(methods(f)));

% --- methods and conversions on the row ------------------------------------------------------------
row('flush_row', @() flush_noout(f));
row('cfg_row', @() cfg_noout(f));
row('writeline_row', @() write_noout(f));
row('char_row', @() char(f));
row('string_row', @() string(f));
row('double_row', @() double(f));
row('cellstr_row', @() cellstr(f));
row('num2cell_first', @() cell_first(f, a));
row('arrayfun_objects', @() describe(arrayfun(@(p) p, f, 'UniformOutput', false)));

% --- eq at its edges -------------------------------------------------------------------------------
row('eq_number', @() mat2str(f == 1));
row('eq_text', @() mat2str(f == 'x'));
row('eq_mismatch', @() mat2str(f(:) == f));
row('eq_empty', @() mat2str(f([]) == a));
row('ne_scalar', @() mat2str(f ~= a));
row('eq_square', @() mat2str([f; f] == [f; f]));

% --- growth, member writes, foreign writes ----------------------------------------------------------
row('grow_far', @() grow_far(f, a));
row('member_grow', @() member_grow(f));
row('member_set_one', @() member_set_one(f));
row('member_set_all', @() member_set_all(f));
row('index_assign_same', @() assign_same(f, b));
row('join_number_first', @() describe([1 a]));
row('bracket_list', @() describe([f.Port]));
row('fliplr_row', @() port_one(fliplr(f)));
row('flip_row', @() port_one(flip(f)));

% --- the other transports --------------------------------------------------------------------------
u0 = udpport;
port = u0.LocalPort;
clear u0
echotcpip("on", port);
t1 = tcpclient("127.0.0.1", port);
row('foreign_write', @() foreign_write(f, t1));
row('eq_foreign', @() mat2str(a == t1));
row('eq_foreign_row', @() mat2str(f == t1));
row('tcp_disp', @() show_disp([t1 t1]));
row('udp_grow', @() udp_grow());
clear t1
echotcpip("off");

% --- what holds the ports open ---------------------------------------------------------------------
held = serialportfind;
clear a b f
row('held_after_clear', @() sprintf('%d', numel(serialportfind)));
row('held_ports_open', @() mat2str(isvalid(held)));
clear held
row('released', @() describe(serialportfind));

% --- delete on the row -----------------------------------------------------------------------------
a = serialport("COM20", 9600);
b = serialport("COM21", 9600);
f = [a b];
row('delete_noout', @() delete_noout(f, a, b));
row('disp_deleted', @() show_disp(f));
row('index_deleted', @() class(f(2)));
row('dot_deleted', @() f(2).Port);
clear

function row(name, body)
try
    out = body();
    if ~ischar(out) && ~isstring(out)
        out = class(out);
    end
catch e
    out = ['ERR ' e.identifier ' | ' e.message];
end
fprintf('%s\t%s\n', name, strrep(char(out), newline, ' / '));
end

function text = describe(v)
text = sprintf('%s %s', class(v), mat2str(size(v)));
end

function text = show_disp(v)
text = evalc('disp(v)');
end

function text = show_echo(v) %#ok<INUSD>
text = evalc('v');
end

function text = show_cell(c)
text = sprintf('%s %s %s', describe(c), class(c{1}), strjoin(cellfun(@char, c, 'UniformOutput', false), ','));
end

function text = twice_same(a)
g = a([1 1]);
text = sprintf('%d %d', g(1) == a, g(2) == a);
end

function text = set_row(f)
out = evalc('set(f, ''Timeout'', 3)');
text = sprintf('[%s] %g %g', out, f(1).Timeout, f(2).Timeout);
end

function text = flush_noout(f)
flush(f);
text = 'ran';
end

function text = cfg_noout(f)
configureTerminator(f, "CR");
text = sprintf('%s %s', f(1).Terminator, f(2).Terminator);
end

function text = write_noout(f)
writeline(f, "hi");
text = 'ran';
end

function text = cell_first(f, a)
nc = num2cell(f);
text = sprintf('%s %d', class(nc{1}), nc{1} == a);
end

function text = grow_far(f, a)
g = f;
g(5) = a;
text = describe(g);
end

function text = member_grow(f)
g = f;
g(3).Timeout = 5;
text = describe(g);
end

function text = member_set_one(f)
g = f;
g(2).Timeout = 7;
text = sprintf('%s %g %g %g', describe(g), g(1).Timeout, g(2).Timeout, f(2).Timeout);
end

function text = member_set_all(f)
g = f;
g.Timeout = 9;
text = sprintf('%g %g', f(1).Timeout, f(2).Timeout);
end

function text = assign_same(f, b)
g = f;
g(1) = b;
text = sprintf('%s %s %d', describe(g), char(g(1).Port), g(1) == b);
end

function text = port_one(g)
text = char(g(1).Port);
end

function text = foreign_write(f, t)
g = f;
g(1) = t;
text = describe(g);
end

function text = udp_grow()
u1 = udpport;
u2 = udpport;
g = u1;
g(3) = u2;
text = sprintf('%s %d %d', describe(g), g(2).LocalPort > 0, g(3) == u2);
end

function text = delete_noout(f, a, b)
delete(f);
text = sprintf('%d %d %d', isvalid(a), isvalid(b), numel(serialportfind));
end
