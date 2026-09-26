% net_conversions.m -- MATLAB values into .NET parameters and .NET returns into MATLAB (interop
% plan, stage 1). The conversion table: which .NET parameter type each MATLAB value reaches at all
% (conv_<value> lists the Only_<type> answers), which of two overloads it prefers (pair_<value>_<A>
% lists the Pair_<A>_<B> winners for every later B), and what every .NET return type becomes. The
% pairwise table is R2025b's fitness order, recorded, not guessed: tools/interop/summarize-overloads.py
% shows it is a strict order for every value probed.

dotnetenv("core", Version="8");
p = interop_paths();
NET.addAssembly(p.assembly);

types = cellstr(string(JGTest.Overloads.Types));
ix_chk('types', strjoin(types, ','));

vals = {
    'double',          3
    'double_frac',     2.5
    'double_big',      1e20
    'double_nan',      NaN
    'double_row',      [1 2 3]
    'double_col',      [1; 2; 3]
    'double_mat',      [1 2; 3 4]
    'double_empty',    []
    'double_complex',  1 + 2i
    'single',          single(3)
    'int8',            int8(3)
    'uint8',           uint8(3)
    'int16',           int16(3)
    'uint16',          uint16(3)
    'int32',           int32(3)
    'uint32',          uint32(3)
    'int64',           int64(3)
    'uint64',          uint64(3)
    'int32_row',       int32([1 2 3])
    'logical',         true
    'logical_row',     [true false]
    'char',            'a'
    'char_row',        'abc'
    'char_mat',        ['ab'; 'cd']
    'char_empty',      ''
    'string',          "abc"
    'string_empty',    ""
    'string_row',      ["a" "b"]
    'string_missing',  string(missing)
    'cell_str',        {'a', 'b'}
    'cell_mixed',      {1, 'b'}
    'cell_empty',      {}
    'struct',          struct('x', 1)
    'fh',              @sin
    'net_enum',        System.DayOfWeek.Monday
    'net_String',      System.String('abc')
    'net_DoubleArr',   NET.createArray('System.Double', 2)
    'net_Object',      System.Object()
    };

for i = 1:size(vals, 1)
    v = vals{i, 2};
    got = cell(1, numel(types));
    for t = 1:numel(types)
        got{t} = only(v, types{t});
    end
    ix_chk(['conv_' vals{i, 1}], strjoin(got, ','));
end

for i = 1:size(vals, 1)
    v = vals{i, 2};
    for t1 = 1:numel(types) - 1
        got = cell(1, numel(types) - t1);
        for t2 = t1 + 1:numel(types)
            got{t2 - t1} = pair(v, types{t1}, types{t2});
        end
        ix_chk(['pair_' vals{i, 1} '_' types{t1}], strjoin(got, ','));
    end
end

ix_chk('complex_message', ix_msg(@() JGTest.Overloads.Only_Double(1 + 2i)));
ix_chk('nomatch_message', ix_msg(@() JGTest.Overloads.Only_Int32('a')));

% ---- returns
names = {'Bool','Byte','SByte','Int16','UInt16','Int32','UInt32','Int64','Single','Double', ...
    'Char','String','NullString','NullObject','Decimal','Pointer','Span','BoxedDouble', ...
    'BoxedString','Id'};
for k = 1:numel(names)
    ix_chk(['ret_' names{k}], ix_try(@() JGTest.Returns.(names{k})()));
end
ix_chk('ret_Int64_exact', sprintf('%d', JGTest.Returns.Int64()));
ix_chk('ret_UInt64_class', class(JGTest.Returns.UInt64()));
ix_chk('ret_Date_Year', JGTest.Returns.Date().Year);
ix_chk('ret_Date_class', class(JGTest.Returns.Date()));
ix_chk('ret_Decimal_double_refused', ix_id(@() double(JGTest.Returns.Decimal())));
ix_chk('ret_Decimal_ToDouble', System.Decimal.ToDouble(JGTest.Returns.Decimal()));
ix_chk('ret_Pointer_ToInt64', JGTest.Returns.Pointer().ToInt64());
ix_chk('ret_Span_TotalSeconds', JGTest.Returns.Span().TotalSeconds);
ix_chk('ret_NullString_isempty', isempty(JGTest.Returns.NullString()));

% ---- into an object slot
l = System.Collections.ArrayList();
adds = {1, int8(2), 'c', "d", true, [1 2 3], {1}, [], ''};
for k = 1:numel(adds)
    l.Add(adds{k});
end
for k = 0:double(l.Count) - 1
    ix_chk(sprintf('object_slot_%d', k), ix_show(l.Item(k)));
end
ix_chk('object_slot_complex', ix_id(@() l.Add(1 + 2i)));
ix_chk('object_slot_struct', ix_id(@() l.Add(struct())));
ix_chk('object_slot_fh', ix_id(@() l.Add(@sin)));

% ---- a value type is copied
pt = JGTest.Point(3, 4);
ix_chk('struct_class', class(pt));
ix_chk('struct_Length', pt.Length);
pt.Move(10);
ix_chk('struct_Move', pt.X);
q = pt;
q.X = 99;
ix_chk('struct_copy_original', pt.X);
ix_chk('struct_copy_copy', q.X);
ix_chk('struct_isa_handle', isa(pt, 'handle'));
ix_chk('struct_isa_ValueType', isa(pt, 'System.ValueType'));

% ---- Nullable
ix_chk('nullable_int32', JGTest.Modifiers.Nullable(int32(3)));
ix_chk('nullable_double', JGTest.Modifiers.Nullable(3));
ix_chk('nullable_empty', JGTest.Modifiers.Nullable([]));
ix_chk('nullable_ret_class', class(JGTest.Modifiers.MaybeNull(true)));
ix_chk('nullable_ret_HasValue', JGTest.Modifiers.MaybeNull(false).HasValue);

function s = only(v, t)
try
    s = char(JGTest.Overloads.(['Only_' t])(v));
catch
    s = '-';
end
end

function s = pair(v, a, b)
try
    s = char(JGTest.Overloads.(['Pair_' a '_' b])(v));
catch
    s = '-';
end
end
