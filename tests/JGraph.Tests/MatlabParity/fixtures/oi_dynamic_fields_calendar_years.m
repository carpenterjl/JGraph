% record: -noFigureWindows
% Open item 347 (ADR 0384): the values and time leftovers after ADR 0375, and item 346's datestr year.
% A dynamic field an index write creates - s.(f)(3), s.(f){2}, s.(f).g, s(2).(f)(3) with s undefined,
% a struct without the field and a non-struct - and the same writes on static names, an element named by
% a variable (fa, fe, fq; the script's own level in fs, a function's in ff); getfield and setfield past
% one name (gs); a table variable or dimension name past namelengthmax (tn); findobj of a metaclass (mo);
% containers.Map's 'UniformValues' and its other names (mp); calendarDuration's integer refusals, its
% 'Format' option and property, a sign, NaN and Inf elements, the unit a zero element writes, empties and
% the shape verbs (cr, cz, cf, cu, cw, ck); datetime's whole components (dt); a year before 1 read through
% an InputFormat (ny); datestr's letters in R2025b's cnv2icudf.m reading (ds). Each row runs in a fresh
% workspace and prints r (class, size, value) or the error, and the identifier of a warning it raised.
rows = {
    'fa1', 'f = ''a''; s.(f)(3) = 1; r = s;'
    'fa2', 'f = ''a''; s = struct(''b'', 1); s.(f)(3) = 1; r = s;'
    'fa3', 'f = ''a''; s = 5; s.(f)(3) = 1; r = s;'
    'fa4', 'f = ''a''; s.(f){2} = 1; r = s;'
    'fa5', 'f = ''a''; s = struct(''b'', 1); s.(f){2} = 1; r = s;'
    'fa6', 'f = ''a''; s = 5; s.(f){2} = 1; r = s;'
    'fa7', 'f = ''a''; s.(f).g = 1; r = s;'
    'fa8', 'f = ''a''; s = struct(''b'', 1); s.(f).g = 1; r = s;'
    'fa9', 'f = ''a''; s = 5; s.(f).g = 1; r = s;'
    'fa10', 'f = ''a''; s(2).(f)(3) = 1; r = s;'
    'fa11', 'f = ''a''; s = struct(''b'', 1); s(2).(f)(3) = 1; r = s;'
    'fa12', 'f = ''a''; s = 5; s(2).(f)(3) = 1; r = s;'
    'fa13', 'f = ''a''; s.(f)(2, 3) = 1; r = s;'
    'fa14', 'f = ''a''; s.(f)(3).g = 1; r = s;'
    'fa15', 'f = ''a''; s.(f){2}(3) = 1; r = s;'
    'fa16', 'f = ''a''; s = struct(''b'', {1, 2}); s.(f)(3) = 1; r = s;'
    'fa17', 'f = ''a''; s = struct(''b'', {1, 2}); s(2).(f)(3) = 1; r = s;'
    'fa18', 'f = ''a''; s.(f)(3) = ''x''; r = s;'
    'fa19', 'f = ''a''; s.(f)(end+1) = 1; r = s;'
    'fa21', 'f = ''a''; s = []; s.(f)(3) = 1; r = s;'
    'fa22', 'f = "a"; s.(f)(3) = 1; r = s;'
    'fa24', 'f = ''a''; s.(f).g(2) = 1; r = s;'
    'fa25', 'f = ''a''; s = struct(''a'', {}); s.(f)(3) = 1; r = s;'
    'fa26', 'f = ''a''; s = {}; s.(f)(3) = 1; r = s;'
    'fa27', 'f = ''a''; s.(f)(3, :) = [1 2]; r = s;'
    'fa28', 'f = ''a''; s.(f)(2) = int8(3); r = s;'
    'fa29', 'f = ''a''; s.(f)(2) = struct(''x'', 1); r = s;'
    'fa30', 'f = ''a''; s.(f)(2).(f) = 1; r = s;'
    'fa31', 'f = ''a''; s.(f){2}.g = 1; r = s;'
    'fa33', 'f = ''a''; s(2).(f).g = 1; r = s;'
    'fa34', 'f = ''a''; s.b = 1; s.(f)(2) = 5; r = s;'
    'fa35', 'f = ''a''; s = struct(); s.(f)(2) = 5; r = s;'
    'fa36', 'f = ''a''; s = 5; s(2).(f) = 1; r = s;'
    'fa37', 'f = ''a''; s = 5; s.(f) = 1; r = s;'
    'fa38', 'f = ''1a''; s.(f)(3) = 1; r = s;'
    'fa39', 'f = ''a''; s.(f)(3) = {1}; r = s;'
    'fa40', 'f = ''a''; s = struct(''a'', 1); s.(f)(3) = 1; r = s;'
    'fa41', 'f = ''a''; s = ''x''; s.(f)(3) = 1; r = s;'
    'fa42', 'f = ''a''; s = {}; s(2).(f) = 1; r = s;'
    'fa43', 'f = ''a''; s.(f)(0) = 1; r = s;'
    'fa44', 'f = ''a''; s.(f)(1.5) = 1; r = s;'
    'fa45', 'f = ''a''; s.(f)(2) = 1; s.(f)(4) = 2; r = s;'
    'gs1', 's.a.b = 5; r = getfield(s, ''a'', ''b'');'
    'gs2', 's.a.b = 5; s = setfield(s, ''a'', ''b'', 7); r = s.a.b;'
    'gs3', 's.a = [1 2 3]; r = getfield(s, ''a'', {2});'
    'gs4', 's.a = [1 2 3]; s = setfield(s, ''a'', {2}, 9); r = s.a;'
    'gs5', 's(2).a = 4; r = getfield(s, {2}, ''a'');'
    'gs6', 's(2).a = 4; s = setfield(s, {2}, ''a'', 9); r = {s.a};'
    'gs7', 's.a = {1, 2}; r = getfield(s, ''a'', {2});'
    'gs8', 's.a.b = [1 2 3]; r = getfield(s, ''a'', ''b'', {3});'
    'gs9', 's = struct(); s = setfield(s, ''a'', ''b'', 7); r = s;'
    'gs10', 's = setfield(struct(), ''a'', {3}, 7); r = s;'
    'gs11', 's.a = [1 2; 3 4]; r = getfield(s, ''a'', {2, 1});'
    'gs12', 's.a = [1 2; 3 4]; r = getfield(s, ''a'', {'':'', 1});'
    'gs13', 's(2).a = 4; r = getfield(s, ''a'');'
    'gs14', 'r = getfield(struct(''a'', 1), ''b'');'
    'gs15', 's.a.b = 5; r = getfield(s, ''a'', ''c'');'
    'gs16', 's.a = 1; s = setfield(s, ''a'', ''b'', 2); r = s;'
    'gs17', 's.a = [1 2 3]; s = setfield(s, ''a'', {5}, 9); r = s.a;'
    'gs18', 'r = getfield(struct(''a'', 1), {1}, ''a'', {1});'
    'gs19', 's.a.b = 5; r = getfield(s, ''a'', {1}, ''b'');'
    'gs20', 's.a = 1; s = setfield(s, {1}, ''a'', {2}, 9); r = s.a;'
    'gs21', 's.a = ''xyz''; r = getfield(s, ''a'', {[1 3]});'
    'gs22', 's = setfield(5, ''a'', 1); r = s;'
    'gs23', 'r = getfield(5, {1});'
    'gs24', 's.a = 1; r = getfield(s, "a");'
    'gs25', 's.a.b = 1; s = setfield(s, "a", "b", 2); r = s.a.b;'
    'gs26', 's.a = {1, 2}; s = setfield(s, ''a'', {2}, 5); r = s.a;'
    'gs27', 'r = getfield(struct(''a'', 1), ''a'', ''b'');'
    'gs28', 's.a = 1; r = getfield(s, ''a'', 5);'
    'gs29', 's.a = 1; s = setfield(s, ''a'', 5, 2); r = s;'
    'gs30', 's.a.b = 5; r = getfield(s, ''a '', ''b '');'
    'gs31', 's.a.b = 5; s = setfield(s, ''a '', ''b '', 7); r = s.a;'
    'gs32', 's.a = [1 2 3]; r = getfield(s, ''a'', {4});'
    'gs33', 's.a = [1 2 3]; s = setfield(s, ''a'', {0}, 1); r = s;'
    'gs34', 's = struct(''a'', {1, 2}); r = getfield(s, {1, 2}, ''a'');'
    'gs35', 's = struct(''a'', {1, 2}); s = setfield(s, ''a'', 3); r = s;'
    'gs36', 's = struct(''a'', {1, 2}); s = setfield(s, {2}, ''a'', {3}, 7); r = s(2).a;'
    'gs37', 'r = getfield({1, 2}, {2});'
    'gs39', 's.a = 1; s = setfield(s, ''a'', ''b'', ''c'', 3); r = s;'
    'gs40', 's.a = 1; s = setfield(s, ''1x'', ''b'', 3); r = s;'
    'gs41', 's.a = 1; r = getfield(s);'
    'gs42', 's.a = 1; s = setfield(s, ''a'');'
    'gs43', 'f = repmat(''a'', 1, 2049); s = setfield(struct(), f, ''b'', 1); g = fieldnames(s); r = numel(g{1});'
    'gs44', 's.a = 1; r = getfield(s, {1});'
    'gs45', 's.a.b = {1, 2}; r = getfield(s, ''a'', ''b'', {2});'
    'gs46', 's = setfield(struct(), ''a'', {2, 3}, 7); r = s.a;'
    'tn1', 'f = repmat(''a'', 1, 2049); t = table(1, ''VariableNames'', {f}); r = numel(t.Properties.VariableNames{1});'
    'tn2', 'f = repmat(''a'', 1, 2048); t = table(1, ''VariableNames'', {f}); r = numel(t.Properties.VariableNames{1});'
    'tn3', 'f = repmat(''a'', 1, 2049); t = table(1); t.Properties.VariableNames = {f}; r = 1;'
    'tn4', 'f = repmat(''a'', 1, 2049); t = array2table(1, ''VariableNames'', {f}); r = 1;'
    'tn7', 'f = repmat(''a'', 1, 2049); t = table(1, ''RowNames'', {f}); r = numel(t.Properties.RowNames{1});'
    'tn9', 'f = repmat(''a'', 1, 2049); t = table(1); t.Properties.DimensionNames{1} = f; r = 1;'
    'tn10', 'f = repmat(''a'', 1, 2049); t = struct2table(struct(f, 1)); r = numel(t.Properties.VariableNames{1});'
    'tn11', 'f = repmat(''a'', 1, 2049); t = timetable(seconds(1), 1, ''VariableNames'', {f}); r = 1;'
    'tn12', 'f = repmat(''a'', 1, 2049); t = addvars(table(1), 2, ''NewVariableNames'', f); r = 1;'
    'tn13', 'f = repmat(''a'', 1, 2049); t = table(1, 2, ''VariableNames'', {''b'', f}); r = 1;'
    'tn14', 'f = repmat(''a'', 1, 2049); t = table(1, ''VariableNames'', string(f)); r = 1;'
    'tn15', 'f = repmat(''a'', 1, 2049); t = array2table([1 2], ''VariableNames'', {''x'', f}); r = 1;'
    'tn16', 'f = repmat(''a'', 1, 2049); t = table(1); t.Properties.VariableNames{1} = f; r = 1;'
    'mp1', 'm = containers.Map(''KeyType'', ''char'', ''ValueType'', ''any'', ''UniformValues'', true); r = m.Count;'
    'mp2', 'm = containers.Map(''KeyType'', ''char'', ''ValueType'', ''any'', ''UniformValues'', false); r = m.Count;'
    'mp3', 'm = containers.Map({''a'', ''b''}, {1, 2}, ''UniformValues'', true); r = m.ValueType;'
    'mp4', 'm = containers.Map({''a'', ''b''}, {1, 2}, ''UniformValues'', false); r = m.ValueType;'
    'mp5', 'm = containers.Map({''a'', ''b''}, {1, ''x''}, ''UniformValues'', true); r = m.ValueType;'
    'mp6', 'm = containers.Map(''UniformValues'', true); r = m.Count;'
    'mp7', 'm = containers.Map(''KeyType'', ''char'', ''UniformValues'', true); r = m.Count;'
    'mp8', 'm = containers.Map({''a''}, {1}, ''uniformvalues'', false); r = m.ValueType;'
    'mp9', 'm = containers.Map(''a'', 1, ''UniformValues'', true); r = m.ValueType;'
    'mp10', 'm = containers.Map({1, 2}, {''x'', ''y''}, ''UniformValues'', true); r = m.ValueType;'
    'mp11', 'm = containers.Map({''a''}, {[1 2]}, ''UniformValues'', true); r = m.ValueType;'
    'mp12', 'm = containers.Map({''a'', ''b''}, {''x'', ''yy''}, ''UniformValues'', true); r = m.ValueType;'
    'mp13', 'm = containers.Map(''ValueType'', ''double'', ''KeyType'', ''char'', ''UniformValues'', true); r = m.Count;'
    'mp14', 'm = containers.Map(''KeyType'', ''char'', ''ValueType'', ''any'', ''UniformValues''); r = m.Count;'
    'mp15', 'm = containers.Map({''a''}, {1}, ''UniformValues'', 1); r = m.ValueType;'
    'mp16', 'm = containers.Map({''a''}, {1}, ''UniformValues'', ''yes''); r = m.ValueType;'
    'mp17', 'm = containers.Map({''a''}, {int8(1)}, ''UniformValues'', true); r = m.ValueType;'
    'mp18', 'm = containers.Map({''a'', ''b''}, {1, 2}, ''UniformValues'', true, ''UniformValues'', false); r = m.ValueType;'
    'mp19', 'm = containers.Map({''a''}, {1}, ''KeyType'', ''char''); r = m.ValueType;'
    'fe1', 's.a(end+1) = 1; r = s;'
    'fe2', 's = struct(''b'', 1); s.a(end+1) = 1; r = s;'
    'fe3', 's.a.b(end+1) = 1; r = s;'
    'fe5', 'f = ''a''; s = struct(''b'', 1); s.(f)(end+1) = 1; r = s;'
    'fe7', 's = struct(''a'', {1, 2}); s.a(3) = 1; r = s;'
    'fe8', 's = struct(''a'', {1, 2}); s.a{3} = 1; r = s;'
    'fe9', 's = struct(''b'', {1, 2}); s.a.g = 1; r = s;'
    'fe10', 's = struct(''b'', {1, 2}); s.a{2} = 1; r = s;'
    'fe11', 's = struct(''b'', 1); s(2).a.g = 1; r = s;'
    'fe12', 's = 5; s.a{2} = 1;'
    'fe13', 's = 5; s.a.g = 1;'
    'fe14', 's = {}; s.a{2} = 1; r = s;'
    'fe15', 's = ''''; s.a(2) = 1; r = s;'
    'fe16', 's = ''''; s(2).a = 1; r = s;'
    'fe17', 's = ''x''; s(2).a = 1;'
    'fe18', 's = {1}; s(2).a = 1;'
    'fe19', 's = true; s.a(2) = 1;'
    'fe20', 's = int8(1); s(2).a = 1;'
    'fe21', 's.a(3) = true; r = s;'
    'fe22', 's.a(2) = single(3); r = s;'
    'fe23', 's.a(2) = "x"; r = s;'
    'fe24', 's.a(2) = {3}; r = s;'
    'fe25', 's.a(3) = 1i; r = s;'
    'fe26', 's = struct(''b'', 1); s.a(2) = ''x''; r = s;'
    'fe29', 'x(3) = ''x''; r = x;'
    'fe30', 'x(2) = int8(3); r = x;'
    'fe31', 's = struct(''b'', 1); s.a(2, :) = 1; r = s;'
    'fe35', 's = struct(''b'', 1); s(2).a(2) = 1; r = s;'
    'fe36', 'x(3) = true; r = x;'
    'fe37', 'x(2) = single(3); r = x;'
    'fq1', 'u.a = 1; k = 1; u(k).a(2) = 9; r = u;'
    'fq2', 'u.a = [1 2]; k = 1; u(k).a(1) = 9; r = u;'
    'fq3', 'u = struct(''a'', {[1 2], [3 4]}); k = 2; u(k).a(3) = 9; r = u(2).a;'
    'fq4', 'u.a = 1; k = 1; u(k).a(2, 1) = 9; r = u;'
    'fq5', 'u.a.b = 1; k = 1; u(k).a.b(2) = 9; r = u.a;'
    'fq6', 'u = struct(''a'', {1, 2}); u(end).a(2) = 9; r = u(2).a;'
    'fq7', 'u = struct(''a'', {1, 2}); k = 2; u(k).a{2} = 9;'
    'fq8', 'u.a = 1; f = ''a''; k = 1; u(k).(f)(2) = 9; r = u;'
    'fq9', 'v.a = [1 2]; for k = 1:1; v(k).a(3) = 9; end; r = v;'
    'fq10', 'f = ''a''; s.(f)(3) = 1; s.(f)(2) = 5; r = s;'
    'fq11', 'f = ''a''; g = ''b''; s.(f).(g)(2) = 1; r = s;'
    'fq12', 'f = ''a''; s.(f)(3) = int8(1); r = s;'
    'fq13', 'x(3) = ''x''; r = double(x);'
    'fq14', 'f = ''a''; s = struct(''a'', {}); s(1).(f)(2) = 1; r = s;'
    'mo1', 'mc = ?Lv324Cls; p = findobj(mc, ''Name'', ''A''); r = {class(p), size(p), p.Name};'
    'mo2', 'mc = ?Lv324Cls; p = findobj(mc, ''Name'', ''Bogus''); r = {class(p), size(p)};'
    'mo3', 'mc = ?Lv324Cls; p = findobj(mc, ''-regexp'', ''Name'', ''^[AB]$''); r = {class(p), size(p)};'
    'mo4', 'mc = ?Lv324Cls; p = findobj(mc, ''Name'', ''Lv324Cls'', ''-depth'', 0); r = {class(p), size(p), p.Name};'
    'mo5', 'mc = ?Lv324Cls; p = findobj(mc, ''Name'', ''A'', ''-depth'', 1); r = {class(p), size(p)};'
    'mo6', 'mc = ?Lv324Cls; p = findobj(mc, ''Name'', ''C'', ''Dependent'', true); r = {class(p), size(p), p.Name};'
    'mo7', 'mc = ?Lv324Cls; p = findobj(mc, ''Name'', ''B''); r = {class(p), size(p), p.Name};'
    'mo8', 'mc = ?Lv324Cls; p = findobj(mc, ''-property'', ''Dependent''); r = {class(p), size(p)};'
    'mo9', 'mc = ?Lv324Cls; p = findobj(mc, ''Name'', ''Lv324Cls'', ''Static'', false); r = {class(p), size(p)};'
    'mo10', 'mc = ?Lv324Cls; p = findobj(mc, ''Dependent'', true); r = {class(p), size(p), p.Name};'
    'cr1', 'r = repmat(calmonths(2), 1, 3);'
    'cr2', 'r = repmat(calmonths(1:2), 2, 2);'
    'cr3', 'r = reshape(calmonths(1:4), 2, 2);'
    'cr4', 'r = reshape(calmonths(1:4), [], 1);'
    'cr5', 'r = -calmonths(2);'
    'cr6', 'r = -(calmonths(1) + caldays(2) + hours(3));'
    'cr7', 'r = calmonths(NaN);'
    'cr8', 'r = calmonths([1 NaN 3]);'
    'cr9', 'r = isnan(calmonths([1 NaN]));'
    'cr10', 'r = caldays(NaN) + calmonths(1);'
    'cr12', 'r = calendarDuration(1, 2, 3, ''Format'', ''ymdt'');'
    'cr13', 'x = calendarDuration(1, 2, 3); x.Format = ''md''; r = x;'
    'cr14', 'r = calendarDuration(1, 14, 3, ''Format'', ''md'');'
    'cr15', 'r = calendarDuration(0, 0, 10, ''Format'', ''ymwd'');'
    'cr16', 'r = calendarDuration(1, 2, 3, 4, 5, 6, ''Format'', ''ymd'');'
    'cr17', 'r = calendarDuration(1, 2, 3, ''Format'', ''xyz'');'
    'cr18', 'x = calendarDuration(1, 2, 3, ''Format'', ''ymdt''); r = x.Format;'
    'cr19', 'r = calweeks(1.5);'
    'cr20', 'r = calmonths(1.5);'
    'cr21', 'r = caldays(2.5);'
    'cr22', 'r = calyears(0.5);'
    'cr23', 'r = calquarters(1.5);'
    'cr24', 'r = calendarDuration(1.5, 0, 0);'
    'cr25', 'r = calendarDuration(0, 0, 0, 1.5, 0, 0);'
    'cr26', 'r = calendarDuration(0, 0, 0, 0, 0, 1.5);'
    'cr27', 'r = calendarDuration(0, 0, 1.5);'
    'cr28', 'r = string(calmonths([]));'
    'cr29', 'r = string(calmonths(zeros(1, 0)));'
    'cr30', 'r = string(calmonths(zeros(0, 3)));'
    'cr31', 'r = char(calmonths([]));'
    'cr32', 'r = cellstr(calmonths([]));'
    'cr33', 'r = calmonths(Inf);'
    'cr34', 'r = calweeks(NaN);'
    'cr35', 'r = -calweeks(2);'
    'cr36', 'r = +calmonths(2);'
    'cr37', 'x = repmat(calmonths(2), 2); r = size(x);'
    'cr38', 'r = repmat(calweeks(1), 1, 2);'
    'cr39', 'r = reshape(calmonths(1:4), 2, 2)'';'
    'cr40', 'r = permute(calmonths(1:2), [2 1]);'
    'cr41', 'r = squeeze(calmonths(1:2));'
    'cr42', 'r = circshift(calmonths(1:3), 1);'
    'cr43', 'r = fliplr(calmonths(1:3));'
    'cr44', 'r = flip(calmonths(1:3));'
    'cr45', 'r = cat(1, calmonths(1), calmonths(2));'
    'cr46', 'r = horzcat(calmonths(1), caldays(2));'
    'cr47', 'x = calmonths(1:3); x(5) = calmonths(9); r = x;'
    'cr48', 'r = calmonths(NaN) + caldays(1);'
    'cr50', 'r = calmonths(-2);'
    'cr51', 'r = -calmonths([1 2]);'
    'cr52', 'r = 2 * calmonths(3);'
    'cr53', 'r = calmonths(3) * 1.5;'
    'cr54', 'r = calendarDuration(1, 2, NaN);'
    'cr55', 'r = calendarDuration([1 2], 0, 0, ''Format'', ''y'');'
    'cr56', 'r = calendarDuration(0, 0, 0, 1, 30, 0, ''Format'', ''ymdt'');'
    'cr57', 'r = rot90(calmonths(1:2));'
    'cr58', 'r = flipud(calmonths(1:3)'');'
    'cr59', 'x = repmat(calmonths(1:2), [1 1 2]); r = size(x);'
    'cr60', 'r = uminus(calmonths(2));'
    'cr61', 'r = calendarDuration([1 2 3]);'
    'cr62', 'r = calendarDuration([1 2 3], ''Format'', ''md'');'
    'cr63', 'r = calendarDuration(0, 25, 0, ''Format'', ''ymd'');'
    'cr64', 'r = calendarDuration(0, 0, 0, 1.5, 0, 0, ''Format'', ''ymdt'');'
    'cr65', 'r = calendarDuration(0, 0, 40, ''Format'', ''mwd'');'
    'cr66', 'r = calendarDuration(0, 0, 0, ''Format'', ''t'');'
    'cr67', 'r = calendarDuration(0, 0, 0, ''Format'', ''md'');'
    'cr68', 'x = calendarDuration(1, 2, 3); x.Format = ''xyz''; r = x;'
    'cr69', 'r = calmonths(int8(2));'
    'cr70', 'r = calmonths(true);'
    'cr71', 'r = calendarDuration(1, NaN, 0);'
    'cr72', 'r = string(calmonths(NaN));'
    'cr73', 'r = isnan(caldays(NaN) + hours(1));'
    'cr75', 'r = calmonths(-Inf);'
    'cr76', 'r = calendarDuration(0, 0, 0, NaN, 0, 0);'
    'cr77', 'r = string(calendarDuration(1, 2, 3, ''Format'', ''md''));'
    'cr78', 'r = calendarDuration(1, 2, 3, ''format'', ''md'');'
    'cr79', 'r = calendarDuration(1, 2, 3, ''Format'', "md");'
    'cr80', 'r = calendarDuration(1, 2, 3, ''Format'', ''mdt'');'
    'cr81', 'r = calendarDuration(0, 0, 0, 0, 0, 1.5, ''Format'', ''md'');'
    'cr82', 'r = calendarDuration(0, 0, 0, 0, 0, 1, ''Format'', ''md'');'
    'cr83', 'r = -calendarDuration(1, 2, 3, ''Format'', ''md'');'
    'cr84', 'r = string(calendarDuration(1, 2, 3, ''Format'', ''md''))'';'
    'dt1', 'r = datetime(0.5, 1, 1);'
    'dt2', 'r = datetime(2020, 1.5, 1);'
    'dt3', 'r = datetime(2020, 1, 1.5);'
    'dt4', 'r = datetime(2020, 1, 1, 1.5, 0, 0);'
    'dt5', 'r = datetime(2020, 1, 1, 0, 0, 1.5);'
    'dt6', 'r = datetime([2020 0.5], 1, 1);'
    'dt7', 'r = datetime([2020.5 1 1]);'
    'dt8', 'r = datetime(2020, 1, 1, 0, 1.5, 0);'
    'dt9', 'r = datetime(NaN, 1, 1);'
    'dt11', 'r = datetime(2020, 1, 1, 0, 0, 0, 1.5);'
    'dt12', 'r = datetime(-0.5, 1, 1);'
    'dt13', 'r = datetime([2020 1.5 1]);'
    'dt14', 'r = datetime([2020 1 1 0 0 1.5]);'
    'dt15', 'r = datetime(2020, 13, 1);'
    'dt16', 'r = datetime(2020, 1, 1, 25, 0, 0);'
    'dt17', 'r = datetime(2020, 1, NaN);'
    'dt18', 'r = datetime(2020, NaN, 1);'
    'dt19', 'r = datetime(int8(20), 1, 1);'
    'dt20', 'r = datetime(2020, 1, 1.5, ''Format'', ''yyyy-MM-dd HH:mm'');'
    'ny1', 'r = datetime(''-0005-03-01'', ''InputFormat'', ''uuuu-MM-dd'');'
    'ny2', 'r = datetime(''0000-03-01'', ''InputFormat'', ''uuuu-MM-dd'');'
    'ny3', 'r = datetime(''-0005-03-01'', ''InputFormat'', ''yyyy-MM-dd'');'
    'ny4', 'r = datetime(''0005-03-01 BCE'', ''InputFormat'', ''yyyy-MM-dd G'');'
    'ny5', 'r = year(datetime(''-0005-03-01'', ''InputFormat'', ''uuuu-MM-dd''));'
    'ny6', 'r = datetime(''-5-03-01'', ''InputFormat'', ''u-MM-dd'');'
    'ny7', 'r = datetime(''0001-01-01'', ''InputFormat'', ''uuuu-MM-dd'');'
    'ny8', 'r = year(datetime(''0000-03-01'', ''InputFormat'', ''uuuu-MM-dd''));'
    'ny9', 'r = datetime(''-0005-03-01'', ''InputFormat'', ''uuuu-MM-dd'', ''Format'', ''uuuu-MM-dd'');'
    'ny10', 'r = datetime(''01-Mar--0005'', ''InputFormat'', ''dd-MMM-uuuu'');'
    'ny11', 'r = datetime(''12345-03-01'', ''InputFormat'', ''uuuu-MM-dd'');'
    'ny12', 'r = year(datetime(''0005-03-01 BC'', ''InputFormat'', ''yyyy-MM-dd G''));'
    'ny13', 'r = year(datetime(''0001-03-01 BC'', ''InputFormat'', ''yyyy-MM-dd G''));'
    'ny14', 'r = year(datetime(''-0005-03-01'', ''InputFormat'', ''yyyy-MM-dd''));'
    'ny15', 'r = year(datetime(''0005-03-01 AD'', ''InputFormat'', ''yyyy-MM-dd G''));'
    'ny16', 'r = year(datetime(''-0005-03-01 12:30'', ''InputFormat'', ''uuuu-MM-dd HH:mm''));'
    'ny17', 'r = year(datetime(''-12345-03-01'', ''InputFormat'', ''uuuu-MM-dd''));'
    'ny18', 'r = year(datetime(''+0005-03-01'', ''InputFormat'', ''uuuu-MM-dd''));'
    'ny19', 'r = year(datetime(''-05-03-01'', ''InputFormat'', ''uu-MM-dd''));'
    'ny20', 'r = year(datetime({''-0005-03-01'', ''2020-01-01''}, ''InputFormat'', ''uuuu-MM-dd''));'
    'ny21', 'r = datetime(''-0005-03-01'');'
    'ny22', 'r = year(datetime(''-0005-03-01'', ''InputFormat'', ''uuuu-MM-dd'', ''TimeZone'', ''UTC''));'
    'ny23', 'r = year(datetime("-0005-03-01", ''InputFormat'', ''uuuu-MM-dd''));'
    'ds1', 'r = datestr(3, ''yyyy'');'
    'ds2', 'r = datestr(738000, ''YYYY'');'
    'ds3', 'r = datestr(738000, ''YYYY-mm-dd'');'
    'ds4', 'r = datestr(738000, ''YY'');'
    'ds5', 'r = datestr(738000, ''yyyy'');'
    'ds6', 'r = datestr(3, ''dd-mmm-yyyy'');'
    'ds7', 'r = datestr(3);'
    'ds8', 'r = datestr(3, ''yy'');'
    'ds9', 'r = datestr(400, ''yyyy'');'
    'ds10', 'r = datestr(738000, ''YYYY-MM'');'
    'ds11', 'r = datestr(738000, ''Yyyy'');'
    'ds12', 'r = datestr(0.5, ''yyyy'');'
    'ds13', 'r = datestr(3, 1);'
    'ds14', 'r = datestr(3, ''mmm dd'');'
    'ds15', 'r = datestr(3, ''YYY'');'
    'ds16', 'r = datestr(3.5, 31);'
    'ds17', 'r = datestr(366, ''yyyy-mm-dd'');'
    'ds18', 'r = datestr(1, ''yyyy-mm-dd'');'
    'ds19', 'r = datevec(3);'
    'ds20', 'r = datestr(738000, ''DD'');'
    'ds21', 'r = datestr(738000, ''QQ-YYYY'');'
    'ds22', 'r = datestr(738000, 27);'
    'ds23', 'r = datestr(738000, 17);'
    'ds24', 'r = datestr(738000, ''dd/mm/YY'');'
    'ds25', 'r = datestr(738000.25, ''YYYY HH:MM PM'');'
    'ds26', 'r = datestr(0, ''yyyy'');'
    'ds27', 'r = datestr(3, ''yyyy-mm-dd HH:MM'');'
    'ds28', 'r = datestr(31, ''yyyy-mm'');'
    'ds29', 'r = datestr(738000, ''Mmm'');'
    'ds30', 'r = datestr(738000, ''HH:MM:SS.FFF'');'
    'ds31', 'r = datestr(738000, ''hh:mm'');'
    'ds32', 'r = datestr(738000, ''Dd'');'
    'ds33', 'r = datestr([3 738000], ''yyyy'');'
    'ds34', 'r = datestr(738000, ''yyyy-QQ'');'
    'ds35', 'r = datestr(738000, ''QQ'');'
    'ds36', 'r = datestr(738000, ''qq-yyyy'');'
    'ds37', 'r = datestr(738000.5, ''HH:MM:ss'');'
    'ds38', 'r = datestr(738000.5, ''HH:MM:SS.fff'');'
    'cz1', 'r = string(calmonths(0));'
    'cz2', 'r = string(caldays(0));'
    'cz3', 'r = string(calyears(0));'
    'cz4', 'r = string(calweeks(0));'
    'cz5', 'r = string(calquarters(0));'
    'cz6', 'r = calmonths(0).Format;'
    'cz7', 'r = caldays(0).Format;'
    'cz8', 'r = calyears(1).Format;'
    'cz9', 'r = string(calendarDuration(0, 0, 0));'
    'cz10', 'x = calmonths(1:3); x(5) = calmonths(9); r = x.Format;'
    'cz11', 'x = caldays(1:3); x(5) = caldays(9); r = x;'
    'cz12', 'r = string(calmonths(1) + caldays(0));'
    'cz14', 'r = string(calmonths(1) - calmonths(1));'
    'cz15', 'r = string(caldays(1) - caldays(1));'
    'cz16', 'r = string(calendarDuration(0, 0, 0, 0, 0, 0));'
    'cz17', 'r = calendarDuration(0, 0, 0).Format;'
    'cz18', 'r = calendarDuration(1, 2, 3, 4, 5, 6).Format;'
    'cz19', 'r = string(hours(0) + caldays(0));'
    'cz20', 'r = string(calmonths(Inf) + caldays(1));'
    'cz21', 'r = string(caldays(Inf));'
    'cz22', 'r = string(calendarDuration(0, 0, 0, Inf, 0, 0));'
    'cz23', 'r = string(-caldays(Inf));'
    'cz24', 'r = string(calmonths([0 1]));'
    'cz25', 'r = string(calyears([0 1]));'
    'cz26', 'r = string(calweeks([0 1]));'
    'cz27', 'r = calmonths(1:2); r(4) = caldays(1);'
    'cz28', 'r = string(calendarDuration(0, 1, 0));'
    'cz29', 'r = calendarDuration(0, 1, 0).Format;'
    'cf1', 'x = calendarDuration(1, 2, 3, ''Format'', ''md''); r = x.Format;'
    'cf2', 'x = calendarDuration(1, 2, 3); x.Format = ''md''; r = x.Format;'
    'cf3', 'x = calendarDuration(1, 2, 3, ''Format'', ''y''); r = x.Format;'
    'cf4', 'x = calendarDuration(1, 2, 3, ''Format'', ''yqmwdt''); r = x;'
    'cf5', 'x = calendarDuration(1, 2, 3, ''Format'', ''dm'');'
    'cf6', 'x = calendarDuration(1, 2, 3, ''Format'', ''mmdt'');'
    'cf7', 'x = calendarDuration(1, 2, 3, ''Format'', '''');'
    'cf8', 'x = calendarDuration(1, 2, 3, ''Format'', 5);'
    'cf9', 'x = calendarDuration(1, 2, 3, ''Format'', ''MDT'');'
    'cf10', 'x = calmonths(1); x.Format = ''t''; r = x.Format;'
    'cf11', 'x = calendarDuration(1, 2, 3, ''Format'', ''qmdt''); r = x;'
    'cf12', 'x = calendarDuration(0, 0, 10, ''Format'', ''ymwdt''); r = x;'
    'cf13', 'x = calendarDuration(1, 2, 3, ''Bogus'', ''md'');'
    'cf14', 'x = calendarDuration(1, 2, 3, ''Format'');'
    'cf15', 'x = calendarDuration(1, 2, 3, 4, 5, 6, 7);'
    'cf16', 'x = calendarDuration(1, 2);'
    'cf17', 'x = calendarDuration([1 2 3], ''Format'', ''ymd''); r = x;'
    'cf18', 'x = calendarDuration(1, 2, 3, ''Format'', ''md''); y = x + calmonths(1); r = y.Format;'
    'cf19', 'x = calendarDuration(1, 2, 3); x.Format = "mdt"; r = x;'
    'cf20', 'x = calendarDuration(1, 2, 3); x.Format = 5;'
    'mp_w1', 'lastwarn(''''); m = containers.Map({''a''}, {1}, ''KeyType'', ''char''); [w, id] = lastwarn; r = string(w);'
    'cu1', 'r = isnan(calmonths([1 NaN]));'
    'cu2', 'r = ismissing(calmonths([1 NaN]));'
    'cu3', 'r = isinf(calmonths([1 Inf]));'
    'cu4', 'r = isfinite(calmonths([1 Inf NaN]));'
    'cu5', 'r = string(calmonths([1 NaN]));'
    'cu6', 'r = char(calmonths(NaN));'
    'cu7', 'r = cellstr(calmonths([1 NaN]));'
    'cu8', 'r = string(calmonths(zeros(2, 0)));'
    'cu9', 'r = cellstr(calmonths(zeros(1, 0)));'
    'cu10', 'r = char(calmonths(zeros(1, 0)));'
    'cu11', 'r = calmonths(NaN) + calmonths(1);'
    'cu12', 'r = calmonths([1 2]) * NaN;'
    'cu13', 'r = string(calmonths(1) * Inf);'
    'cu14', 'r = calmonths(1i);'
    'cu15', 'r = caldays(''a'');'
    'cu16', 'r = calendarDuration(1, 2, 3, 4, 5.5, 6);'
    'cu17', 'r = calweeks([1 1.5]);'
    'cu18', 'r = calendarDuration([1 2 3.5]);'
    'cu19', 'r = calendarDuration([1 2 3 4 5 6.5]);'
    'cu20', 'r = calendarDuration([1 2 3 4.5 5 6]);'
    'cu21', 'r = calmonths(-1.5);'
    'cu23', 'r = -calmonths(int8(2));'
    'cu24', 'x = calmonths(1:3); r = -x(2:3);'
    'cu25', 'r = uplus(calmonths(2));'
    'cu27', 'r = calmonths(4) / 2;'
    'cu28', 'r = calmonths(3) / 2;'
    'cw1', 'lastwarn(''''); x = calendarDuration(1, 2, 3, ''Format'', ''md''); [w, id] = lastwarn; r = string(strrep(w, newline, ''/''));'
    'cw3', 'try; x = calendarDuration(1, 2); catch e; r = string(strrep(e.message, newline, ''/'')); end'
    'cw4', 'try; x = calendarDuration(1, 2, 3, ''Format'', ''dmt''); catch e; r = string(strrep(e.message, newline, ''/'')); end'
    'cw5', 'try; x = calendarDuration(1, 2, 3, ''Format'', ''tdm''); catch e; r = string(strrep(e.message, newline, ''/'')); end'
    'cw6', 'try; x = calendarDuration(1, 2, 3, ''Format'', ''dmm''); catch e; r = string([e.identifier '' '' strrep(e.message, newline, ''/'')]); end'
    'cw7', 'try; x = calendarDuration(1, 2, 3, ''Format'', ''dy''); catch e; r = string([e.identifier '' '' strrep(e.message, newline, ''/'')]); end'
    'cw8', 'try; x = calendarDuration(1, 2, 3, ''Format'', ''wq''); catch e; r = string([e.identifier '' '' strrep(e.message, newline, ''/'')]); end'
    'ck1', 'r = string([caldays(1) + hours(1), caldays(0)]);'
    'ck2', 'r = string([hours(1) + calmonths(0), calmonths(0)]);'
    'ck3', 'r = string([calyears(1), calmonths(1), caldays(0)]);'
    'ck4', 'r = string([calyears(1), calmonths(0)]);'
    'ck5', 'r = string([calyears(1) + calmonths(1), calmonths(0)]);'
    'ck6', 'r = string([calweeks(1), calweeks(0), caldays(1)]);'
    'ck7', 'r = string([calquarters(1), calquarters(0)]);'
    'ck8', 'r = string([calmonths(1), calmonths(NaN), calmonths(0)]);'
    'ck9', 'r = string([calmonths(1); calmonths(0)]);'
    'ck10', 'r = string([calmonths(-1), calmonths(0)]);'
    'ck11', 'r = string([calmonths(Inf), calmonths(0)]);'
    'ck12', 'x = [calmonths(1) calmonths(0)]; r = string(x(2));'
    'ck13', 'r = cellstr([calmonths(1), calmonths(0)]);'
    'ck14', 'r = char([calmonths(1), calmonths(0)]);'
    'ck15', 'r = string([calendarDuration(0, 14, 0), calmonths(0)]);'
    'ck16', 'r = string([calendarDuration(1, 2, 3) - calendarDuration(1, 2, 3), calmonths(1)]);'
    'ck17', 'r = string([hours(2) + caldays(0), caldays(0)]);'
};
for i = 1:size(rows, 1)
    fprintf('CHK|%s|%s|exact\n', rows{i, 1}, run_row(rows{i, 2}));
end

% The refusals' words, the long names replaced by their lengths.
fprintf('CHK|tn_msg|%s|exact\n', run_row('f = repmat(''a'', 1, 2049); try; t = table(1, ''VariableNames'', {f}); r = 0; catch e; r = string([e.identifier '' '' strrep(e.message, f, ''<2049>'')]); end'));
fprintf('CHK|tn_msg3|%s|exact\n', run_row('f = repmat(''a'', 1, 2049); try; t = table(1); t.Properties.DimensionNames{1} = f; r = 0; catch e; r = string([e.identifier '' '' strrep(e.message, f, ''<2049>'')]); end'));

% The same writes at the script's own level, each in a cleared workspace.
srows = {
    'fs1', 'f = ''a''; s.(f)(3) = 1; r = s;'
    'fs2', 'f = ''a''; s = struct(''b'', 1); s.(f)(3) = 1; r = s;'
    'fs3', 'f = ''a''; s = 5; s.(f)(3) = 1; r = s;'
    'fs4', 'f = ''a''; s.(f){2} = 1; r = s;'
    'fs5', 'f = ''a''; s = struct(''b'', 1); s.(f){2} = 1; r = s;'
    'fs7', 'f = ''a''; s.(f).g = 1; r = s;'
    'fs8', 'f = ''a''; s = struct(''b'', 1); s.(f).g = 1; r = s;'
    'fs10', 'f = ''a''; s(2).(f)(3) = 1; r = s;'
    'fs11', 'f = ''a''; s = struct(''b'', 1); s(2).(f)(3) = 1; r = s;'
    'fs12', 'f = ''a''; s = 5; s(2).(f)(3) = 1; r = s;'
};
for i = 1:size(srows, 1)
    clear s f r
    lastwarn('', '');
    try
        eval(srows{i, 2});
        out__ = describe(r);
    catch err__
        out__ = sprintf('ERR %s / %s', err__.identifier, strrep(err__.message, newline, ' / '));
    end
    fprintf('CHK|%s|%s|exact\n', srows{i, 1}, strrep(out__, '|', '!'));
end

% Written out, not through eval: at the script's level and in a function.
clear s f r
f = 'a';
try; s.(f)(3) = 1; out__ = describe(s); catch err__; out__ = ['ERR ' err__.identifier]; end
fprintf('CHK|fd1|%s|exact\n', out__);
clear s
s = struct('b', 1);
try; s.(f){2} = 1; out__ = describe(s); catch err__; out__ = ['ERR ' err__.identifier]; end
fprintf('CHK|fd5|%s|exact\n', out__);
clear s
try; s(2).(f)(3) = 1; out__ = describe(s); catch err__; out__ = ['ERR ' err__.identifier]; end
fprintf('CHK|fd10|%s|exact\n', out__);
clear s
s = 5;
try; s.(f)(3) = 1; out__ = describe(s); catch err__; out__ = ['ERR ' err__.identifier ' / ' err__.message]; end
fprintf('CHK|fd3|%s|exact\n', out__);
fprintf('CHK|ff1|%s|exact\n', fn_direct());

function out = fn_direct()
f = 'a';
try
    s.(f)(3) = 1;
    t = struct('b', 1);
    t.(f){2} = 1;
    u(2).(f)(3) = 1;
    v = struct('a', {[1 2], [3 4]});
    k = 2;
    v(k).(f)(3) = 9;
    out = [describe(s) ' # ' describe(t) ' # ' describe(u) ' # ' mat2str(v(2).a)];
catch err__
    out = ['ERR ' err__.identifier ' / ' err__.message];
end
end

function out = run_row(code__)
lastwarn('', '');
try
    eval(code__);
    out = describe(r);
catch err__
    out = sprintf('ERR %s / %s', err__.identifier, strrep(err__.message, newline, ' / '));
end
[~, wid__] = lastwarn;
if ~isempty(wid__)
    out = sprintf('%s  WARN %s', out, wid__);
end
out = strrep(out, '|', '!');
end

function s = describe(v)
c = class(v);
z = mat2str(size(v));
if isstruct(v)
    f = fieldnames(v);
    if isempty(v)
        s = sprintf('%s %s {%s}', c, z, strjoin(cellfun(@nm, f', 'UniformOutput', false), ','));
    else
        parts = cell(1, numel(v));
        for q = 1:numel(v)
            parts{q} = flat(v(q));
        end
        s = sprintf('%s %s {%s} %s', c, z, strjoin(cellfun(@nm, f', 'UniformOutput', false), ','), strjoin(parts, ' ; '));
    end
elseif iscell(v)
    parts = cellfun(@flat, v(:)', 'UniformOutput', false);
    s = sprintf('%s %s {%s}', c, z, strjoin(parts, ','));
elseif isdatetime(v) || isduration(v) || iscategorical(v) || iscalendarduration(v)
    if isempty(v)
        s = sprintf('%s %s []', c, z);
    else
        s = sprintf('%s %s [%s]', c, z, strjoin(cellstr(string(v(:)')), ','));
    end
elseif ischar(v) && size(v, 1) > 1
    s = sprintf('%s %s %s', c, z, strjoin(cellfun(@flat, arrayfun(@(q) v(q, :), 1:size(v, 1), 'UniformOutput', false), 'UniformOutput', false), '/'));
else
    s = sprintf('%s %s %s', c, z, flat(v));
end
end

function s = flat(v)
if isstruct(v) && ~isscalar(v)
    s = sprintf('<struct %s {%s}>', mat2str(size(v)), strjoin(fieldnames(v)', ','));
elseif isstruct(v)
    f = fieldnames(v);
    parts = cell(1, numel(f));
    for q = 1:numel(f)
        parts{q} = [nm(f{q}) '=' flat(v.(f{q}))];
    end
    s = ['<' strjoin(parts, ',') '>'];
elseif ischar(v)
    if numel(v) > 60
        s = sprintf('''<%d chars>''', numel(v));
    else
        s = ['''' strrep(strrep(strrep(strrep(v, char(0), '\0'), newline, '\n'), char(9), '\t'), char(13), '\r') ''''];
    end
elseif isstring(v)
    s = ['"' char(strjoin(v, '","')) '"'];
elseif iscell(v)
    s = ['{' strjoin(cellfun(@flat, v(:)', 'UniformOutput', false), ',') '}'];
elseif isnumeric(v) || islogical(v)
    s = [class(v) ':' mat2str(v)];
elseif isa(v, 'function_handle')
    s = func2str(v);
else
    s = ['<' class(v) ' ' mat2str(size(v)) '>'];
end
end

function s = nm(n)
if numel(n) > 60
    s = sprintf('<%d chars>', numel(n));
else
    s = n;
end
end
