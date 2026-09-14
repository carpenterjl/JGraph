% value_isolation_events.m -- appendix A of the value-ownership plan: events and listeners. A
% listener capturing a value later written, a listener writing a global the caller holds as an
% operand, the event's name, a listener surviving clear when made by addlistener, disabling and
% deleting a listener (#106, V6), and a listener object's lifetime ending with its last handle
% (#107, V10). JGraph's parser refuses an events block, which stops the whole run at the first
% construction: the recording carries a RUN line pending V6.

run_case('a106_listener_capture', @a106_listener_capture);
run_case('a106_listener_writes_global_operand', @a106_listener_writes_global_operand);
run_case('a106_listener_event_name', @a106_listener_event_name);
run_case('a107_listener_fn_lifetime', @a107_listener_fn_lifetime);
run_case('a106_addlistener_survives_clear', @a106_addlistener_survives_clear);
run_case('a106_listener_disabled', @a106_listener_disabled);
run_case('a106_listener_delete_isvalid', @a106_listener_delete_isvalid);

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

function store_ev(x)
global ev_out
ev_out = x;
end

function s = a106_listener_capture()
global ev_out
ev_out = [];
b = EventBox();
v = [1 2 3];
lh = addlistener(b, 'Changed', @(~, ~) store_ev(v)); %#ok<NASGU>
v(1) = 7;
b.fire();
s = mat2str(ev_out);
end

function bump_ev()
global g_ev
g_ev(1) = 7;
end

function z = fire_zero(b)
b.fire();
z = 0;
end

function s = a106_listener_writes_global_operand()
global g_ev
g_ev = [1 2 3];
b = EventBox();
lh = addlistener(b, 'Changed', @(~, ~) bump_ev()); %#ok<NASGU>
r = g_ev + fire_zero(b);
s = sprintf('%s %s', mat2str(r), mat2str(g_ev));
end

function s = a106_listener_event_name()
global ev_out
ev_out = '';
b = EventBox();
lh = addlistener(b, 'Changed', @(src, evt) store_ev(evt.EventName)); %#ok<NASGU>
b.fire();
s = ev_out;
end

function s = a107_listener_fn_lifetime()
b = EventBox();
lh = listener(b, 'Changed', @(~, ~) vlog('L')); %#ok<NASGU>
b.fire();
clear lh
b.fire();
s = logged_text();
end

function s = a106_addlistener_survives_clear()
b = EventBox();
lh = addlistener(b, 'Changed', @(~, ~) vlog('A')); %#ok<NASGU>
clear lh
b.fire();
s = logged_text();
end

function s = a106_listener_disabled()
b = EventBox();
lh = addlistener(b, 'Changed', @(~, ~) vlog('D'));
lh.Enabled = false;
b.fire();
s = ['[' logged_text() ']'];
end

function s = a106_listener_delete_isvalid()
b = EventBox();
lh = addlistener(b, 'Changed', @(~, ~) []);
delete(lh);
s = sprintf('%d', isvalid(lh));
end
