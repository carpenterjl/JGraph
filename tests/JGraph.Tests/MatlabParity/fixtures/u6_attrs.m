% U6 of the app-building plan (ADR 0203): the attributes a class, a property, a method and an
% event can carry beyond access - Abstract, Sealed, Transient, NonCopyable, AbortSet,
% InferiorClasses, AllowedSubclasses - with the two mixins they need, matlab.mixin.Copyable and
% matlab.mixin.SetGet.
global vlog_text
vlog_text = '';

% --- Abstract ------------------------------------------------------------------------------------
u2_chk('abstract_class', @() U6Abs);
u2_chk('abstract_method_only', @() U6AbsMeth);
u2_chk('abstract_half_done', @() U6Half);
k = U6Conc;
u2_chk('concrete_compute', @() compute(k, 2));
u2_chk('concrete_inherited', @() k.twice(2));
u2_chk('concrete_rate', @() k.Rate);
u5_run('concrete_show', @() show(k));
u2_chk('concrete_show_log', @() u6_log());
u2_chk('concrete_supers', @() superclasses(k)');
u2_chk('concrete_props', @() properties(k)');
mc = ?U6Abs;
u2_chk('meta_abstract', @() mc.Abstract);
mc = ?U6Conc;
u2_chk('meta_concrete', @() mc.Abstract);
u2_chk('meta_name', @() mc.Name);
u2_chk('meta_superclass', @() mc.SuperclassList(1).Name);
mc = ?U6AbsMeth;
u2_chk('meta_abstract_by_method', @() mc.Abstract);
mc = ?U6SealedBase;
u2_chk('meta_sealed', @() mc.Sealed);
mc = ?U6Base;
u2_chk('meta_handle', @() mc.HandleCompatible);
mc = ?U6Shape;
u2_chk('meta_value', @() mc.HandleCompatible);
u2_chk('meta_no_superclass', @() numel(mc.SuperclassList));

% --- attribute forms -----------------------------------------------------------------------------
u2_chk('forms_class', @() class(U6Attr));
u2_chk('forms_static', @() U6Attr.s());
u2_chk('forms_constant', @() U6Attr.K);
u2_chk('forms_method', @() m(U6Attr));
u2_chk('forms_sealed', @() U6AttrSub);

% --- matlab.mixin.Copyable, NonCopyable, Transient -----------------------------------------------
d = U6Doc;
d.Title = 'orig';
d.Stamp = 99;
d.Cache = 'warm';
d.Both = 'set';
d.Child = U6Base(1);
vlog_text = '';
e = copy(d);
u2_chk('copy_class', @() class(e));
u2_chk('copy_is_another', @() e == d);
u2_chk('copy_title', @() e.Title);
u2_chk('copy_data', @() e.Data);
u2_chk('copy_noncopyable', @() e.Stamp);
u2_chk('copy_transient', @() e.Cache);
u2_chk('copy_transient_noncopyable', @() e.Both);
u2_chk('copy_shares_child', @() e.Child == d.Child);
e.Title = 'changed';
u2_chk('copy_independent', @() d.Title);
u2_chk('copy_supers', @() superclasses(d)');
u2_chk('copy_isa_mixin', @() isa(d, 'matlab.mixin.Copyable'));
u2_chk('copy_isa_handle', @() isa(d, 'handle'));
u2_chk('copy_log', @() u6_log());
dd = U6DeepDoc;
dd.Title = 'deep';
ee = copy(dd);
u2_chk('copy_element_class', @() class(ee));
u2_chk('copy_element_title', @() ee.Title);
u2_chk('copy_element_log', @() u6_log());
u2_chk('copy_element_outside', @() copyElement(dd));
u2_chk('copy_of_plain_handle', @() copy(U6MixA));
u2_chk('copy_of_value', @() copy(U6VBox));
u2_chk('copy_methods', @() sort(methods(d))');
vlog_text = '';

% --- Transient and save --------------------------------------------------------------------------
s = U6Saved;
s.Keep = 5;
s.Temp = 'changed';
file = [tempname '.mat'];
save(file, 's');
clear s
load(file);
delete(file);
u2_chk('saved_kept', @() s.Keep);
u2_chk('saved_transient', @() s.Temp);

% --- AbortSet ------------------------------------------------------------------------------------
a = U6Abort;
lp = addlistener(a, 'P', 'PostSet', @(~, ~) vlog('postP'));
lq = addlistener(a, 'Q', 'PostSet', @(~, ~) vlog('postQ'));
vlog_text = '';
a.P = 1;
a.Q = 1;
u2_chk('abortset_same_value', @() u6_log());
a.P = 2;
a.Q = 2;
u2_chk('abortset_new_value', @() u6_log());
a.P = [2 2];
u2_chk('abortset_new_shape', @() u6_log());
a.P = single([2 2]);
u2_chk('abortset_new_class', @() u6_log());
u2_chk('abortset_kept_class', @() class(a.P));

% --- InferiorClasses -----------------------------------------------------------------------------
vlog_text = '';
u2_chk('inferior_on_the_right', @() U6Shape + U6Dom);
u2_chk('inferior_on_the_left', @() U6Dom + U6Shape);
u2_chk('inferior_subclass', @() U6Square(1) + U6Dom);
u2_chk('inferior_with_number', @() 1 + U6Dom);
vlog_text = '';

% --- AllowedSubclasses ---------------------------------------------------------------------------
u2_chk('allowed_subclass', @() class(U6Allowed));
u2_chk('denied_subclass', @() U6Denied);

% --- matlab.mixin.SetGet -------------------------------------------------------------------------
kn = U6Knob;
u5_run('setget_set', @() set(kn, 'Level', 5));
u2_chk('setget_get', @() get(kn, 'Level'));
u5_run('setget_set_pairs', @() set(kn, 'Level', 2, 'Label', 'x'));
u2_chk('setget_get_label', @() get(kn, 'Label'));
u2_chk('setget_get_cell', @() get(kn, {'Level', 'Label'}));
u5_run('setget_case', @() set(kn, 'level', 3));
u2_chk('setget_case_got', @() kn.Level);
u5_run('setget_partial', @() set(kn, 'Lev', 4));
u2_chk('setget_partial_got', @() get(kn, 'Lev'));
u5_run('setget_readonly', @() set(kn, 'Locked', 1));
u2_chk('setget_get_readonly', @() get(kn, 'Locked'));
u5_run('setget_private', @() set(kn, 'Inner', 1));
u2_chk('setget_get_private', @() get(kn, 'Inner'));
u5_run('setget_unknown', @() set(kn, 'Nope', 1));
u2_chk('setget_get_unknown', @() get(kn, 'Nope'));
u5_run('setget_struct', @() set(kn, struct('Level', 9, 'Label', 'st')));
u2_chk('setget_struct_got', @() {kn.Level, kn.Label});
g = get(kn);
u2_chk('setget_get_all', @() fieldnames(g)');
u2_chk('setget_supers', @() superclasses(kn)');
u2_chk('setget_on_plain', @() get(U6MixA, 'A'));
