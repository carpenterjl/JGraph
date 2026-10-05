% U6 of the app-building plan (ADR 0203): the corners of inheritance and access. What a subclass
% inherits besides plain properties - accessors, validators, a default from the superclass's own
% file - writes that go past a restricted property, a protected method overridden and called from
% the superclass, a private method a subclass also defines, abstract signatures, and which class
% answers a call that mixes two.
global vlog_text
vlog_text = '';

% --- what comes with an inherited property -------------------------------------------------------
a = U6AccSub(3);
u2_chk('default_from_superclass_file', @() u6_getp(U6AccSub, 'W'));
u6_id('validator_of_superclass', @() u6_setp(a, 'W', -1));
u6_id('size_of_superclass', @() u6_setp(a, 'W', [1 2]));
u2_chk('dependent_inherited', @() a.Twice);
a.Twice = 10;
u2_chk('dependent_set_inherited', @() a.W);
vlog_text = '';
a.Tag = 'x';
u2_chk('set_method_inherited', @() u6_log());
u2_chk('local_function_of_superclass', @() a.localSees());
u2_chk('properties_with_dependent', @() properties(a)');

% --- writes that go past a property --------------------------------------------------------------
a = a.grow();
u2_chk('element_write_inside', @() a.Count);
u2_chk('field_write_inside', @() a.Info.b);
u2_chk('element_read_outside', @() a.Count(2));
u2_chk('field_read_outside', @() a.Info.a);
u2_chk('element_write_outside', @() u6_setidx(a, 'Count', 1, 9));
u2_chk('field_write_outside', @() u6_setsub(a, 'Info', 'c', 3));
u2_chk('element_write_kept', @() a.Count);
v = U6Vault;
u2_chk('element_write_private', @() u6_setidx(v, 'Secret', 2, 5));
u2_chk('element_write_readonly', @() u6_setidx(v, 'ReadOnly', 1, 'X'));
u2_chk('element_read_private', @() v.Secret(1));
u2_chk('dynamic_read_private', @() u6_getp(v, 'Secret'));
u2_chk('element_write_public', @() u6_getp(u6_setidx(v, 'Open', 3, 7), 'Open'));
held = {v};
u2_chk('private_through_cell', @() held{1}.Secret);
st.v = v;
u2_chk('private_through_struct', @() st.v.Secret);
u2_chk('public_through_struct', @() st.v.Open);

% --- a protected method a subclass overrides -----------------------------------------------------
u2_chk('hook_called_by_superclass', @() a.runHook());
u2_chk('hook_of_plain_superclass', @() runHook(U6Acc));
u2_chk('hook_outside', @() hook(a));
h = a.hookHandle();
u2_chk('hook_handle', @() h(a));
h = a.describeHandle();
u2_chk('bound_inherited_handle', @() h());
u2_chk('static_through_instance_inside', @() a.stat());
u2_chk('static_through_instance', @() a.kind());
u2_chk('static_through_class', @() U6AccSub.kind());

% --- a private method a subclass also defines ----------------------------------------------------
u2_chk('private_shadow_class', @() class(U6PrivSub));
p = U6PrivSub;
u2_chk('private_shadow_from_superclass', @() p.run());
u2_chk('private_shadow_from_superclass_dot', @() p.runDot());
u2_chk('private_shadow_from_subclass', @() p.runSub());
u2_chk('private_shadow_outside', @() helper(p));
u2_chk('private_shadow_plain', @() run(U6Priv));
u2_chk('private_shadow_plain_outside', @() helper(U6Priv));
u2_chk('private_shadow_methods', @() sort(methods(p))');

% --- abstract signatures -------------------------------------------------------------------------
u2_chk('signatures_abstract', @() U6Sig);
u2_chk('signatures_partial', @() U6SigPart);
s = U6SigImpl;
vlog_text = '';
clear a
a(s);
s.d();
u2_chk('signatures_no_output', @() u6_log());
u2_chk('signatures_one_output', @() b(s, 1));
[r1, r2] = c(s);
u2_chk('signatures_two_outputs', @() [r1 r2]);
u2_chk('signatures_protected', @() s.runE());
u2_chk('signatures_protected_outside', @() g(s));
u2_chk('signatures_static', @() U6SigImpl.f());
u2_chk('signatures_methods', @() sort(methods(s))');

% --- constructors --------------------------------------------------------------------------------
u2_chk('handle_constructor_by_name', @() u6_getp(U6Ctor(4), 'V'));
vlog_text = '';
w = U6Var('named', 5);
u2_chk('varargin_to_superclass', @() {w.Name, w.Sides});
u2_chk('varargin_to_superclass_log', @() u6_log());
w = U6Var;
u2_chk('varargin_none_log', @() u6_log());
u2_chk('varargin_too_many', @() U6Var(1, 2, 3));
vlog_text = '';
br = U6BothRev;
u2_chk('both_constructors_reversed', @() u6_log());

% --- which class answers a call that mixes two ---------------------------------------------------
u2_chk('dominant_first', @() mix(U6High, U6Low));
u2_chk('dominant_second', @() mix(U6Low, U6High));
u2_chk('dominant_alone', @() mix(U6Low, U6Low));
u2_chk('dominant_number_first', @() mix(1, U6Low));
lo = U6Low;
u2_chk('dominant_dot', @() lo.mix(U6High));

% --- events --------------------------------------------------------------------------------------
q = U6QuietSub;
u2_chk('events_hidden_left_out', @() events(q)');
u2_chk('events_by_name', @() events('U6QuietSub')');
lw = addlistener(q, 'Whisper', @(~, ~) vlog('whisper'));
ls = addlistener(q, 'Shout', @(~, ~) vlog('shout'));
vlog_text = '';
q.go();
u2_chk('events_hidden_still_heard', @() u6_log());
u2_chk('events_unknown', @() class(addlistener(q, 'Nope', @(~, ~) 1)));

% --- an object of a subclass through save and load -----------------------------------------------
sq = U6Square(4);
sq.Name = 'kept';
file = [tempname '.mat'];
save(file, 'sq');
back = load(file);
delete(file);
u2_chk('saved_class', @() class(back.sq));
u2_chk('saved_inherited', @() back.sq.Name);
u2_chk('saved_own', @() back.sq.Side);
u2_chk('saved_equal', @() isequal(back.sq, sq));
vlog_text = '';
