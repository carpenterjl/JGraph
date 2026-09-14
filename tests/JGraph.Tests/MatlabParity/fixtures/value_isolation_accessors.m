% value_isolation_accessors.m -- appendix A of the value-ownership plan: property accessors. A set
% method storing a value later written (#27), and the order R2025b calls getter, setter, subscripts,
% end and the right-hand side in a composite write through a property with get.p/set.p, on a value
% class and a handle class, standing alone and held in a struct field or a cell (#147, the order
% V6's write-back rule follows), and a Dependent property read through get.q (#28). JGraph refuses
% set.p/get.p and the Dependent attribute in a class file, which stops the whole run at the first
% construction: the recording carries a RUN line pending V6, and every case is owned by V6.

run_case('a028_dependent_get_read', @a028_dependent_get_read);
run_case('a027_set_method_store', @a027_set_method_store);
run_case('a147_acc_end_write', @a147_acc_end_write);
run_case('a147_acc_end_growth', @a147_acc_end_growth);
run_case('a147_acc_subscript_and_rhs', @a147_acc_subscript_and_rhs);
run_case('a147_acc_subscript_range_to_end', @a147_acc_subscript_range_to_end);
run_case('a147_acc_handle_indexed_write', @a147_acc_handle_indexed_write);
run_case('a147_acc_handle_end_write', @a147_acc_handle_end_write);
run_case('a147_acc_read_end', @a147_acc_read_end);
run_case('a147_acc_compound_whole', @a147_acc_compound_whole);
run_case('a147_acc_struct_held_handle_end', @a147_acc_struct_held_handle_end);
run_case('a147_acc_cell_held_handle_end', @a147_acc_cell_held_handle_end);

function run_case(name, fn)
global vlog_text
vlog_text = '';
try
    fprintf('CHK|%s|%s|exact\n', name, clean(fn()));
catch err
    fprintf('CHK|%s|ERR %s|exact\n', name, clean(err.message));
end
end

function s = clean(s)
if isnumeric(s) || islogical(s)
    s = mat2str(s);
end
s = char(s);
s = strrep(s, char(13), '');
s = strrep(s, char(10), ' ');
s = strrep(s, '|', '/');
end

function s = logged(v)
global vlog_text
if isnumeric(v) || islogical(v)
    v = mat2str(v);
end
s = sprintf('%s / %s', vlog_text, v);
end

function k = idx_l()
vlog('idx');
k = 2;
end

function v = rhs_l()
vlog('rhs');
v = 9;
end

function s = a028_dependent_get_read()
b = DepBox(); x = b.q; b.p(1) = 7;
s = sprintf('%d %d', x(1), b.q(1));
end

function s = a027_set_method_store()
v = ones(1, 3); b = SetBox(); b.p = v; v(1) = 7;
s = num2str(b.p(1));
end

function s = a147_acc_end_write()
o = LogBox();
vlog('start');
o.p(end) = rhs_l();
s = logged(o.p);
end

function s = a147_acc_end_growth()
o = LogBox();
vlog('start');
o.p(end + 1) = rhs_l();
s = logged(o.p);
end

function s = a147_acc_subscript_and_rhs()
o = LogBox();
vlog('start');
o.p(idx_l()) = rhs_l();
s = logged(o.p);
end

function s = a147_acc_subscript_range_to_end()
o = LogBox();
vlog('start');
o.p(idx_l():end) = rhs_l();
s = logged(o.p);
end

function s = a147_acc_handle_indexed_write()
h = LogHBox();
vlog('start');
h.p(idx_l()) = rhs_l();
s = logged(h.p);
end

function s = a147_acc_handle_end_write()
h = LogHBox();
vlog('start');
h.p(end) = rhs_l();
s = logged(h.p);
end

function s = a147_acc_read_end()
o = LogBox();
vlog('start');
x = o.p(end);
s = logged(x);
end

function s = a147_acc_compound_whole()
o = LogBox();
vlog('start');
o.p = o.p + rhs_l();
s = logged(o.p);
end

function s = a147_acc_struct_held_handle_end()
st.h = LogHBox();
vlog('start');
st.h.p(end) = rhs_l();
s = logged(st.h.p);
end

function s = a147_acc_cell_held_handle_end()
c = {LogHBox()};
vlog('start');
c{1}.p(end) = rhs_l();
s = logged(c{1}.p);
end
