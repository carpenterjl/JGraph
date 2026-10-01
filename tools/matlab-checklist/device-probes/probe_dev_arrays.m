% probe_dev_arrays.m -- what R2025b does with arrays of device objects (device classes plan, stage
% D13, ADR 0197): the row serialportfind answers and the brackets that make one. Needs the com0com
% pair COM20<->COM21 and nothing else; it opens no real device.
a = serialport("COM20", 9600);
b = serialport("COM21", 9600);
row('find', @() describe(serialportfind));
f = [a b];
row('hcat', @() describe(f));
row('vcat', @() describe([a; b]));
row('hcat3', @() describe([a b a]));
row('hcat_row_scalar', @() describe([f a]));
row('vcat_rows', @() describe([f; f]));
row('transpose', @() describe(f'));
row('empty_join', @() [describe([a []]) ' / ' describe([[] a])]);
row('shape', @() sprintf('%d %d %d %d %d %d %d %d', isempty(f), isscalar(f), isrow(f), isvector(f), iscolumn(f), ismatrix(f), ndims(f), length(f)));
row('index_scalar', @() sprintf('%s %s %s', f(2).Port, f(end).Port, describe(f(1))));
row('index_vector', @() [describe(f([2 1])) ' ' port_of(f([2 1]), 1)]);
row('index_range', @() describe(f(2:end)));
row('index_colon', @() describe(f(:)));
row('index_logical', @() describe(f([false true])));
row('index_two', @() describe(f(1, 2)));
row('index_out', @() describe(f(3)));
row('index_zero', @() describe(f(0)));
row('index_empty', @() describe(f([])));
row('loop', @() loop_ports(f));
row('loop_column', @() loop_ports([a; b]));
row('dot_on_row', @() f.Port);
row('dot_list', @() numel({f.Port}));
row('method_on_row', @() flush(f));
row('isvalid', @() mat2str(isvalid(f)));
row('eq', @() mat2str(f == a));
row('eq_rows', @() mat2str(f == f));
row('isequal', @() sprintf('%d %d %d', isequal(f(1), a), isequal(f, [a b]), isequal(f, [b a])));
row('join_number', @() describe([a 1]));
row('join_text', @() describe([a 'x']));
row('join_struct', @() describe([a struct('x', 1)]));
row('join_cell', @() describe([{a} {b}]));
row('same_twice', @() describe([a a]));
row('class', @() sprintf('%s %d %d', class(f), isa(f, 'handle'), isobject(f)));
row('numel_find_one', @() describe(serialportfind(Port="COM20")));
row('get_on_row', @() class(get(f, 'Port')));
row('set_on_row', @() set(f, 'Timeout', 5));
row('timeouts', @() sprintf('%g %g', a.Timeout, b.Timeout));
row('assign_grow', @() grow(a, b));
row('assign_delete', @() shrink(f));
row('end_arith', @() f(end - 1).Port);
row('cellfun', @() strjoin(arrayfun(@(p) p.Port, f), ','));
row('num2cell', @() describe(num2cell(f)));
row('repmat', @() describe(repmat(a, 1, 3)));
row('horzcat_fn', @() describe(horzcat(a, b)));
row('vertcat_fn', @() describe(vertcat(a, b)));
row('cat_fn', @() describe(cat(2, a, b)));
row('reshape', @() describe(reshape(f, 2, 1)));
row('numel_fn', @() sprintf('%d %s', numel(f), mat2str(size(f))));
row('size_dims', @() sprintf('%d %d', size(f, 1), size(f, 2)));
row('size_two_out', @() two_out(f));

% The other transports, on the loopback.
u0 = udpport;
port = u0.LocalPort;
clear u0
echotcpip("on", port);
t1 = tcpclient("127.0.0.1", port);
t2 = tcpclient("127.0.0.1", port);
row('tcp_hcat', @() describe([t1 t2]));
row('tcp_find', @() describe(tcpclientfind));
u1 = udpport;
u2 = udpport;
row('udp_hcat', @() describe([u1 u2]));
row('mixed', @() describe([a t1]));
row('mixed_udp', @() describe([t1 u1]));
clear t1 t2 u1 u2
echotcpip("off");

delete(b);
row('isvalid_after_delete', @() mat2str(isvalid(f)));
row('delete_row', @() delete(f));
row('isvalid_after', @() sprintf('%d %d', isvalid(a), isvalid(b)));
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

function text = port_of(v, k)
text = char(v(k).Port);
end

function text = loop_ports(f)
text = '';
for p = f
    text = [text sprintf('[%s %s]', describe(p), strjoin(arrayfun(@(q) char(q.Port), p, 'UniformOutput', false), '+'))]; %#ok<AGROW>
end
end

function text = grow(a, b)
g = a;
g(3) = b;
text = describe(g);
end

function text = shrink(f)
f(1) = [];
text = [describe(f) ' ' char(f(1).Port)];
end

function text = two_out(f)
[r, c] = size(f);
text = sprintf('%d %d', r, c);
end
