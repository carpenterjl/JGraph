% record: -noFigureWindows
% U6 of the app-building plan (ADR 0203): function handles to methods. A handle made inside a
% class - to a bare method name, to obj.method, as an anonymous function, to a static method -
% is called later from outside it, as a callback is; a handle made outside is held to the access
% the script has.
global vlog_text
vlog_text = '';
c = U6Counter;

% --- made inside the class -----------------------------------------------------------------------
h = c.bare();
u2_chk('bare_class', @() class(h));
h(c, 5);
u2_chk('bare_called', @() c.Count);
h = c.dotted();
u2_chk('dotted_text', @() strrep(func2str(h), ' ', ''));
h(2);
u2_chk('dotted_called', @() c.Count);
h = c.anon();
u2_chk('anonymous_text', @() strrep(func2str(h), ' ', ''));
h(3);
u2_chk('anonymous_called', @() c.Count);
h = c.anonDot();
h(4);
u2_chk('anonymous_dot_called', @() c.Count);
h = c.privBare();
u5_run('private_bare_call', @() h(c));
u2_chk('private_bare_called', @() c.Count);
h = c.privDot();
u2_chk('private_dotted_text', @() strrep(func2str(h), ' ', ''));
u5_run('private_dotted_call', @() h());
u2_chk('private_dotted_called', @() c.Count);
h = c.privAnon();
u5_run('private_anonymous_call', @() h());
u2_chk('private_anonymous_called', @() c.Count);
h = c.stat();
u2_chk('static_called', @() h(21));
h = c.wrapped();
u5_run('wrapped_call', @() h([], []));
u2_chk('wrapped_called', @() c.Count);
u5_run('stored_call', @() c.runFn());
u2_chk('stored_called', @() c.Count);
h = c.cb();
h([], []);
u2_chk('callback_called', @() c.Count);
h = c.cbAnon();
h([], []);
u2_chk('callback_anonymous_called', @() c.Count);

% --- made in the script --------------------------------------------------------------------------
c = U6Counter;
h = @add;
h(c, 10);
u2_chk('script_bare_called', @() c.Count);
h = @c.add;
u2_chk('script_dotted_text', @() strrep(func2str(h), ' ', ''));
h(1);
u2_chk('script_dotted_called', @() c.Count);
h = @step;
u5_run('script_private_bare', @() h(c));
h = @() step(c);
u5_run('script_private_anonymous', @() h());
h = @() c.step();
u5_run('script_private_dotted', @() h());
u2_chk('script_private_left_alone', @() c.Count);
h = str2func('add');
h(c, 1);
u2_chk('str2func_called', @() c.Count);
feval(@add, c, 1);
feval('add', c, 1);
u2_chk('feval_called', @() c.Count);
h = @U6Counter.twice;
u2_chk('script_static', @() h(4));
h = @U6Counter;
u2_chk('constructor_handle', @() class(h()));

% --- a handle keeps its object -------------------------------------------------------------------
c = U6Counter;
g = @() c.Count;
h = c.dotted();
clear c
h(7);
u2_chk('alive_after_clear', @() g());

% --- as a graphics callback ----------------------------------------------------------------------
c = U6Counter;
fig = figure('Visible', 'off');
btn = uicontrol(fig, 'Callback', c.cb());
cbk = get(btn, 'Callback');
u2_chk('callback_property_class', @() class(cbk));
cbk(btn, []);
u2_chk('callback_property_called', @() c.Count);
set(btn, 'Callback', c.privAnon());
cbk = get(btn, 'Callback');
cbk();
u2_chk('callback_private_called', @() c.Count);
delete(fig);

% --- methods through cellfun and arrayfun --------------------------------------------------------
u2_chk('cellfun_method', @() cellfun(@area, {U6Square(2), U6Square(3)}));
u2_chk('cellfun_anonymous', @() cellfun(@(o) o.area(), {U6Square(2), U6Cube}));
u2_chk('cellfun_text', @() cellfun(@describe, {U6Square(2), U6Shape}, 'UniformOutput', false));
