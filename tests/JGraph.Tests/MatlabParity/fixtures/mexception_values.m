% mexception_values.m -- V6 of the value-ownership plan, sixth sub-stage: exception fidelity
% (appendix A #59, #66, #67). An MException is a value: addCause answers a new one and leaves the
% original's causes alone. throw, rethrow and throwAsCaller carry the whole exception - identifier,
% message, causes and stack - and catch hands that same value back; an error records the stack it
% unwound through, rethrow keeps it, throwAsCaller drops the frame that called it.

run_case('m_cause_default', @m_cause_default);
run_case('m_addcause_alias', @m_addcause_alias);
run_case('m_addcause_twice', @m_addcause_twice);
run_case('m_addcause_is_a_value', @m_addcause_is_a_value);
run_case('m_addcause_refuses_a_struct', @m_addcause_refuses_a_struct);
run_case('m_throw_keeps_cause', @m_throw_keeps_cause);
run_case('m_throw_keeps_cause_two_deep', @m_throw_keeps_cause_two_deep);
run_case('m_rethrow_keeps_cause', @m_rethrow_keeps_cause);
run_case('m_cause_after_catch_is_isolated', @m_cause_after_catch_is_isolated);
run_case('m_error_records_stack', @m_error_records_stack);
run_case('m_stack_fields', @m_stack_fields);
run_case('m_stack_line_and_file', @m_stack_line_and_file);
run_case('m_stack_two_frames', @m_stack_two_frames);
run_case('m_rethrow_keeps_stack_top', @m_rethrow_keeps_stack_top);
run_case('m_rethrow_keeps_stack_depth', @m_rethrow_keeps_stack_depth);
run_case('m_throw_built_exception_stack', @m_throw_built_exception_stack);
run_case('m_built_exception_has_no_stack', @m_built_exception_has_no_stack);
run_case('m_throwascaller_stack_top', @m_throwascaller_stack_top);
run_case('m_throwascaller_keeps_identifier', @m_throwascaller_keeps_identifier);
run_case('m_rethrow_keeps_identifier_message', @m_rethrow_keeps_identifier_message);
run_case('m_rethrow_error_struct', @m_rethrow_error_struct);
run_case('m_error_of_exception_keeps_cause', @m_error_of_exception_keeps_cause);
run_case('m_caught_is_mexception', @m_caught_is_mexception);
run_case('m_message_format', @m_message_format);
run_case('m_stack_through_cellfun', @m_stack_through_cellfun);
run_case('m_stack_through_anonymous', @m_stack_through_anonymous);
run_case('m_getreport_has_message', @m_getreport_has_message);
run_case('m_getreport_basic', @m_getreport_basic);
run_case('m_exception_in_cell_alias', @m_exception_in_cell_alias);
run_case('m_stack_is_a_value', @m_stack_is_a_value);

function run_case(name, fn)
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

function thrower()
error('probe:bad', 'bad %d', 7);
end

function middle()
thrower();
end

function throw_built(e)
throw(e);
end

function as_caller()
try
    thrower();
catch inner
    throwAsCaller(inner);
end
end

function calls_as_caller()
as_caller();
end

% --- causes ---------------------------------------------------------------------------------------------

function s = m_cause_default()
e = MException('a:b', 'outer');
s = sprintf('%s %s %d', class(e.cause), mat2str(size(e.cause)), numel(e.cause));
end

function s = m_addcause_alias()
e = MException('a:b', 'outer');
f = addCause(e, MException('c:d', 'inner'));
s = sprintf('%d %d', numel(e.cause), numel(f.cause));
end

function s = m_addcause_twice()
e = MException('a:b', 'outer');
e = addCause(e, MException('c:d', 'one'));
e = addCause(e, MException('e:f', 'two'));
s = sprintf('%d %s %s %s', numel(e.cause), mat2str(size(e.cause)), e.cause{1}.message, e.cause{2}.identifier);
end

function s = m_addcause_is_a_value()
e = MException('a:b', 'outer');
g = e;
g = addCause(g, MException('c:d', 'inner'));
s = sprintf('%d %d %s', numel(e.cause), numel(g.cause), g.message);
end

function s = m_addcause_refuses_a_struct()
e = MException('a:b', 'outer');
e = addCause(e, struct('message', 'm', 'identifier', 'x:y'));
s = sprintf('%d', numel(e.cause));
end

function s = m_throw_keeps_cause()
e = addCause(MException('probe:outer', 'outer'), MException('probe:inner', 'inner'));
try
    throw(e);
catch c
    s = sprintf('%d %s', numel(c.cause), c.cause{1}.identifier);
end
end

function s = m_throw_keeps_cause_two_deep()
inner = addCause(MException('p:mid', 'mid'), MException('p:leaf', 'leaf'));
e = addCause(MException('p:top', 'top'), inner);
try
    try
        throw(e);
    catch first
        rethrow(first);
    end
catch c
    s = sprintf('%s>%s>%s', c.identifier, c.cause{1}.identifier, c.cause{1}.cause{1}.identifier);
end
end

function s = m_rethrow_keeps_cause()
e = addCause(MException('probe:outer', 'outer'), MException('probe:inner', 'inner'));
try
    try
        throw(e);
    catch first
        rethrow(first);
    end
catch c
    s = sprintf('%d %s %s', numel(c.cause), c.identifier, c.cause{1}.message);
end
end

function s = m_cause_after_catch_is_isolated()
e = addCause(MException('probe:outer', 'outer'), MException('probe:inner', 'inner'));
try
    throw(e);
catch c
    c = addCause(c, MException('probe:more', 'more'));
end
s = sprintf('%d %d', numel(e.cause), numel(c.cause));
end

% --- the stack ------------------------------------------------------------------------------------------

function s = m_error_records_stack()
try
    thrower();
catch e
    s = sprintf('%s %d %s', e.stack(1).name, numel(e.stack) >= 2, class(e.stack));
end
end

function s = m_stack_fields()
try
    thrower();
catch e
    s = sprintf('%s %s', strjoin(fieldnames(e.stack)', ','), class(e.stack(1).line));
end
end

function s = m_stack_line_and_file()
try
    thrower();
catch e
    [~, name, ext] = fileparts(e.stack(1).file);
    s = sprintf('%d %s%s', e.stack(1).line, name, ext);
end
end

function s = m_stack_two_frames()
try
    middle();
catch e
    s = sprintf('%s %s', e.stack(1).name, e.stack(2).name);
end
end

function s = m_rethrow_keeps_stack_top()
try
    try
        thrower();
    catch e1
        rethrow(e1);
    end
catch e2
    s = e2.stack(1).name;
end
end

function s = m_rethrow_keeps_stack_depth()
try
    try
        middle();
    catch e1
        rethrow(e1);
    end
catch e2
    s = sprintf('%d %s', numel(e2.stack) == numel(e1.stack), e2.stack(2).name);
end
end

function s = m_throw_built_exception_stack()
e = MException('a:b', 'built');
try
    throw_built(e);
catch c
    s = sprintf('%s %d', c.stack(1).name, numel(e.stack));
end
end

function s = m_built_exception_has_no_stack()
e = MException('a:b', 'built');
s = sprintf('%s %s %d', class(e.stack), mat2str(size(e.stack)), isempty(e.stack));
end

function s = m_throwascaller_stack_top()
try
    calls_as_caller();
catch e
    s = e.stack(1).name;
end
end

function s = m_throwascaller_keeps_identifier()
try
    calls_as_caller();
catch e
    s = sprintf('%s %s', e.identifier, e.message);
end
end

% --- what travels -----------------------------------------------------------------------------------------

function s = m_rethrow_keeps_identifier_message()
try
    try
        thrower();
    catch e1
        rethrow(e1);
    end
catch e2
    s = sprintf('%s %s', e2.identifier, e2.message);
end
end

function s = m_rethrow_error_struct()
try
    rethrow(struct('message', 'from a struct', 'identifier', 'x:y'));
catch e
    s = sprintf('%s %s %s', class(e), e.identifier, e.message);
end
end

function s = m_error_of_exception_keeps_cause()
e = addCause(MException('probe:outer', 'outer'), MException('probe:inner', 'inner'));
try
    error(e);
catch c
    s = sprintf('%s %d', c.identifier, numel(c.cause));
end
end

function s = m_caught_is_mexception()
try
    thrower();
catch e
    s = sprintf('%s %d %d', class(e), isa(e, 'MException'), isscalar(e));
end
end

function s = m_message_format()
e = MException('a:b', 'v=%d and %s', 5, 'text');
s = e.message;
end

function s = m_stack_through_cellfun()
try
    cellfun(@(k) thrower(), {1});
catch e
    s = sprintf('%s %s', e.identifier, e.stack(1).name);
end
end

function s = m_stack_through_anonymous()
f = @() thrower();
try
    f();
catch e
    s = sprintf('%s %d', e.stack(1).name, numel(e.stack) >= 2);
end
end

function s = m_getreport_has_message()
try
    thrower();
catch e
    r = getReport(e);
    s = sprintf('%d %d', ischar(r), contains(r, 'bad 7'));
end
end

function s = m_getreport_basic()
e = MException('a:b', 'outer');
s = getReport(e, 'basic');
end

function s = m_exception_in_cell_alias()
e = MException('a:b', 'outer');
c = {e}; d = c;
d{1} = addCause(d{1}, MException('c:d', 'inner'));
s = sprintf('%d %d', numel(c{1}.cause), numel(d{1}.cause));
end

function s = m_stack_is_a_value()
try
    thrower();
catch e
    st = e.stack;
    st(1).name = 'changed';
    s = sprintf('%s %s', e.stack(1).name, st(1).name);
end
end
