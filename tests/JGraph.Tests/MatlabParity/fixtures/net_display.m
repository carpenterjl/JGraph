% net_display.m -- how a .NET value displays (interop plan, stage 1): echo, disp, the listing verbs
% (methods, methods -full, events), and the display of returned values. Hyperlinks are reduced to
% their text and each fixture line is the display joined with " / " (ix_flat).
%
% JGraph displays a value in its own layout, as it does every value (ADR 0174), so the display rows
% are recorded divergences; the listing rows name what R2025b lists. methods -full is filtered by
% own_lines: R2025b lists overloads in an order it does not keep between runs, so the lines are sorted.

dotnetenv("core", Version="8");
p = interop_paths();
NET.addAssembly(p.assembly);

m = JGTest.Members(2, 'm');
ix_chk('echo', ix_flat(evalc('m')), 'div=ADR0174');
ix_chk('disp', ix_flat(evalc('disp(m)')), 'div=ADR0174');
ix_chk('display', ix_flat(evalc('display(m)')), 'div=ADR0174');
s = System.String('abc');
ix_chk('string_echo', ix_flat(evalc('s')), 'div=ADR0174');
x = JGTest.Statics.Twice(int32(4));
ix_chk('int32_return_echo', ix_flat(evalc('x')), 'div=ADR0174');
f = JGTest.Factory.Make('t');
ix_chk('factory_echo', ix_flat(evalc('f')), 'div=ADR0174');
pt = JGTest.Point(3, 4);
ix_chk('valuetype_echo', ix_flat(evalc('pt')), 'div=ADR0174');
d = JGTest.Returns.Decimal();
ix_chk('decimal_echo', ix_flat(evalc('d')), 'div=ADR0174');
ip = JGTest.Returns.Pointer();
ix_chk('intptr_echo', ix_flat(evalc('ip')), 'div=ADR0174');
n = JGTest.Modifiers.MaybeNull(true);
ix_chk('nullable_echo', ix_flat(evalc('n')), 'div=ADR0174');
c = JGTest.Color.Green;
ix_chk('enum_echo', ix_flat(evalc('c')), 'div=ADR0174');
dw = System.DayOfWeek.Monday;
ix_chk('bcl_enum_echo', ix_flat(evalc('dw')), 'div=ADR0174');
a = NET.addAssembly(p.assembly);
ix_chk('assembly_echo', ix_flat(evalc('a')), 'div=ADR0174');
ix_chk('assembly_class', class(a));

ix_chk('methods_list', ix_flat(evalc('methods(m)')));
ix_chk('methods_cell', strjoin(methods(m)', ','));
ix_chk('methods_class_name', strjoin(methods('JGTest.Members')', ','));
ix_chk('methods_full_statics', ix_flat(own_lines(evalc('methods(''JGTest.Statics'', ''-full'')'))));
ix_chk('methods_full_members', ix_flat(own_lines(evalc('methods(m, ''-full'')'))));
ix_chk('methods_full_modifiers', ix_flat(own_lines(evalc('methods(''JGTest.Modifiers'', ''-full'')'))));
pub = JGTest.Publisher();
ix_chk('events', strjoin(events(pub)', ','));
ix_chk('events_disp', ix_flat(evalc('events(pub)')));

function t = own_lines(t)
% OWN_LINES  A methods -full listing without the lines handle and matlab.mixin.Scalar contribute,
%   and in sorted order: R2025b lists overloads of one name in an order it does not keep from one
%   run to the next (handle's addlistener, and a type's own Twice(int32) and Twice(double) alike).
lines = splitlines(string(t));
lines = lines(~contains(lines, '% Inherited from'));
t = char(strjoin(sort(lines), newline));
end
