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

function one(f)
x = f(); %#ok<NASGU>
end

function raise_quietly(pub)
evalc('pub.RaiseSync()');
end
