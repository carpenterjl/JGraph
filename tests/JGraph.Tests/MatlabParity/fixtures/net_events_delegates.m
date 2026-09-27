% net_events_delegates.m -- .NET events and delegates (interop plan, stage 5): addlistener and
% listener on a .NET event, argument shapes, listener order, disable and delete, what R2025b
% refuses (a non-standard delegate, a static event), a failing callback, events raised on a thread-
% pool thread (queued while MATLAB runs, delivered at pause and drawnow), and function handles as
% Func, Action and custom delegates. The cross-thread deadlock case never returns in R2025b and is a
% JGraph-only fixture.

dotnetenv("core", Version="8");
p = interop_paths();
NET.addAssembly(p.assembly);
ix_evlog();

pub = JGTest.Publisher();
ix_chk('events', strjoin(events(pub)', ','));
lh = addlistener(pub, 'Fired', @(src, evt) ix_evlog('Fired', src, evt));
ix_chk('listener_class', class(lh));
ix_chk('listener_EventName', lh.EventName);
ix_chk('listener_Source_class', class(lh.Source{1}));
ix_chk('listener_Enabled', lh.Enabled);
ix_chk('net_handler_count', pub.FiredHandlerCount);
pub.RaiseSync();
ix_chk('sync', ix_evlog());
lh2 = addlistener(pub, 'Fired', @(src, evt) ix_evlog('Fired2'));
pub.RaiseSync();
ix_chk('two_listeners_order', ix_evlog());
ix_chk('net_handler_count_two', pub.FiredHandlerCount);
delete(lh2);
pub.RaiseSync();
ix_chk('after_delete', ix_evlog());
lh.Enabled = false;
pub.RaiseSync();
ix_chk('disabled', ix_evlog());
lh.Enabled = true;
clear lh
pub.RaiseSync();
ix_chk('addlistener_survives_clear', ix_evlog());

pub2 = JGTest.Publisher();
L = listener(pub2, 'Fired', @(src, evt) ix_evlog('viaListener'));
pub2.RaiseSync();
ix_chk('listener_fires', ix_evlog());
clear L
pub2.RaiseSync();
ix_chk('listener_cleared', ix_evlog());

lc = addlistener(pub2, 'Custom', @(src, evt) ix_evlog('Custom', evt.Value, evt.Tag));
pub2.RaiseCustom(2.5, 'tag');
ix_chk('custom_args', ix_evlog());
delete(lc);

ix_chk('nonstandard_refused', ix_msg(@() addlistener(pub2, 'NonStandard', @(varargin) ix_evlog('NS'))));
ix_chk('static_event_refused', ix_id(@() addlistener('JGTest.Publisher', 'StaticFired', @(s, e) 1)));
ix_chk('bad_event', ix_id(@() addlistener(pub2, 'NoSuchEvent', @(s, e) 1)));

pub3 = JGTest.Publisher();
lb = addlistener(pub3, 'Fired', @(s, e) error('my:id', 'callback failed'));
w = evalc('pub3.RaiseSync()');
ix_chk('callback_error_is_warning', contains(w, 'Error occurred while executing the listener callback'));
ix_chk('callback_error_not_thrown', ix_id(@() raise_quietly(pub3)));
delete(lb);

% ---- thread-pool events: queued while MATLAB runs, delivered when it yields
pub4 = JGTest.Publisher();
lt = addlistener(pub4, 'Custom', @(src, evt) ix_evlog('Pool', evt.Value));
pub4.RaiseOnThreadPool(int32(3), int32(10));
t0 = tic;
while toc(t0) < 0.5
end
ix_chk('pool_not_during_busy_loop', ix_evlog());
pause(0.5);
ix_chk('pool_at_pause', ix_evlog());
pub4.RaiseOnThreadPool(int32(3), int32(10));
t0 = tic;
while toc(t0) < 0.5
end
drawnow;
ix_chk('pool_at_drawnow', ix_evlog());
delete(lt);

% ---- delegates from function handles
ix_chk('Func_from_handle', JGTest.Invoker.Apply(@(x) x * 3, 2));
ix_chk('Func2_from_handle', JGTest.Invoker.Combine(@(a, b) a - b, 10, 3));
ix_chk('custom_delegate_ctor', JGTest.Invoker.ApplyUnary(JGTest.Unary(@(x) x + 100), 2));
ix_chk('custom_delegate_handle', JGTest.Invoker.ApplyUnary(@(x) x + 100, 2));
fg = NET.createGeneric('System.Func', {'System.Double', 'System.Double'}, @(x) x * 5);
ix_chk('Func_createGeneric_class', class(fg));
ix_chk('Func_createGeneric_apply', JGTest.Invoker.Apply(fg, 2));
ix_chk('Func_Invoke', fg.Invoke(3));
JGTest.Invoker.Run(System.Action(@() ix_evlog('Action')));
ix_chk('Action_ran', ix_evlog());
ix_chk('Action_output_refused', ix_id(@() one(@() JGTest.Invoker.Run(System.Action(@() 1)))));
ix_chk('Describe_delegate', JGTest.Invoker.Call(JGTest.Describe(@(n, c) [char(n) num2str(c)])));
ix_chk('refout_delegate', JGTest.Invoker.CallRefOut(JGTest.RefOut(@(a) deal(a + 1, 7)), 2));
d = JGTest.Invoker.Doubler();
ix_chk('returned_delegate_class', class(d));
ix_chk('returned_delegate_Invoke', d.Invoke(21));
ix_chk('returned_delegate_paren', d(21));
ix_chk('delegate_error', ix_id(@() JGTest.Invoker.Apply(@(x) error('my:id', 'inside'), 1)));
ix_chk('delegate_wrong_return', ix_id(@() JGTest.Invoker.Apply(@(x) 'text', 1)));
u = JGTest.Unary(@(x) x);
ix_chk('BeginInvoke_core', ix_id(@() u.BeginInvoke(4, [], [])));

% Stage 5's own probes (probe5a-h): the edges of the rules above. A queued pool event that reaches a
% listener disabled or deleted since crashes R2025b (probe5d, probe5e), so no line here does that.
pe = JGTest.Publisher();
ix_chk('refuse_nonstd_listener', ix_id(@() listener(pe, 'NonStandard', @(varargin) 1)));
ix_chk('refuse_char_callback', ix_id(@() addlistener(pe, 'Fired', 'disp(1)')));
ix_chk('refuse_number_callback', ix_id(@() addlistener(pe, 'Fired', 5)));
ix_chk('refuse_two_args', ix_id(@() addlistener(pe, 'Fired')));
ix_chk('refuse_four_args', ix_id(@() addlistener(pe, 'Name', 'PostSet', @(s, e) 1)));
ix_chk('refuse_event_number', ix_id(@() addlistener(pe, 5, @(s, e) 1)));
ix_chk('refuse_valuetype', ix_id(@() addlistener(System.DateTime.Now, 'Fired', @(s, e) 1)));
ix_chk('refuse_string_object', ix_id(@() addlistener(System.String('x'), 'Fired', @(s, e) 1)));
ix_chk('bad_event_message', ix_msg(@() addlistener(pe, 'NoSuchEvent', @(s, e) 1)));
ix_chk('string_event_name', class(addlistener(pe, "Fired", @(s, e) 1)));
ix_chk('events_byname', strjoin(events('JGTest.Publisher')', ','));
ix_chk('events_string', strjoin(events(System.String('x'))', ','));
pf = JGTest.Publisher();
lf = addlistener(pf, 'Fired', @(src, evt) ix_evlog('A'));
ix_chk('lh_properties', strjoin(properties(lf)', ','));
ix_chk('lh_Recursive', lf.Recursive);
ix_chk('lh_Source_size', size(lf.Source));
ix_chk('lh_Source_eq', lf.Source{1} == pf);
ix_chk('lh_Callback_class', class(lf.Callback));
ix_chk('lh_isvalid', isvalid(lf));
lf.Callback = @(src, evt) ix_evlog('B');
pf.RaiseSync();
ix_chk('callback_swapped', ix_evlog());
delete(lf);
ix_chk('count_after_delete_all', pf.FiredHandlerCount);
ix_chk('lh_isvalid_deleted', isvalid(lf));
ix_chk('lh_read_deleted', ix_id(@() lf.Enabled));
L2 = listener(pf, 'Fired', @(s, e) ix_evlog('L'));
ix_chk('count_listener', pf.FiredHandlerCount);
clear L2
ix_chk('count_listener_cleared', pf.FiredHandlerCount);
lz = addlistener(pf, 'Fired', @() ix_evlog('zero'));
w = evalc('pf.RaiseSync()');
ix_chk('zero_arg_callback_warns', contains(w, 'Too many input arguments'));
ix_chk('zero_arg_callback_log', ix_evlog());
delete(lz);
lb2 = addlistener(pf, 'Fired', @(s, e) error('my:id', 'callback failed'));
lastwarn('');
evalc('pf.RaiseSync()');
[~, wid] = lastwarn;
ix_chk('callback_error_lastwarn', wid);
delete(lb2);
lr = addlistener(pf, 'Fired', @(s, e) recur(s));
pf.RaiseSync();
ix_chk('recursive_default', ix_evlog());
lr.Recursive = true;
pf.RaiseSync();
ix_chk('recursive_true', ix_evlog());
delete(lr);

p5 = JGTest.Publisher();
ix_chk('obd_listener', class(addlistener(p5, 'ObjectBeingDestroyed', @(s, e) ix_evlog('OBD', s, e))));
delete(p5);
ix_chk('obd_fired', ix_evlog());
ix_chk('deleted_class', class(p5));
ix_chk('deleted_isvalid', isvalid(p5));
ix_chk('deleted_prop', ix_id(@() p5.Name));
ix_chk('deleted_method', ix_id(@() p5.ToString()));
o = JGTest.Publisher();
b = o;
al = System.Collections.ArrayList();
al.Add(o);
delete(o);
ix_chk('deleted_alias_isvalid', isvalid(b));
x = al.Item(0);
ix_chk('deleted_rewrapped_isvalid', isvalid(x));
ix_chk('deleted_rewrapped_Name', x.Name);
ix_chk('deleted_as_argument', ix_id(@() al.Contains(o)));
ix_chk('deleted_eq', o == b);
ix_chk('deleted_isequal', isequal(o, b));
ix_chk('deleted_addlistener', ix_id(@() addlistener(o, 'Fired', @(s, e) 1)));
ix_chk('deleted_events', numel(events(o)));
ix_chk('deleted_isa', isa(o, 'JGTest.Publisher'));
sd = System.String('abc');
delete(sd);
ix_chk('deleted_string_isvalid', isvalid(sd));
ix_chk('deleted_string_char', ix_id(@() char(sd)));

pp = JGTest.Publisher();
lpp = addlistener(pp, 'Custom', @(src, evt) ix_evlog('Pool', evt.Value));
pp.RaiseOnThreadPool(int32(2), int32(10));
busy(0.5);
drawnow nocallbacks
ix_chk('pool_nocallbacks', ix_evlog());
drawnow limitrate
ix_chk('pool_limitrate', ix_evlog());
% pause(0) is left out: R2025b delivered none of two queued events in probe5f and one here.
pp.RaiseOnThreadPool(int32(2), int32(10));
busy(0.5);
ix_chk('pool_callee_pause', waitlog(0.2));
pp.RaiseOnThreadPool(int32(2), int32(10));
busy(0.5);
x1 = 1 + 1; x2 = x1 * 2; %#ok<NASGU>
ix_chk('pool_statements', ix_evlog());
pause(0.2);
ix_chk('pool_after_statements', ix_evlog());
lpc = addlistener(pp, 'Fired', @(s, e) pausing());
pp.RaiseOnThreadPool(int32(2), int32(10));
busy(0.5);
pp.RaiseSync();
ix_chk('pool_inside_callback', ix_evlog());
delete(lpc);
pp.RaiseOnThreadPool(int32(2), int32(10));
busy(0.5);
evalc('pause(0.2)');
ix_chk('pool_in_evalc', ix_evlog());
delete(lpp);

ix_chk('ctor_number', ix_id(@() JGTest.Unary(5)));
ix_chk('ctor_char', ix_id(@() JGTest.Unary('sin')));
ix_chk('ctor_none', ix_id(@() JGTest.Unary()));
ix_chk('ctor_two', ix_id(@() JGTest.Unary(@sin, @cos)));
ix_chk('ctor_message', ix_msg(@() JGTest.Unary(5)));
ix_chk('named_handle', JGTest.Invoker.Apply(@sin, 0.5));
ix_chk('named_local', JGTest.Invoker.Apply(@triple, 2));
ix_chk('ret_int32', JGTest.Invoker.Apply(@(x) int32(3), 1));
ix_chk('ret_logical', JGTest.Invoker.Apply(@(x) true, 1));
ix_chk('ret_single', JGTest.Invoker.Apply(@(x) single(2.5), 1));
ix_chk('ret_empty', ix_id(@() JGTest.Invoker.Apply(@(x) [], 1)));
ix_chk('ret_vector', ix_id(@() JGTest.Invoker.Apply(@(x) [1 2], 1)));
ix_chk('ret_vector_message', ix_msg(@() JGTest.Invoker.Apply(@(x) [1 2], 1)));
ix_chk('ret_char1', ix_id(@() JGTest.Invoker.Apply(@(x) 'a', 1)));
ix_chk('ret_cell', ix_id(@() JGTest.Invoker.Apply(@(x) {1}, 1)));
ix_chk('ret_struct', ix_id(@() JGTest.Invoker.Apply(@(x) struct('a', 1), 1)));
ix_chk('ret_complex', ix_id(@() JGTest.Invoker.Apply(@(x) 1i, 1)));
ix_chk('ret_none', ix_id(@() JGTest.Invoker.Apply(@(x) disp(''), 1)));
ix_chk('ret_string_describe', JGTest.Invoker.Call(JGTest.Describe(@(n, c) "s")));
ix_chk('ret_number_describe', ix_id(@() JGTest.Invoker.Call(JGTest.Describe(@(n, c) 5))));
ix_chk('arg_classes', JGTest.Invoker.Call(JGTest.Describe(@(n, c) [class(n) '/' class(c)])));
ix_chk('too_few_inputs', ix_id(@() JGTest.Invoker.Apply(@() 1, 1)));
ix_chk('unused_input', JGTest.Invoker.Apply(@(a, b) 1, 1));
ix_chk('varargin', JGTest.Invoker.Apply(@(varargin) numel(varargin), 1));
ix_chk('named_failing', ix_id(@() JGTest.Invoker.Apply(@failing, 1)));
ix_chk('named_no_output', ix_id(@() JGTest.Invoker.Apply(@failnoout, 1)));
ix_chk('action_error', ix_id(@() JGTest.Invoker.Run(System.Action(@() error('my:id', 'inside')))));
JGTest.Invoker.Run(System.Action(@showout));
ix_chk('action_nargout', ix_evlog());
ix_chk('func_nargout', JGTest.Invoker.Apply(@nargshow, 1));
ix_chk('action_returns_value', ix_id(@() JGTest.Invoker.Run(System.Action(@() 1))));
ix_chk('action_one_input', ix_id(@() JGTest.Invoker.Run(System.Action(@(x) 1))));
ix_chk('refout_one_output', ix_id(@() JGTest.Invoker.CallRefOut(JGTest.RefOut(@(a) a + 1), 2)));
ix_chk('refout_named', JGTest.Invoker.CallRefOut(JGTest.RefOut(@refout2), 2));
ix_chk('delegate_error_class', error_class(@() JGTest.Invoker.Apply(@(x) error('my:id', 'inside'), 1)));
ix_chk('named_failing_class', error_class(@() JGTest.Invoker.Apply(@failing, 1)));
u1 = JGTest.Unary(@(x) x + 1);
ix_chk('u_Invoke', u1.Invoke(1));
ix_chk('u_Invoke_none', ix_id(@() u1.Invoke()));
ix_chk('u_Invoke_two', ix_id(@() u1.Invoke(1, 2)));
ix_chk('u_paren', u1(1));
ix_chk('u_paren_int32', u1(int32(1)));
ix_chk('u_paren_none', ix_id(@() u1()));
ix_chk('u_paren_char', ix_id(@() u1('a')));
ix_chk('u_isa_Delegate', isa(u1, 'System.Delegate'));
ix_chk('u_isa_MulticastDelegate', isa(u1, 'System.MulticastDelegate'));
ix_chk('u_EndInvoke', ix_id(@() u1.EndInvoke([])));
ix_chk('u_DynamicInvoke', u1.DynamicInvoke({2}));
ix_chk('u_eq_self', u1 == u1);
ix_chk('u_Equals_other', u1.Equals(JGTest.Unary(@(x) x + 1)));
a1 = System.Action(@() ix_evlog('a1'));
a2 = System.Action(@() ix_evlog('a2'));
c12 = System.Delegate.Combine(a1, a2);
ix_chk('Combine_class', class(c12));
c12.Invoke();
ix_chk('Combine_Invoke', ix_evlog());
ix_chk('Combine_length', c12.GetInvocationList().Length);
r12 = System.Delegate.Remove(c12, a1);
r12.Invoke();
ix_chk('Remove_Invoke', ix_evlog());
ix_chk('RemoveAll_empty', isempty(System.Delegate.RemoveAll(c12, c12)));
a1();
ix_chk('paren_Action_statement', ix_evlog());
ix_chk('Action_class', class(a1));
f3 = NET.createGeneric('System.Func', {'System.Double', 'System.Double', 'System.Double'}, @(a, b) a * b);
ix_chk('Func3_class', class(f3));
ix_chk('Func3_apply', JGTest.Invoker.Combine(f3, 3, 4));
fs = NET.createGeneric('System.Func', {'System.String'}, @() 'x');
ix_chk('FuncString_class', class(fs));
ix_chk('FuncString_Invoke', fs.Invoke());
ix_chk('Func_no_handle', ix_id(@() NET.createGeneric('System.Func', {'System.Double', 'System.Double'})));
ix_chk('Func_number', ix_id(@() NET.createGeneric('System.Func', {'System.Double', 'System.Double'}, 5)));
ix_chk('Func_no_handle_message', ix_msg(@() NET.createGeneric('System.Func', {'System.Double', 'System.Double'})));
ag = NET.createGeneric('System.Action', {'System.Double'}, @(v) ix_evlog('ag', v));
ix_chk('ActionGeneric_class', class(ag));
ag.Invoke(7);
ix_chk('ActionGeneric_ran', ix_evlog());
ix_chk('EventHandler_ctor', class(System.EventHandler(@(s, e) 1)));
ix_chk('add_handler_hidden', ix_id(@() pp.add_Fired(System.EventHandler(@(s, e) 1))));

t1 = JGTest.Invoker.ApplyLater(@(v) poolfn(v), 4);
ix_chk('later_class', class(t1));
busy(0.5);
ix_chk('later_busy_done', t1.IsCompleted);
ix_chk('later_busy_log', ix_evlog());
pause(0.3);
ix_chk('later_pause_done', t1.IsCompleted);
ix_chk('later_pause_log', ix_evlog());
ix_chk('later_result', t1.Result);
t2 = JGTest.Invoker.ApplyLater(@(v) poolfn(v), 5);
busy(0.5);
drawnow;
pause(0.05);
ix_chk('later_drawnow_done', t2.IsCompleted);
ix_evlog();
t3 = JGTest.Invoker.ApplyLater(@(v) error('pool:id', 'pool failure'), 5);
evalc('pause(0.3)');
ix_chk('later_error_faulted', t3.IsFaulted);

function one(f)
x = f(); %#ok<NASGU>
end

function raise_quietly(pub)
evalc('pub.RaiseSync()');
end

function recur(s)
% A callback that raises its own event again, twice deep.
persistent depth
if isempty(depth)
    depth = 0;
end
ix_evlog('R');
depth = depth + 1;
if depth < 3
    s.RaiseSync();
end
depth = depth - 1;
end

function busy(t)
t0 = tic;
while toc(t0) < t
end
end

function out = waitlog(t)
pause(t);
out = ix_evlog();
end

function pausing()
ix_evlog('Pin');
pause(0.2);
ix_evlog('Pout');
end

function y = poolfn(x)
ix_evlog('poolfn', x);
y = 10 * x;
end

function y = triple(x)
y = 3 * x;
end

function y = failing(x) %#ok<STOUT,INUSD>
error('my:named', 'named failure');
end

function failnoout(x) %#ok<INUSD>
end

function showout()
ix_evlog(sprintf('nargout=%d', nargout));
end

function y = nargshow(x)
y = nargout + x;
end

function [a, b] = refout2(a)
a = a + 1;
b = 7;
end

function c = error_class(f)
try
    f();
    c = 'none';
catch e
    c = [class(e) ' ' e.identifier];
end
end
