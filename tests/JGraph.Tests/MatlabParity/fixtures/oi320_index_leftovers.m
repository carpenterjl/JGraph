% record: -noFigureWindows
% Open item 320 (ADR 0366): the indexing leftovers after ADR 0354. A struct written into a variable,
% field or cell slot that holds data (s), c{i, j, k} = v over several cells (c), the identifiers of a
% field write one level down and through a cell (f, v, e), cell's sizes (n, z), the colon over
% characters (k, l), reshape's refusals (r), a cell's read past the end naming the position (b), a
% table's paren index by a string name (t, h), the struct-into-data family (g); and the paren index
% after a paren index, which JGraph keeps (two div rows). Each row runs in a fresh workspace and
% prints r (class, size, value) or the error.
rows = {
    's1', 'x = []; x(2, 2) = struct(''a'', 1); r = x;'
    's2', 'x = []; x(1:2) = struct(''a'', 1); r = x;'
    's3', 'x = []; x(:, 1) = struct(''a'', 1); r = x;'
    's4', 'x = []; x(1) = struct(''a'', 1); r = x;'
    's5', 'x = []; x(3) = struct(''a'', 1); r = x;'
    's6', 'y(2, 2) = struct(''a'', 1); r = size(y);'
    's7', 'y(3) = struct(''a'', 1); r = size(y);'
    's8', 'x = []; x(1).a = 1; r = x;'
    's9', 'x = []; x.a = 1; r = x;'
    's10', 'x = zeros(0, 3); x(1) = struct(''a'', 1); r = x;'
    's11', 'x = {}; x(1) = struct(''a'', 1); r = x;'
    's12', 'x = ''''; x(1) = struct(''a'', 1); r = x;'
    's13', 'x = []; x(1, 1, 2) = struct(''a'', 1); r = x;'
    's14', 'x = []; x(:) = struct(''a'', 1); r = x;'
    's15', 'x = []; x(2, 2) = struct(''a'', {}); r = x;'
    's16', 'x = []; x([]) = struct(''a'', 1); r = x;'
    's17', 'x = 5; x(2) = struct(''a'', 1); r = x;'
    's18', 'x = []; x(2) = {1}; r = x;'
    's19', 'x = int8([]); x(2) = struct(''a'', 1); r = x;'
    's20', 'x = []; x(1) = struct(''a'', {1, 2}); r = x;'
    's21', 'x = []; x(1:2) = struct(''a'', {1, 2}); r = size(x);'
    's22', 'x = []; x(1, 1:2) = struct(''a'', {1, 2}); r = size(x);'
    'c1', 'c = cell(2, 2, 2); c{:, 1, 1} = 5; r = c;'
    'c2', 'c = cell(2, 2, 2); c{1:2, 1, 1} = 5; r = c;'
    'c3', 'c = cell(2, 2, 2); c{1, 1, :} = 5; r = c;'
    'c4', 'c = cell(2, 2, 2); c{1, 1, 2} = 5; r = c{1, 1, 2};'
    'c5', 'c = {}; c{1, 1, 2} = 5; r = size(c);'
    'c6', 'c = cell(2, 2, 2); c{:, :, 1} = 5; r = c;'
    'c7', 'c = cell(1, 1, 2); c{1, 1, :} = 5; r = c;'
    'c8', 'c = cell(2, 2, 2); c{[true false], 1, 1} = 5; r = c{1};'
    'c9', 'c = cell(2, 2, 2); c{[], 1, 1} = 5; r = size(c);'
    'c10', 'c = cell(2, 2, 2); c{1, 1, 1:2} = 5; r = c;'
    'c11', 'c = cell(2, 2); c{:, 1} = 5; r = c;'
    'f1', 'st.t = struct(''f'', {1, 2}); st.t.f = 5; r = st;'
    'f2', 'st.t = ''ab''; st.t.f = 1; r = st;'
    'f3', 'st.t = 5; st.t.f = 1; r = st;'
    'f4', 'st.t = struct(''f'', {}); st.t.f = 1; r = st;'
    'f5', 'st.t = "s"; st.t.f = 1; r = st;'
    'f6', 'st.t = {1}; st.t.f = 1; r = st;'
    'f7', 'st.a.b = 5; st.a.b.c = 1; r = st;'
    'f8', 'st = struct(''t'', {1, 2}); st.t.f = 1; r = st;'
    'f9', 'st = struct(''t'', {}); st.t.f = 1; r = st;'
    'f10', 'st.t = []; st.t.f = 1; r = st.t.f;'
    'f11', 'st.t = struct(''f'', {1, 2}); st.t(1).f = 5; r = st.t(1).f;'
    'f12', 'st(2).t = 1; st.t.f = 1; r = st;'
    'f13', 'st.t = true; st.t.f = 1; r = st;'
    'f14', 'st.t = @sin; st.t.f = 1; r = st;'
    'f15', 'st.t = int8(1); st.t.f = 1; r = st;'
    'f16', 'c = {struct(''a'', 1)}; c{1}.a.b = 2; r = c{1}.a.b;'
    'f17', 'c = {5}; c{1}.a = 2; r = c;'
    'f18', 'st.t = struct(''f'', {1, 2}); st.t.f.g = 5; r = st;'
    'n1', 'r = cell(2, 2, 2);'
    'n2', 'r = cell(2, 3, 0);'
    'n3', 'r = cell([2 2 2]);'
    'n4', 'r = size(cell(1, 2, 3, 4));'
    'n5', 'r = size(cell(2, 2, 1));'
    'n6', 'r = size(cell(2, -1, 2));'
    'n7', 'r = size(cell(2, 2, 2.5));'
    'k1', 'r = ''a'':''c'';'
    'k2', 'r = ''a'':2:''e'';'
    'k3', 'r = 1:''c'';'
    'k4', 'r = ''a'':100;'
    'k5', 'r = "a":"c";'
    'k6', 'r = ''c'':''a'';'
    'k7', 'r = ''a'':int8(100);'
    'k8', 'r = ''ab'':''c'';'
    'k9', 'r = ''a'':0.5:''b'';'
    'k10', 'r = 97:''c'';'
    'k11', 'r = ''a'':''a'';'
    'k12', 'r = ''a'':true;'
    'r1', 'r = reshape(1:6, 4, 2);'
    'r2', 'r = reshape(1:6, [], 4);'
    'r3', 'r = reshape(1:6, [4 2]);'
    'r4', 'r = reshape(1:6, [], []);'
    'r5', 'r = reshape({1, 2, 3}, 2, 2);'
    'r6', 'r = reshape(''abc'', 2, 2);'
    'r7', 'r = reshape(1:6, 2, 2, 2);'
    'r8', 'r = reshape(1:6, -1, 6);'
    'r9', 'r = reshape(1:6, 2.5, 2);'
    'r10', 'r = reshape(1:6, 2);'
    'b1', 'c = {1, 2; 3, 4}; r = c{3, 1};'
    'b2', 'c = {1, 2; 3, 4}; r = c(3, 1);'
    'b3', 'c = {1, 2; 3, 4}; r = c{1, 5};'
    'b4', 'c = {1, 2; 3, 4}; r = c{5};'
    'b5', 'c = {1, 2; 3, 4}; r = c{1, 1, 2};'
    'b6', 'c = cell(2, 2, 2); r = c{1, 1, 3};'
    'b7', 'c = {1, 2; 3, 4}; r = c{0, 1};'
    'b8', 'c = {1, 2; 3, 4}; r = c{1.5, 1};'
    'b9', 'c = {1, 2; 3, 4}; r = c(1, 5);'
    'b10', 'c = {1, 2; 3, 4}; r = c{3, :};'
    't1', 'T = table([1; 2], [3; 4], ''VariableNames'', {''p'', ''q''}); r = T(2, "p");'
    't2', 'T = table([1; 2], [3; 4], ''VariableNames'', {''p'', ''q''}); r = T(:, "q");'
    't3', 'T = table([1; 2], [3; 4], ''VariableNames'', {''p'', ''q''}); r = T(2, ["p" "q"]);'
    't4', 'T = table([1; 2], [3; 4], ''VariableNames'', {''p'', ''q''}); r = T{2, "p"};'
    't5', 'T = table([1; 2], [3; 4], ''VariableNames'', {''p'', ''q''}); r = T(2, "z");'
    't6', 'T = table([1; 2], [3; 4], ''VariableNames'', {''p'', ''q''}); T(2, "p") = table(9); r = T.p;'
    't7', 'T = table([1; 2], [3; 4], ''VariableNames'', {''p'', ''q''}); r = T("1", "p");'
    't8', 'T = table([1; 2], [3; 4], ''VariableNames'', {''p'', ''q''}, ''RowNames'', {''a'', ''b''}); r = T("b", "p");'
    't9', 'T = table([1; 2], [3; 4], ''VariableNames'', {''p'', ''q''}); r = T(2, {''p''});'
    'v1', 's = "x"; s.f = 1; r = s;'
    'v2', 'h = @sin; h.f = 1; r = h;'
    'v3', 's = {1}; s.f = 1; r = s;'
    'v4', 'c = {struct(''a'', {1, 2})}; c{1}.a = 5; r = c;'
    'v5', 'c = {5}; c{1}.a.b = 2; r = c;'
    'v6', 'st.t = "s"; st.t.g.h = 1; r = st;'
    'v7', 'st.t = @sin; st.t.f.g = 1; r = st;'
    'v8', 's.a = struct(''b'', {1, 2}); s.a.b.c = 1; r = s;'
    'v9', 'st.t = ["a" "b"]; st.t.f = 1; r = st;'
    'v10', 'st.t = string.empty; st.t.f = 1; r = st;'
    'v11', 'st.t = int8([]); st.t.f = 1; r = st;'
    'v12', 'st.t = {}; st.t.f = 1; r = st;'
    'v13', 'st.t = ''''; st.t.f = 1; r = st;'
    'v14', 'st.t = zeros(0, 3); st.t.f = 1; r = st;'
    'v15', 'c = {struct(''a'', {})}; c{1}.a = 5; r = c;'
    'e1', 'x = {}; x.f = 1; r = x;'
    'e2', 'x = ''''; x.f = 1; r = x;'
    'e3', 'x = int8([]); x.f = 1; r = x;'
    'e4', 'x = zeros(0, 3); x.f = 1; r = x;'
    'e5', 'x = "x"; x.f.g = 1; r = x;'
    'e6', 'x = string.empty; x.f = 1; r = x;'
    'e7', 'x = cell(0, 2); x.f = 1; r = x;'
    'e8', 'x = false(0); x.f = 1; r = x;'
    'e9', 'st.t = cell(0, 2); st.t.f = 1; r = st.t;'
    'e10', 'st.t = false(0); st.t.f = 1; r = st.t;'
    'e11', 'st.t = blanks(0); st.t.f = 1; r = st.t;'
    'e12', 'c = {{}}; c{1}.f = 1; r = c{1};'
    'e13', 'c = {[]}; c{1}.f = 1; r = c{1};'
    'e14', 'st.t = {}; st.t.f.g = 1; r = st.t;'
    'g1', 's.f = []; s.f(2) = struct(''a'', 1); r = s.f;'
    'g2', 'c = {[]}; c{1}(2) = struct(''a'', 1); r = c{1};'
    'g3', 'global g320; g320(2) = struct(''a'', 1); r = g320;'
    'g4', 's.f = 5; s.f(2) = struct(''a'', 1); r = s.f;'
    'g5', 'x = "a"; x(2) = struct(''a'', 1); r = x;'
    'g6', 'x = true; x(2) = struct(''a'', 1); r = x;'
    'g7', 'x = struct(''b'', 1); x(2) = struct(''a'', 1); r = x;'
    'g8', 'x = []; x(2).a = 1; r = x;'
    'g9', 'x = 1:3; x(2) = struct(''a'', 1); r = x;'
    'h1', 'T = table([1; 2], [3; 4], ''VariableNames'', {''p'', ''q''}); r = T(2, ''z'');'
    'h2', 'T = table([1; 2], [3; 4], ''VariableNames'', {''p'', ''q''}); r = T{2, ''z''};'
    'h3', 'T = table([1; 2], [3; 4], ''VariableNames'', {''p'', ''q''}); r = T(''a'', ''p'');'
    'h4', 'T = table([1; 2], [3; 4], ''VariableNames'', {''p'', ''q''}); r = T{2, "z"};'
    'h5', 'T = table([1; 2], [3; 4], ''VariableNames'', {''p'', ''q''}); r = T(2, {''z''});'
    'h6', 'T = table([1; 2], [3; 4], ''VariableNames'', {''p'', ''q''}); r = T(2, "P");'
    'l1', 'r = ''''; for ch = ''a'':''d'', r = [r ch ''-'']; end'
    'l2', 'x = 1:200; r = x(''a'':''c'');'
    'l3', 'r = double(''a'':''c'');'
    'l4', 'r = numel(''a'':''z'');'
    'z1', 'r = size(cell(3));'
    'z2', 'r = size(cell(0));'
    'z3', 'r = size(cell(-1));'
    'z4', 'r = size(cell([2 3]));'
    'z5', 'r = size(cell([4]));'
    'z6', 'r = size(cell(2, 3, 1, 1));'
    'z7', 'r = size(cell(int8(2), 2, 2));'
    'z8', 'r = size(cell([]));'
    'z9', 'r = size(cell(2, []));'
    'z10', 'r = size(cell(Inf));'
    'z11', 'r = size(cell(NaN));'
    };
for k = 1:size(rows, 1)
    fprintf('CHK|%s|%s|exact\n', rows{k, 1}, run_row(rows{k, 2}));
end

% x(:)() and x(1)() are refused when R2025b reads the file; JGraph reads them as the chain they spell.
fprintf('CHK|paren_colon_paren|%s|div=ADR0366\n', run_row('x = 1:3; r = x(:)();'));
fprintf('CHK|paren_one_paren|%s|div=ADR0366\n', run_row('x = 1:3; r = x(1)();'));

function out = run_row(code__)
try
    eval(code__);
    out = describe(r);
catch err__
    out = sprintf('ERR %s / %s', err__.identifier, strrep(err__.message, newline, ' / '));
end
end

function s = describe(v)
c = class(v);
z = mat2str(size(v));
if isstruct(v)
    f = fieldnames(v);
    if isempty(v)
        s = sprintf('%s %s {%s}', c, z, strjoin(f', ','));
    else
        parts = cell(1, numel(v));
        for q = 1:numel(v)
            parts{q} = flat(v(q));
        end
        s = sprintf('%s %s {%s} %s', c, z, strjoin(f', ','), strjoin(parts, ' ; '));
    end
elseif iscell(v)
    parts = cellfun(@flat, v(:)', 'UniformOutput', false);
    s = sprintf('%s %s {%s}', c, z, strjoin(parts, ','));
elseif istable(v)
    s = sprintf('%s %s [%s] %s', c, z, strjoin(v.Properties.VariableNames, ','), mat2str(table2array(v)));
else
    s = sprintf('%s %s %s', c, z, flat(v));
end
end

function s = flat(v)
if isstruct(v)
    f = fieldnames(v);
    parts = cell(1, numel(f));
    for q = 1:numel(f)
        parts{q} = [f{q} '=' flat(v.(f{q}))];
    end
    s = ['<' strjoin(parts, ',') '>'];
elseif ischar(v)
    s = ['''' v ''''];
elseif isstring(v)
    s = ['"' char(strjoin(v, '","')) '"'];
elseif iscell(v)
    s = ['{' strjoin(cellfun(@flat, v(:)', 'UniformOutput', false), ',') '}'];
elseif isnumeric(v) || islogical(v)
    s = mat2str(v);
elseif isa(v, 'function_handle')
    s = func2str(v);
else
    s = ['<' class(v) '>'];
end
end
