% probe_net_events: .NET events and delegates: listeners, argument shapes, threads, lifetime.
dotnetenv("core", Version="8");
a = ip_assets();
NET.addAssembly(a.assembly);
global LOG
LOG = {};
logit = @(varargin) ip_evlog(varargin{:});

p = JGTest.Publisher();
ip_pr('events', 'events(p)');
ip_px('events.disp', 'events(p)');

% ---- synchronous standard event
lh = addlistener(p, 'Fired', @(src, evt) logit('Fired', src, evt));
ip_pr('lh.class', 'class(lh)');
ip_px('lh.disp', 'lh');
ip_pr('count.after.add', 'p.FiredHandlerCount');
p.RaiseSync();
ip_pr('log.sync', 'ip_evlog()');
lh2 = addlistener(p, 'Fired', @(src, evt) logit('Fired2'));
p.RaiseSync();
ip_pr('log.two.listeners', 'ip_evlog()');
ip_pr('count.two', 'p.FiredHandlerCount');
delete(lh2);
ip_pr('count.after.delete', 'p.FiredHandlerCount');
p.RaiseSync();
ip_pr('log.after.delete', 'ip_evlog()');
lh.Enabled = false;
p.RaiseSync();
ip_pr('log.disabled', 'ip_evlog()');
lh.Enabled = true;
clear lh
p.RaiseSync();
ip_pr('log.after.clear.lh', 'ip_evlog()');
ip_pr('count.after.clear.lh', 'p.FiredHandlerCount');

% ---- listener() does not keep itself alive?
L = listener(p, 'Fired', @(src, evt) logit('viaListener'));
p.RaiseSync();
ip_pr('log.listener', 'ip_evlog()');
clear L
p.RaiseSync();
ip_pr('log.listener.cleared', 'ip_evlog()');

% ---- EventHandler<CustomArgs>
lc = addlistener(p, 'Custom', @(src, evt) logit('Custom', src, evt));
p.RaiseCustom(2.5, 'tag');
ip_pr('log.custom', 'ip_evlog()');
delete(lc);

% ---- non-standard delegate signature
ip_px('nonstandard.addlistener', 'ln = addlistener(p, ''NonStandard'', @(varargin) ip_evlog(''NonStandard'', varargin{:}));');
ip_px('nonstandard.raise', 'p.RaiseNonStandard(int32(7), ''seven'')');
ip_pr('log.nonstandard', 'ip_evlog()');

% ---- a static event
ip_px('static.addlistener', 'ls = addlistener(''JGTest.Publisher'', ''StaticFired'', @(s, e) ip_evlog(''Static'', s, e));');
JGTest.Publisher.RaiseStatic();
ip_pr('log.static', 'ip_evlog()');

% ---- a bad event name, a bad callback
ip_px('bad.event', 'addlistener(p, ''NoSuchEvent'', @(s, e) 1)');
ip_px('bad.callback.nargin', 'lb = addlistener(p, ''Fired'', @() ip_evlog(''zeroArgs'')); p.RaiseSync(); delete(lb);');
ip_pr('log.bad.callback', 'ip_evlog()');
ip_px('callback.errors', 'lb = addlistener(p, ''Fired'', @(s, e) error(''my:id'', ''callback failed'')); p.RaiseSync(); delete(lb);');
ip_px('callback.errors.after', 'disp(''still running'')');

% ---- an event raised on a thread-pool thread while MATLAB is busy and while it pauses
lt = addlistener(p, 'Custom', @(src, evt) logit('Pool', evt.Value));
p.RaiseOnThreadPool(int32(3), int32(50));
t0 = tic; x = 0; while toc(t0) < 0.5, x = x + 1; end %#ok<NASGU>
ip_pr('log.pool.busy.loop', 'ip_evlog()');
pause(0.5);
ip_pr('log.pool.after.pause', 'ip_evlog()');
p.RaiseOnThreadPool(int32(3), int32(50));
pause(1);
ip_pr('log.pool.during.pause', 'ip_evlog()');
p.RaiseOnThreadPool(int32(3), int32(50));
java.lang.Thread.sleep(500);
ip_pr('log.pool.java.sleep', 'ip_evlog()');
drawnow;
ip_pr('log.pool.after.drawnow', 'ip_evlog()');
delete(lt);

% ---- delegates from function handles
ip_pr('del.Func', 'JGTest.Invoker.Apply(@(x) x * 3, 2)');
ip_pr('del.Unary', 'JGTest.Invoker.ApplyUnary(JGTest.Unary(@(x) x + 100), 2)');
ip_pr('del.Unary.handle', 'JGTest.Invoker.ApplyUnary(@(x) x + 100, 2)');
ip_pr('del.Func2', 'JGTest.Invoker.Combine(@(a, b) a - b, 10, 3)');
ip_pr('del.Func.class', 'class(System.Func<System*Double,System*Double>(@(x) x))');
ip_pr('del.Func.createGeneric', 'class(NET.createGeneric(''System.Func'', {''System.Double'', ''System.Double''}, @(x) x))');
fg = NET.createGeneric('System.Func', {'System.Double', 'System.Double'}, @(x) x * 5);
ip_pr('del.Func.generic.apply', 'JGTest.Invoker.Apply(fg, 2)');
ip_pr('del.Func.Invoke', 'fg.Invoke(3)');
ip_pr('del.Action', 'JGTest.Invoker.Run(System.Action(@() ip_evlog(''Action'')))');
ip_px('del.Action.call', 'JGTest.Invoker.Run(System.Action(@() ip_evlog(''Action'')))');
ip_pr('log.action', 'ip_evlog()');
ip_pr('del.Describe', 'JGTest.Invoker.Call(JGTest.Describe(@(n, c) [char(n) num2str(c)]))');
ip_pr('del.RefOut', 'JGTest.Invoker.CallRefOut(JGTest.RefOut(@(a) deal(a + 1, 7)), 2)');
ip_pr('del.returned', 'class(JGTest.Invoker.Doubler())');
d = JGTest.Invoker.Doubler();
ip_pr('del.returned.Invoke', 'd.Invoke(21)');
ip_pr('del.returned.paren', 'd(21)');
ip_pr('del.error', 'JGTest.Invoker.Apply(@(x) error(''my:id'', ''inside delegate''), 1)');
ip_pr('del.wrong.return', 'JGTest.Invoker.Apply(@(x) ''text'', 1)');
ip_pr('del.methods', 'methods(d)');
u = JGTest.Unary(@(x) x);
ip_px('del.BeginInvoke', 'ar = u.BeginInvoke(4, [], []); r = u.EndInvoke(ar)');
