% value_isolation_observable.m -- appendix A of the value-ownership plan: an observable property.
% A PostSet listener seeing an indexed write, and a value read out of the property staying its
% own (#108, V6). JGraph refuses the SetObservable attribute, which stops the whole run at the
% first construction: the recording carries a RUN line pending V6.

run_case('a108_postset_sees_indexed_write', @a108_postset_sees_indexed_write);
run_case('a108_postset_value_alias', @a108_postset_value_alias);

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

function s = logged_text()
global vlog_text
s = vlog_text;
end

function s = a108_postset_sees_indexed_write()
o = ObsBox();
lh = addlistener(o, 'data', 'PostSet', @(src, evt) vlog(mat2str(evt.AffectedObject.data))); %#ok<NASGU>
o.data(2) = 9;
s = logged_text();
end

function s = a108_postset_value_alias()
o = ObsBox();
v = o.data;
o.data(1) = 5;
s = sprintf('%s %s', mat2str(v), mat2str(o.data));
end
