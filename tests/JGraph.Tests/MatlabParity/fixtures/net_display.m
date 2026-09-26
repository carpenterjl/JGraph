% net_display.m -- how a .NET value displays (interop plan, stage 1): echo, disp, the listing verbs
% (methods, methods -full, events), and the display of returned values. Hyperlinks are reduced to
% their text and each fixture line is the display joined with " / " (ix_flat).

dotnetenv("core", Version="8");
p = interop_paths();
NET.addAssembly(p.assembly);

m = JGTest.Members(2, 'm');
ix_chk('echo', ix_flat(evalc('m')));
ix_chk('disp', ix_flat(evalc('disp(m)')));
ix_chk('display', ix_flat(evalc('display(m)')));
s = System.String('abc');
ix_chk('string_echo', ix_flat(evalc('s')));
x = JGTest.Statics.Twice(int32(4));
ix_chk('int32_return_echo', ix_flat(evalc('x')));
f = JGTest.Factory.Make('t');
ix_chk('factory_echo', ix_flat(evalc('f')));
pt = JGTest.Point(3, 4);
ix_chk('valuetype_echo', ix_flat(evalc('pt')));
d = JGTest.Returns.Decimal();
ix_chk('decimal_echo', ix_flat(evalc('d')));
ip = JGTest.Returns.Pointer();
ix_chk('intptr_echo', ix_flat(evalc('ip')));
n = JGTest.Modifiers.MaybeNull(true);
ix_chk('nullable_echo', ix_flat(evalc('n')));
c = JGTest.Color.Green;
ix_chk('enum_echo', ix_flat(evalc('c')));
dw = System.DayOfWeek.Monday;
ix_chk('bcl_enum_echo', ix_flat(evalc('dw')));
a = NET.addAssembly(p.assembly);
ix_chk('assembly_echo', ix_flat(evalc('a')));
ix_chk('assembly_class', class(a));

ix_chk('methods_list', ix_flat(evalc('methods(m)')));
ix_chk('methods_cell', strjoin(methods(m)', ','));
ix_chk('methods_class_name', strjoin(methods('JGTest.Members')', ','));
ix_chk('methods_full_statics', ix_flat(evalc('methods(''JGTest.Statics'', ''-full'')')));
ix_chk('methods_full_members', ix_flat(evalc('methods(m, ''-full'')')));
ix_chk('methods_full_modifiers', ix_flat(evalc('methods(''JGTest.Modifiers'', ''-full'')')));
pub = JGTest.Publisher();
ix_chk('events', strjoin(events(pub)', ','));
ix_chk('events_disp', ix_flat(evalc('events(pub)')));
