% probe_net_conversions: MATLAB-to-.NET conversion and overload fitness, and .NET-to-MATLAB returns.
%   only<TAB>value<TAB>type    -> the .NET type the Only_<type> overload received, or ERR
%   pair<TAB>value<TAB>A_B     -> which of A and B MATLAB chose, or ERR
%   ret.*                      -> what each .NET return type becomes in MATLAB
dotnetenv("core", Version="8");
a = ip_assets();
NET.addAssembly(a.assembly);

types = cellstr(string(JGTest.Overloads.Types));

vals = {
    'double',          3
    'double.frac',     2.5
    'double.neg',      -1
    'double.big',      1e20
    'double.nan',      NaN
    'double.row',      [1 2 3]
    'double.col',      [1; 2; 3]
    'double.mat',      [1 2; 3 4]
    'double.empty',    []
    'double.complex',  1 + 2i
    'single',          single(3)
    'int8',            int8(3)
    'uint8',           uint8(3)
    'int16',           int16(3)
    'uint16',          uint16(3)
    'int32',           int32(3)
    'uint32',          uint32(3)
    'int64',           int64(3)
    'uint64',          uint64(3)
    'int32.row',       int32([1 2 3])
    'logical',         true
    'logical.row',     [true false]
    'char',            'a'
    'char.row',        'abc'
    'char.mat',        ['ab'; 'cd']
    'char.empty',      ''
    'string',          "abc"
    'string.empty',    ""
    'string.row',      ["a" "b"]
    'string.missing',  string(missing)
    'cell.str',        {'a', 'b'}
    'cell.mixed',      {1, 'b'}
    'cell.empty',      {}
    'struct',          struct('x', 1)
    'fh',              @sin
    'enum.DayOfWeek',  System.DayOfWeek.Monday
    'net.String',      System.String('abc')
    'net.DoubleArr',   NET.createArray('System.Double', 2)
    'net.Object',      System.Object()
    };

for i = 1:size(vals, 1)
    name = vals{i, 1};
    v = vals{i, 2};
    for t = 1:numel(types)
        try
            r = char(JGTest.Overloads.(['Only_' types{t}])(v));
        catch e
            r = ['ERR ' e.identifier];
        end
        fprintf('only\t%s\t%s\t%s\n', name, types{t}, r);
    end
end

for i = 1:size(vals, 1)
    name = vals{i, 1};
    v = vals{i, 2};
    for t1 = 1:numel(types)
        for t2 = t1 + 1:numel(types)
            m = ['Pair_' types{t1} '_' types{t2}];
            try
                r = char(JGTest.Overloads.(m)(v));
            catch e
                r = ['ERR ' e.identifier];
            end
            fprintf('pair\t%s\t%s_%s\t%s\n', name, types{t1}, types{t2}, r);
        end
    end
end

% ---- the messages behind the refusals, once each
ip_pr('msg.only.Int32.char', 'JGTest.Overloads.Only_Int32(''a'')');
ip_pr('msg.only.Double.cell', 'JGTest.Overloads.Only_Double({1})');
ip_pr('msg.only.Double.row', 'JGTest.Overloads.Only_Double([1 2])');
ip_pr('msg.only.DoubleArr.mat', 'JGTest.Overloads.Only_DoubleArr([1 2; 3 4])');
ip_pr('msg.only.String.struct', 'JGTest.Overloads.Only_String(struct())');
ip_pr('msg.only.Double.complex', 'JGTest.Overloads.Only_Double(1+2i)');

% ---- .NET returns into MATLAB
names = {'Bool','Byte','SByte','Int16','UInt16','Int32','UInt32','Int64','UInt64','Single', ...
    'Double','Char','String','NullString','NullObject','Decimal','Pointer','Date','Span', ...
    'BoxedDouble','BoxedString','Id'};
for k = 1:numel(names)
    ip_pr(['ret.' names{k}], ['JGTest.Returns.' names{k} '()']);
end
ip_pr('ret.Int64.exact', 'JGTest.Returns.Int64() == int64(9007199254740992)');
ip_pr('ret.Int64.sprintf', 'sprintf(''%d'', JGTest.Returns.Int64())');
ip_pr('ret.UInt64.sprintf', 'sprintf(''%d'', JGTest.Returns.UInt64())');
ip_pr('ret.NullString.isempty', 'isempty(JGTest.Returns.NullString())');
ip_pr('ret.NullObject.isempty', 'isempty(JGTest.Returns.NullObject())');
ip_pr('ret.Decimal.double', 'double(JGTest.Returns.Decimal())');
ip_pr('ret.Decimal.ToDouble', 'System.Decimal.ToDouble(JGTest.Returns.Decimal())');
ip_pr('ret.Pointer.ToInt64', 'JGTest.Returns.Pointer().ToInt64()');
ip_pr('ret.Date.Year', 'JGTest.Returns.Date().Year');
ip_pr('ret.Date.char', 'char(JGTest.Returns.Date().ToString(''yyyy-MM-dd HH:mm''))');
ip_pr('ret.Span.TotalSeconds', 'JGTest.Returns.Span().TotalSeconds');
ip_px('ret.disp.Decimal', 'x = JGTest.Returns.Decimal()');
ip_px('ret.disp.Date', 'x = JGTest.Returns.Date()');
ip_px('ret.disp.Pointer', 'x = JGTest.Returns.Pointer()');

% ---- System.String and MATLAB text
s = System.String('Hello');
ip_pr('str.char', 'char(s)');
ip_pr('str.string', 'string(s)');
ip_pr('str.cellstr', 'cellstr(s)');
ip_pr('str.double', 'double(s)');
ip_pr('str.Length', 's.Length');
ip_pr('str.plus', 's + "x"');
ip_pr('str.concat', '[s ''x'']');
ip_pr('str.eq.char', 's == ''Hello''');
ip_pr('str.strcmp', 'strcmp(s, ''Hello'')');
ip_pr('str.isequal', 'isequal(s, ''Hello'')');
ip_pr('str.Concat.static', 'System.String.Concat(''a'', ''b'')');
ip_pr('str.Format', 'System.String.Format(''{0}-{1}'', 1, ''x'')');
ip_pr('str.Split', 'System.String(''a,b,c'').Split('','')');
ip_pr('str.Empty', 'System.String.Empty');
ip_pr('str.IsNullOrEmpty.char', 'System.String.IsNullOrEmpty('''')');
ip_pr('str.IsNullOrEmpty.string', 'System.String.IsNullOrEmpty("")');
ip_pr('str.IsNullOrEmpty.missing', 'System.String.IsNullOrEmpty(string(missing))');
ip_pr('str.IsNullOrEmpty.empty', 'System.String.IsNullOrEmpty([])');

% ---- numbers into generic object slots
l = System.Collections.ArrayList();
adds = {'1', 'int8(2)', '''c''', '"d"', 'true', '[1 2 3]', '{1}', '1+2i', '[]', '''''', 'struct()', '@sin', 'int64(2)^60'};
for k = 1:numel(adds)
    ip_px(['arraylist.add.' adds{k}], ['l.Add(' adds{k} ');']);
end
for k = 0:double(l.Count) - 1
    ip_pr(sprintf('arraylist.%d', k), sprintf('l.Item(%d)', k));
    ip_pr(sprintf('arraylist.%d.type', k), sprintf('char(l.Item(%d).GetType().FullName)', k));
end

% ---- value-type copy semantics
p = JGTest.Point(3, 4);
ip_pr('struct.class', 'class(p)');
ip_pr('struct.len', 'p.Length');
p.Move(10);
ip_pr('struct.move.x', 'p.X');
q = p;
q.X = 99;
ip_pr('struct.copy.p', 'p.X');
ip_pr('struct.copy.q', 'q.X');
ip_pr('struct.shift', 'JGTest.Point.Shift(p, 1).X');
ip_pr('struct.after.shift', 'p.X');
ip_px('struct.disp', 'p');
ip_pr('struct.isa.handle', 'isa(p, ''handle'')');
ip_pr('struct.isa.ValueType', 'isa(p, ''System.ValueType'')');

% ---- dictionary and Nullable
ip_pr('nullable.int', 'JGTest.Modifiers.Nullable(int32(3))');
ip_pr('nullable.double', 'JGTest.Modifiers.Nullable(3)');
ip_pr('nullable.empty', 'JGTest.Modifiers.Nullable([])');
ip_pr('nullable.ret.give', 'JGTest.Modifiers.MaybeNull(true)');
ip_pr('nullable.ret.none', 'JGTest.Modifiers.MaybeNull(false)');
ip_px('nullable.ret.disp', 'x = JGTest.Modifiers.MaybeNull(true)');
d = dictionary(["a" "b"], [1 2]);
ip_pr('dictionary.to.object', 'JGTest.Overloads.Only_Object(d)');
