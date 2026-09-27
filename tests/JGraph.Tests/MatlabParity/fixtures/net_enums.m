% net_enums.m -- .NET enumerations (interop plan, stage 4): display, conversion to char, string and
% numbers, comparison, [Flags] combinations with bitor and bitand, enums over byte, long and ulong
% (the value past 2^63 is where MATLAB itself rounds), and enums as arguments.

dotnetenv("core", Version="8");
p = interop_paths();
NET.addAssembly(p.assembly);

c = JGTest.Color.Green;
ix_chk('class', class(c));
ix_chk('show', c);
ix_chk('echo', ix_flat(evalc('c')), 'div=ADR0174');
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
ix_chk('flags_echo', ix_flat(evalc('f')), 'div=ADR0174');
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
ix_chk('ulong_echo', ix_flat(evalc('JGTest.Huge.High')), 'div=ADR0174');
ix_chk('bcl_DayOfWeek', System.DayOfWeek.Friday);
ix_chk('bcl_DayOfWeek_int32', int32(System.DayOfWeek.Friday));

% Stage 4's own probes (probe4, probe4b, probe4c): the text verbs, relations, casts, bit operations.
ix_chk('switch_member', switched(c, JGTest.Color.Green));
ix_chk('switch_name', switched(c, 'Green'));
ix_chk('strcmp', strcmp(c, 'Green'));
ix_chk('strcmp_cell', strcmp(c, {'Green', 'Red'}));
ix_chk('strcmpi', strcmpi(c, 'green'));
ix_chk('isequal_member', isequal(c, JGTest.Color.Green));
ix_chk('isequal_name', isequal(c, 'Green'));
ix_chk('isequal_string', isequal(c, "Green"));
ix_chk('isequal_two_types', isequal(JGTest.Color.Green, JGTest.Access.Read));
ix_chk('ismember_member', ismember(c, JGTest.Color.Green));
ix_chk('ismember_other', ismember(c, JGTest.Color.Red));
ix_chk('ismember_cellstr', ismember(c, {'Red', 'Green'}));
ix_chk('ismember_string', ismember(c, ["Red" "Green"]));
ix_chk('lt', JGTest.Color.Red < JGTest.Color.Green);
ix_chk('ne', JGTest.Color.Red ~= JGTest.Color.Green);
ix_chk('eq_two_types', JGTest.Color.Green == JGTest.Access.Read);
ix_chk('lt_two_types', JGTest.Color.Red < JGTest.Access.Read);
ix_chk('gt_number', JGTest.Color.Blue > 1);
ix_chk('eq_number', c == 1);
ix_chk('eq_string', c == "Green");
ix_chk('plus_refused', ix_id(@() c + 1));
ix_chk('int8_refused', ix_id(@() int8(c)));
ix_chk('int64_refused', ix_id(@() int64(c)));
ix_chk('single_refused', ix_id(@() single(c)));
ix_chk('logical_refused', ix_id(@() logical(c)));
ix_chk('cellstr', cellstr(c));
ix_chk('long_int32_refused', ix_id(@() int32(JGTest.Big.Negative)));
ix_chk('flags_double', double(JGTest.Access.All));
ix_chk('bitor_nonflags_refused', ix_id(@() bitor(JGTest.Color.Red, JGTest.Color.Green)));
ix_chk('bitand_nonflags_refused', ix_id(@() bitand(JGTest.Color.Red, JGTest.Color.Green)));
ix_chk('bitxor_nonflags_refused', ix_id(@() bitxor(JGTest.Color.Red, JGTest.Color.Green)));
ix_chk('bitor_byte_refused', ix_id(@() bitor(JGTest.Small.Low, JGTest.Small.High)));
ix_chk('bitor_number_refused', ix_id(@() bitor(JGTest.Access.Read, 2)));
ix_chk('bitand_number_refused', ix_id(@() bitand(JGTest.Access.Read, 2)));
ix_chk('bitor_number_first_refused', ix_id(@() bitor(2, JGTest.Access.Read)));
ix_chk('bitxor_number_first_refused', ix_id(@() bitxor(2, JGTest.Access.Read)));
ix_chk('bitor_two_types_refused', ix_id(@() bitor(JGTest.Access.Read, JGTest.Color.Green)));
ix_chk('bitor_type_refused', ix_id(@() bitor(JGTest.Access.Read, JGTest.Access.Write, 'uint8')));
ix_chk('bitor_same', bitor(JGTest.Access.Read, JGTest.Access.Read));
ix_chk('bitxor', bitxor(JGTest.Access.All, JGTest.Access.Read));
ix_chk('bitand_none', bitand(JGTest.Access.Read, JGTest.Access.Write));
ix_chk('undefined_combination', JGTest.EnumTools.Combine(JGTest.Access.Execute, JGTest.Access.Execute));
ix_chk('flags_All_char', char(JGTest.Access.All));
ix_chk('enumeration_member', ix_flat(evalc('enumeration(c)')));
ix_chk('enumeration_flags', ix_flat(evalc('enumeration(''JGTest.Access'')')));
ix_chk('enumeration_bcl', ix_flat(evalc('enumeration(''System.DayOfWeek'')')));
ix_chk('enumeration_command', ix_flat(evalc('enumeration JGTest.Color')));
ix_chk('enumeration_not_enum', ix_flat(evalc('enumeration(''System.String'')')));
ix_chk('enumeration_no_class', ix_flat(evalc('enumeration(''No.Such.Enum'')')));
ix_chk('enumeration_layout', strrep(strrep(evalc('enumeration(''JGTest.Color'')'), newline, '\n'), ' ', '_'));

function s = switched(x, y)
switch x
    case y
        s = 'hit';
    otherwise
        s = 'miss';
end
end
