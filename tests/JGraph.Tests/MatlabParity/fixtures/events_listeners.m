% events_listeners.m -- V6 of the value-ownership plan: events and listeners (appendix A #106) and
% observable properties (#108). The a106/a108 lines in value_isolation_events and
% value_isolation_observable hold the ownership questions; these hold the forms around them: the
% order a notify runs its listeners in (newest first), the event's fields, custom event data, a
% failing callback reported and passed over, Recursive, Enabled, delete and isvalid, the events
% list, ObjectBeingDestroyed, every refusal in R2025b's words, and the PreSet/PostSet a property
% write raises -- once for each indexed write, before and after, to the same value, through a
% method, on a nested struct. Each case makes its own objects, so no listener outlives its case; a
% failing callback's warning goes to the error stream, which the recording does not read.

run_case('h_ev_notify_order', @h_ev_notify_order);
run_case('h_ev_two_listeners_newest_first', @h_ev_two_listeners_newest_first);
run_case('h_ev_other_event_untouched', @h_ev_other_event_untouched);
run_case('h_ev_evt_fields', @h_ev_evt_fields);
run_case('h_ev_lh_props', @h_ev_lh_props);
run_case('h_ev_custom_data', @h_ev_custom_data);
run_case('h_ev_bad_data', @h_ev_bad_data);
run_case('h_ev_unknown_event_notify', @h_ev_unknown_event_notify);
run_case('h_ev_unknown_event_listen', @h_ev_unknown_event_listen);
run_case('h_ev_case_sensitive_notify', @h_ev_case_sensitive_notify);
run_case('h_ev_listen_struct', @h_ev_listen_struct);
run_case('h_ev_listen_number', @h_ev_listen_number);
run_case('h_ev_callback_error_continues', @h_ev_callback_error_continues);
run_case('h_ev_recursive_off', @h_ev_recursive_off);
run_case('h_ev_recursive_on', @h_ev_recursive_on);
run_case('h_ev_disable_reenable', @h_ev_disable_reenable);
run_case('h_ev_delete_source_listener_valid', @h_ev_delete_source_listener_valid);
run_case('h_ev_delete_lh_twice', @h_ev_delete_lh_twice);
run_case('h_ev_notify_after_delete_source', @h_ev_notify_after_delete_source);
run_case('h_ev_events_list', @h_ev_events_list);
run_case('h_ev_events_list_name', @h_ev_events_list_name);
run_case('h_ev_events_struct', @h_ev_events_struct);
run_case('h_ev_callback_writes_source', @h_ev_callback_writes_source);
run_case('h_ev_count_bump_order', @h_ev_count_bump_order);
run_case('h_ev_listener_string_callback', @h_ev_listener_string_callback);
run_case('h_ev_listener_cell_callback', @h_ev_listener_cell_callback);
run_case('h_ev_listener_named', @h_ev_listener_named);
run_case('h_ev_two_sources', @h_ev_two_sources);
run_case('h_ev_zero_arg_callback', @h_ev_zero_arg_callback);
run_case('h_ev_listener_fn_then_delete', @h_ev_listener_fn_then_delete);
run_case('h_ev_lh_enabled_bad', @h_ev_lh_enabled_bad);
run_case('h_ev_lh_enabled_numeric', @h_ev_lh_enabled_numeric);
run_case('h_ev_lh_enabled_vector', @h_ev_lh_enabled_vector);
run_case('h_ev_lh_recursive_text', @h_ev_lh_recursive_text);
run_case('h_ev_lh_callback_reassign', @h_ev_lh_callback_reassign);
run_case('h_ev_lh_callback_bad', @h_ev_lh_callback_bad);
run_case('h_ev_lh_unknown_prop', @h_ev_lh_unknown_prop);
run_case('h_ev_lh_deleted_prop', @h_ev_lh_deleted_prop);
run_case('h_ev_lh_deleted_write', @h_ev_lh_deleted_write);
run_case('h_ev_notify_bare_class', @h_ev_notify_bare_class);
run_case('h_ev_destroyed_event', @h_ev_destroyed_event);
run_case('h_ev_destroyed_event_method_delete', @h_ev_destroyed_event_method_delete);
run_case('h_ev_notify_destroyed_by_hand', @h_ev_notify_destroyed_by_hand);
run_case('h_ev_listen_deleted_source', @h_ev_listen_deleted_source);
run_case('h_ev_notify_too_few', @h_ev_notify_too_few);
run_case('h_ev_notify_string_name', @h_ev_notify_string_name);
run_case('h_ev_clear_source_keeps_listener', @h_ev_clear_source_keeps_listener);
run_case('h_ev_lh_predicates', @h_ev_lh_predicates);
run_case('h_ev_isa_handle_lh', @h_ev_isa_handle_lh);
run_case('h_ev_addlistener_output_optional', @h_ev_addlistener_output_optional);
run_case('h_ev_listener_source_shared', @h_ev_listener_source_shared);
run_case('h_ev_listener_capture_snapshot', @h_ev_listener_capture_snapshot);
run_case('h_ps_order', @h_ps_order);
run_case('h_ps_preset_sees_old', @h_ps_preset_sees_old);
run_case('h_ps_postset_sees_new', @h_ps_postset_sees_new);
run_case('h_ps_evt_fields', @h_ps_evt_fields);
run_case('h_ps_same_value', @h_ps_same_value);
run_case('h_ps_indexed_once_each', @h_ps_indexed_once_each);
run_case('h_ps_via_method', @h_ps_via_method);
run_case('h_ps_not_observable', @h_ps_not_observable);
run_case('h_ps_unknown_prop', @h_ps_unknown_prop);
run_case('h_ps_bad_kind', @h_ps_bad_kind);
run_case('h_ps_other_prop', @h_ps_other_prop);
run_case('h_ps_recursive_guard', @h_ps_recursive_guard);
run_case('h_ps_lh_class', @h_ps_lh_class);
run_case('h_ps_delete_lh', @h_ps_delete_lh);
run_case('h_ps_cell_props', @h_ps_cell_props);
run_case('h_ps_growth', @h_ps_growth);
run_case('h_ps_preset_error_continues', @h_ps_preset_error_continues);
run_case('h_ps_in_ctor_none', @h_ps_in_ctor_none);
run_case('h_ps_proplistener_disabled', @h_ps_proplistener_disabled);
run_case('h_ps_proplistener_props', @h_ps_proplistener_props);
run_case('h_ps_notify_in_postset', @h_ps_notify_in_postset);
run_case('h_ps_preset_then_postset_order', @h_ps_preset_then_postset_order);
run_case('h_ps_alias_write', @h_ps_alias_write);
run_case('h_ps_nested_struct_in_prop', @h_ps_nested_struct_in_prop);
run_case('h_ps_nested_struct_whole', @h_ps_nested_struct_whole);

function run_case(name, fn)
global vlog_text ev_depth
vlog_text = '';
ev_depth = 0;
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

function named_cb(~, ~)
vlog('N');
end

function setcount(src)
src.count = 99;
end

function nested_notify(src)
global ev_depth
ev_depth = ev_depth + 1;
if ev_depth < 3
    notify(src, 'Changed');
end
vlog(num2str(ev_depth));
end

function guarded_set(evt)
global ev_depth
ev_depth = ev_depth + 1;
if ev_depth < 3
    evt.AffectedObject.a = 0;
end
vlog(num2str(ev_depth));
end

% ---- events ------------------------------------------------------------------------------------

function s = h_ev_notify_order()
b = EventPair();
lh = addlistener(b, 'Changed', @(~, ~) vlog('L')); %#ok<NASGU>
vlog('a'); b.fire('Changed'); vlog('b');
s = logged_text();
end

function s = h_ev_two_listeners_newest_first()
b = EventPair();
lh1 = addlistener(b, 'Changed', @(~, ~) vlog('one')); %#ok<NASGU>
lh2 = addlistener(b, 'Changed', @(~, ~) vlog('two')); %#ok<NASGU>
b.fire('Changed');
s = logged_text();
end

function s = h_ev_other_event_untouched()
b = EventPair();
lh = addlistener(b, 'Changed', @(~, ~) vlog('L')); %#ok<NASGU>
b.fire('Other');
s = ['[' logged_text() ']'];
end

function s = h_ev_evt_fields()
b = EventPair();
lh = addlistener(b, 'Changed', @(src, evt) vlog([class(evt) '/' evt.EventName '/' class(evt.Source) '/' num2str(src == evt.Source)])); %#ok<NASGU>
b.fire('Changed');
s = logged_text();
end

function s = h_ev_lh_props()
b = EventPair();
lh = addlistener(b, 'Changed', @(~, ~) []);
s = sprintf('%s %s %d %d %s %s %d %d', class(lh), lh.EventName, lh.Enabled, lh.Recursive, class(lh.Callback), class(lh.Source), numel(lh.Source), lh.Source{1} == b);
end

function s = h_ev_custom_data()
b = EventPair();
lh = addlistener(b, 'Changed', @(~, evt) vlog([num2str(evt.n) '/' evt.EventName '/' class(evt) '/' class(evt.Source)])); %#ok<NASGU>
notify(b, 'Changed', TickData(5));
s = logged_text();
end

function s = h_ev_bad_data()
b = EventPair();
notify(b, 'Changed', 5);
s = 'no error';
end

function s = h_ev_unknown_event_notify()
b = EventPair();
notify(b, 'Nope');
s = 'no error';
end

function s = h_ev_unknown_event_listen()
b = EventPair();
addlistener(b, 'Nope', @(~, ~) []);
s = 'no error';
end

function s = h_ev_case_sensitive_notify()
b = EventPair();
lh = addlistener(b, 'Changed', @(~, ~) vlog('L')); %#ok<NASGU>
notify(b, 'changed');
s = ['[' logged_text() ']'];
end

function s = h_ev_listen_struct()
addlistener(struct('a', 1), 'a', @(~, ~) []);
s = 'no error';
end

function s = h_ev_listen_number()
addlistener(5, 'a', @(~, ~) []);
s = 'no error';
end

function s = h_ev_callback_error_continues()
b = EventPair();
lh = addlistener(b, 'Changed', @(~, ~) error('probe:boom', 'boom')); %#ok<NASGU>
lh2 = addlistener(b, 'Changed', @(~, ~) vlog('after')); %#ok<NASGU>
vlog('a');
try
    b.fire('Changed');
    vlog('b');
catch err
    vlog(['ERR ' err.message]);
end
s = logged_text();
end

function s = h_ev_recursive_off()
b = EventPair();
lh = addlistener(b, 'Changed', @(src, ~) nested_notify(src)); %#ok<NASGU>
b.fire('Changed');
s = logged_text();
end

function s = h_ev_recursive_on()
b = EventPair();
lh = addlistener(b, 'Changed', @(src, ~) nested_notify(src));
lh.Recursive = true;
b.fire('Changed');
s = logged_text();
end

function s = h_ev_disable_reenable()
b = EventPair();
lh = addlistener(b, 'Changed', @(~, ~) vlog('D'));
lh.Enabled = false;
b.fire('Changed');
lh.Enabled = true;
b.fire('Changed');
s = [logged_text() ' ' class(lh.Enabled)];
end

function s = h_ev_delete_source_listener_valid()
b = EventPair();
lh = addlistener(b, 'Changed', @(~, ~) []);
delete(b);
s = sprintf('%d %d', isvalid(lh), isvalid(b));
end

function s = h_ev_delete_lh_twice()
b = EventPair();
lh = addlistener(b, 'Changed', @(~, ~) vlog('L'));
delete(lh);
delete(lh);
b.fire('Changed');
s = sprintf('%d [%s]', isvalid(lh), logged_text());
end

function s = h_ev_notify_after_delete_source()
b = EventPair();
delete(b);
notify(b, 'Changed');
s = 'no error';
end

function s = h_ev_events_list()
b = EventPair();
c = events(b);
s = [class(c) ' ' strjoin(c', ',') ' ' mat2str(size(c))];
end

function s = h_ev_events_list_name()
b = EventPair(); %#ok<NASGU>
c = events('EventPair');
s = strjoin(c', ',');
end

function s = h_ev_events_struct()
c = events(struct('a', 1));
s = sprintf('%s %s', class(c), mat2str(size(c)));
end

function s = h_ev_callback_writes_source()
b = EventPair();
lh = addlistener(b, 'Changed', @(src, ~) setcount(src)); %#ok<NASGU>
b.fire('Changed');
s = sprintf('%d', b.count);
end

function s = h_ev_count_bump_order()
b = EventPair();
lh = addlistener(b, 'Changed', @(src, ~) vlog(num2str(src.count))); %#ok<NASGU>
b.bump(); b.bump();
s = logged_text();
end

function s = h_ev_listener_string_callback()
b = EventPair();
addlistener(b, 'Changed', 'disp(1)');
s = 'no error';
end

function s = h_ev_listener_cell_callback()
b = EventPair();
addlistener(b, 'Changed', {@named_cb, 1});
s = 'no error';
end

function s = h_ev_listener_named()
b = EventPair();
lh = addlistener(b, 'Changed', @named_cb); %#ok<NASGU>
b.fire('Changed');
s = logged_text();
end

function s = h_ev_two_sources()
b1 = EventPair();
b2 = EventPair();
lh = addlistener(b1, 'Changed', @(~, ~) vlog('one')); %#ok<NASGU>
b2.fire('Changed');
b1.fire('Changed');
s = logged_text();
end

function s = h_ev_zero_arg_callback()
b = EventPair();
lh = addlistener(b, 'Changed', @() vlog('z')); %#ok<NASGU>
b.fire('Changed');
vlog('after');
s = logged_text();
end

function s = h_ev_listener_fn_then_delete()
b = EventPair();
lh = listener(b, 'Changed', @(~, ~) vlog('L'));
b.fire('Changed');
delete(lh);
b.fire('Changed');
s = sprintf('%s %d', logged_text(), isvalid(lh));
end

function s = h_ev_lh_enabled_bad()
b = EventPair();
lh = addlistener(b, 'Changed', @(~, ~) []);
lh.Enabled = 'maybe';
s = 'no error';
end

function s = h_ev_lh_enabled_numeric()
b = EventPair();
lh = addlistener(b, 'Changed', @(~, ~) vlog('E'));
lh.Enabled = 0;
b.fire('Changed');
s = sprintf('[%s] %s %d', logged_text(), class(lh.Enabled), lh.Enabled);
end

function s = h_ev_lh_enabled_vector()
b = EventPair();
lh = addlistener(b, 'Changed', @(~, ~) []);
lh.Enabled = [true false];
s = 'no error';
end

function s = h_ev_lh_recursive_text()
b = EventPair();
lh = addlistener(b, 'Changed', @(~, ~) []);
lh.Recursive = 'yes';
s = 'no error';
end

function s = h_ev_lh_callback_reassign()
b = EventPair();
lh = addlistener(b, 'Changed', @(~, ~) vlog('one'));
lh.Callback = @(~, ~) vlog('two');
b.fire('Changed');
s = logged_text();
end

function s = h_ev_lh_callback_bad()
b = EventPair();
lh = addlistener(b, 'Changed', @(~, ~) []);
lh.Callback = 5;
s = 'no error';
end

function s = h_ev_lh_unknown_prop()
b = EventPair();
lh = addlistener(b, 'Changed', @(~, ~) []);
lh.Foo = 1;
s = 'no error';
end

function s = h_ev_lh_deleted_prop()
b = EventPair();
lh = addlistener(b, 'Changed', @(~, ~) []);
delete(lh);
x = lh.Enabled; %#ok<NASGU>
s = 'no error';
end

function s = h_ev_lh_deleted_write()
b = EventPair();
lh = addlistener(b, 'Changed', @(~, ~) []);
delete(lh);
lh.Enabled = true;
s = 'no error';
end

function s = h_ev_notify_bare_class()
notify(EventPair, 'Changed');
s = 'no error';
end

function s = h_ev_destroyed_event()
b = EventPair();
lh = addlistener(b, 'ObjectBeingDestroyed', @(src, evt) vlog([evt.EventName '/' num2str(isvalid(src))])); %#ok<NASGU>
vlog('a');
delete(b);
vlog('b');
s = sprintf('%s %d', logged_text(), isvalid(b));
end

function s = h_ev_destroyed_event_method_delete()
b = EventPair();
lh = addlistener(b, 'ObjectBeingDestroyed', @(~, ~) vlog('gone')); %#ok<NASGU>
b.delete();
s = logged_text();
end

function s = h_ev_notify_destroyed_by_hand()
b = EventPair();
lh = addlistener(b, 'ObjectBeingDestroyed', @(~, ~) vlog('gone')); %#ok<NASGU>
notify(b, 'ObjectBeingDestroyed');
s = 'no error';
end

function s = h_ev_listen_deleted_source()
b = EventPair();
delete(b);
addlistener(b, 'Changed', @(~, ~) []);
s = 'no error';
end

function s = h_ev_notify_too_few()
b = EventPair();
notify(b);
s = 'no error';
end

function s = h_ev_notify_string_name()
b = EventPair();
lh = addlistener(b, "Changed", @(~, ~) vlog('S')); %#ok<NASGU>
notify(b, "Changed");
s = logged_text();
end

function s = h_ev_clear_source_keeps_listener()
b = EventPair();
c = b;
lh = addlistener(b, 'Changed', @(~, ~) vlog('K')); %#ok<NASGU>
clear b
c.fire('Changed');
s = logged_text();
end

function s = h_ev_lh_predicates()
b = EventPair();
lh = addlistener(b, 'Changed', @(~, ~) []);
s = sprintf('%d %d %d', isstruct(lh), ishandle(lh), isvalid(lh));
end

function s = h_ev_isa_handle_lh()
b = EventPair();
lh = addlistener(b, 'Changed', @(~, ~) []);
s = sprintf('%d %d %d', isa(lh, 'handle'), isa(lh, 'event.listener'), isobject(lh));
end

function s = h_ev_addlistener_output_optional()
b = EventPair();
addlistener(b, 'Changed', @(~, ~) vlog('K'));
b.fire('Changed');
s = logged_text();
end

function s = h_ev_listener_source_shared()
b = EventPair();
c = b;
lh = addlistener(c, 'Changed', @(~, ~) vlog('S')); %#ok<NASGU>
b.fire('Changed');
s = logged_text();
end

function s = h_ev_listener_capture_snapshot()
b = EventPair();
v = [1 2 3];
lh = addlistener(b, 'Changed', @(~, ~) vlog(mat2str(v))); %#ok<NASGU>
v(1) = 9;
b.fire('Changed');
s = logged_text();
end

% ---- PreSet and PostSet ------------------------------------------------------------------------

function s = h_ps_order()
o = ObsPair();
lh = addlistener(o, 'a', 'PostSet', @(~, ~) vlog('P')); %#ok<NASGU>
vlog('a'); o.a = [4 5]; vlog('b');
s = logged_text();
end

function s = h_ps_preset_sees_old()
o = ObsPair();
lh = addlistener(o, 'a', 'PreSet', @(~, evt) vlog(mat2str(evt.AffectedObject.a))); %#ok<NASGU>
o.a = 9;
s = [logged_text() ' ' mat2str(o.a)];
end

function s = h_ps_postset_sees_new()
o = ObsPair();
lh = addlistener(o, 'a', 'PostSet', @(~, evt) vlog(mat2str(evt.AffectedObject.a))); %#ok<NASGU>
o.a = 9;
s = logged_text();
end

function s = h_ps_evt_fields()
o = ObsPair();
lh = addlistener(o, 'a', 'PostSet', @(src, evt) vlog([class(evt) '/' evt.EventName '/' evt.Source.Name '/' class(evt.Source) '/' class(src) '/' num2str(evt.AffectedObject == o)])); %#ok<NASGU>
o.a = 1;
s = logged_text();
end

function s = h_ps_same_value()
o = ObsPair();
lh = addlistener(o, 'a', 'PostSet', @(~, ~) vlog('P')); %#ok<NASGU>
o.a = o.a;
s = ['[' logged_text() ']'];
end

function s = h_ps_indexed_once_each()
o = ObsPair();
lh = addlistener(o, 'a', 'PostSet', @(~, ~) vlog('P')); %#ok<NASGU>
o.a(2) = 9;
o.a(end + 1) = 4;
s = [logged_text() ' ' mat2str(o.a)];
end

function s = h_ps_via_method()
o = ObsPair();
lh = addlistener(o, 'a', 'PostSet', @(~, evt) vlog(mat2str(evt.AffectedObject.a))); %#ok<NASGU>
o.setA(7);
s = logged_text();
end

function s = h_ps_not_observable()
o = ObsPair();
addlistener(o, 'plain', 'PostSet', @(~, ~) []);
s = 'no error';
end

function s = h_ps_unknown_prop()
o = ObsPair();
addlistener(o, 'nope', 'PostSet', @(~, ~) []);
s = 'no error';
end

function s = h_ps_bad_kind()
o = ObsPair();
addlistener(o, 'a', 'PostFoo', @(~, ~) []);
s = 'no error';
end

function s = h_ps_other_prop()
o = ObsPair();
lh = addlistener(o, 'b', 'PostSet', @(~, ~) vlog('B')); %#ok<NASGU>
o.a = 5;
o.b = 5;
s = logged_text();
end

function s = h_ps_recursive_guard()
o = ObsPair();
lh = addlistener(o, 'a', 'PostSet', @(~, evt) guarded_set(evt)); %#ok<NASGU>
o.a = 1;
s = logged_text();
end

function s = h_ps_lh_class()
o = ObsPair();
lh = addlistener(o, 'a', 'PostSet', @(~, ~) []);
s = sprintf('%s %s %s %s %d', class(lh), lh.EventName, class(lh.Source), class(lh.Object), lh.Object{1} == o);
end

function s = h_ps_delete_lh()
o = ObsPair();
lh = addlistener(o, 'a', 'PostSet', @(~, ~) vlog('P'));
o.a = 1;
delete(lh);
o.a = 2;
s = logged_text();
end

function s = h_ps_cell_props()
o = ObsPair();
lh = addlistener(o, {'a', 'b'}, 'PostSet', @(~, evt) vlog(evt.Source.Name)); %#ok<NASGU>
o.a = 1;
o.b = 2;
s = logged_text();
end

function s = h_ps_growth()
o = ObsPair();
lh = addlistener(o, 'a', 'PostSet', @(~, evt) vlog(mat2str(evt.AffectedObject.a))); %#ok<NASGU>
o.a(5) = 1;
s = logged_text();
end

function s = h_ps_preset_error_continues()
o = ObsPair();
lh = addlistener(o, 'a', 'PreSet', @(~, ~) error('probe:veto', 'veto')); %#ok<NASGU>
try
    o.a = 9;
    vlog('after');
catch err
    vlog(['ERR ' err.message]);
end
s = [logged_text() ' ' mat2str(o.a)];
end

function s = h_ps_in_ctor_none()
o = ObsPair();
lh = addlistener(o, 'a', 'PostSet', @(~, ~) vlog('P')); %#ok<NASGU>
o2 = ObsPair(); %#ok<NASGU>
s = ['[' logged_text() ']'];
end

function s = h_ps_proplistener_disabled()
o = ObsPair();
lh = addlistener(o, 'a', 'PostSet', @(~, ~) vlog('P'));
lh.Enabled = false;
o.a = 1;
lh.Enabled = true;
o.a = 2;
s = logged_text();
end

function s = h_ps_proplistener_props()
o = ObsPair();
lh = addlistener(o, 'a', 'PostSet', @(~, ~) []);
s = sprintf('%s %d %d %s %d %d', lh.Source{1}.Name, lh.Enabled, lh.Recursive, class(lh.Callback), numel(lh.Object), lh.Object{1} == o);
end

function s = h_ps_notify_in_postset()
o = ObsPair();
b = EventPair();
lh1 = addlistener(b, 'Changed', @(~, ~) vlog('C')); %#ok<NASGU>
lh2 = addlistener(o, 'a', 'PostSet', @(~, ~) notify(b, 'Changed')); %#ok<NASGU>
o.a = 1;
s = logged_text();
end

function s = h_ps_preset_then_postset_order()
o = ObsPair();
lh1 = addlistener(o, 'a', 'PostSet', @(~, ~) vlog('post')); %#ok<NASGU>
lh2 = addlistener(o, 'a', 'PreSet', @(~, ~) vlog('pre')); %#ok<NASGU>
o.a = 1;
s = logged_text();
end

function s = h_ps_alias_write()
o = ObsPair();
p = o;
lh = addlistener(o, 'a', 'PostSet', @(~, evt) vlog(mat2str(evt.AffectedObject.a))); %#ok<NASGU>
p.a = [7 8];
s = [logged_text() ' ' mat2str(o.a)];
end

function s = h_ps_nested_struct_in_prop()
o = ObsPair();
lh = addlistener(o, 's', 'PostSet', @(~, evt) vlog(mat2str(evt.AffectedObject.s.v))); %#ok<NASGU>
o.s.v(2) = 5;
s = logged_text();
end

function s = h_ps_nested_struct_whole()
o = ObsPair();
lh = addlistener(o, 's', 'PostSet', @(~, evt) vlog(mat2str(evt.AffectedObject.s.v))); %#ok<NASGU>
o.s.v = [8 9];
s = [logged_text() ' ' mat2str(o.s.v)];
end
