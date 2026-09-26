% net_enums.m -- .NET enumerations (interop plan, stage 4): display, conversion to char, string and
% numbers, comparison, [Flags] combinations with bitor and bitand, enums over byte, long and ulong
% (the value past 2^63 is where MATLAB itself rounds), and enums as arguments.

dotnetenv("core", Version="8");
p = interop_paths();
NET.addAssembly(p.assembly);

c = JGTest.Color.Green;
ix_chk('class', class(c));
ix_chk('show', c);
ix_chk('echo', ix_flat(evalc('c')));
ix_chk('char', char(c));
ix_chk('string', string(c));
ix_chk('int32', int32(c));
ix_chk('double', double(c));
ix_chk('eq', c == JGTest.Color.Green);
ix_chk('eq_other', c == JGTest.Color.Red);
ix_chk('eq_char', c == 'Green');
ix_chk('isa_Enum', isa(c, 'System.Enum'));
ix_chk('isa_handle', isa(c, 'handle'));
ix_chk('bad_member', ix_id(@() JGTest.Color.Purple));
ix_chk('enumeration_no_output', ix_id(@() enumeration('JGTest.Color')));
ix_chk('enumeration_output_refused', ix_try(@() enumeration('JGTest.Color')));
ix_chk('enumeration_disp', ix_flat(evalc('enumeration(''JGTest.Color'')')));
ix_chk('GetNames', string(System.Enum.GetNames(c.GetType())));
ix_chk('ToString', c.ToString());
ix_chk('CompareTo', c.CompareTo(JGTest.Color.Blue));

ix_chk('arg_enum', JGTest.EnumTools.Name(c));
ix_chk('arg_char_refused', ix_id(@() JGTest.EnumTools.Name('Blue')));
ix_chk('arg_int32_refused', ix_id(@() JGTest.EnumTools.Name(int32(2))));
ix_chk('arg_double_refused', ix_id(@() JGTest.EnumTools.Name(2)));
ix_chk('return_enum', JGTest.EnumTools.Next(c));
ix_chk('return_enum_class', class(JGTest.EnumTools.Next(c)));

f = JGTest.EnumTools.Combine(JGTest.Access.Read, JGTest.Access.Write);
ix_chk('flags_show', f);
ix_chk('flags_echo', ix_flat(evalc('f')));
ix_chk('flags_char', char(f));
ix_chk('flags_int32', int32(f));
ix_chk('flags_bitor', bitor(JGTest.Access.Read, JGTest.Access.Execute));
ix_chk('flags_bitand', bitand(f, JGTest.Access.Write));
ix_chk('flags_or_refused', ix_id(@() JGTest.Access.Read | JGTest.Access.Execute));
ix_chk('flags_HasFlag', f.HasFlag(JGTest.Access.Write));
ix_chk('flags_arg', JGTest.EnumTools.CanWrite(f));

ix_chk('byte_uint8', uint8(JGTest.EnumTools.SmallHigh()));
ix_chk('byte_int32_refused', ix_id(@() int32(JGTest.EnumTools.SmallHigh())));
ix_chk('long_int64', int64(JGTest.EnumTools.BigLarge()));
ix_chk('long_double', double(JGTest.Big.Negative));
ix_chk('ulong_uint64', sprintf('%d', uint64(JGTest.EnumTools.HugeHigh())));
ix_chk('ulong_value', sprintf('%d', JGTest.EnumTools.HugeValue(JGTest.Huge.High)));
ix_chk('ulong_echo', ix_flat(evalc('JGTest.Huge.High')));
ix_chk('bcl_DayOfWeek', System.DayOfWeek.Friday);
ix_chk('bcl_DayOfWeek_int32', int32(System.DayOfWeek.Friday));
