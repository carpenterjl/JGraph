% record: -noFigureWindows
% Open item 318 (ADR 0378): a fresh session's warning table, fopen's own readers detecting the encoding as
% fileread does (fgetl, fgets, fscanf, fread of char, textscan through a file id) and fopen(fid) naming it,
% fwrite to the console (ids 1 and 2), the metaclass of a built-in class (?MException), MException's read-only
% properties, checkcode's ignored-arguments warning, and an identifier's message read as a format.
s = warning;
parts = cell(1, numel(s));
for k = 1:numel(s)
    parts{k} = [s(k).identifier ':' s(k).state];
end
chk('fresh_table', sprintf('%d # %s', numel(s), strjoin(parts, ',')));
chk('fresh_printed', lines(evalc('warning')));
q = warning('query', 'verbose');
chk('fresh_verbose', q.state);
q = warning('query', 'MATLAB:JavaComponentThreading');
chk('fresh_one_off', q.state);
warning('off', 'oi318:z');
chk('printed_sorted', lines(evalc('warning')));
warning('on', 'all');
s = warning;
chk('after_on_all', sprintf('%d # %s:%s', numel(s), s(1).identifier, s(1).state));

% --- fopen's own reading detects the encoding ---------------------------------------------------
lib = tempname;
mkdir(lib);
files = {
    'utf8', uint8([72 195 169 108 10 50 10])
    'cp1252', uint8([72 233 108 10 50 10])
    'bom', uint8([239 187 191 72 195 169 10 50 10])
    'ascii', uint8([72 105 10 50 10])
    'broken', uint8([72 195 169 233 65 10 50 10])
    'cutend', uint8([72 195 169 233])
    'u16', uint8([255 254 72 0 105 0 10 0])
    'late', uint8([65 10 66 233 10])
    };
for k = 1:size(files, 1)
    f = fullfile(lib, [files{k, 1} '.txt']);
    fid = fopen(f, 'w'); fwrite(fid, files{k, 2}, 'uint8'); fclose(fid);
    nm = files{k, 1};
    fid = fopen(f); [~, ~, ~, enc] = fopen(fid); fclose(fid);
    chk(['enc_' nm], enc);
    fid = fopen(f); t1 = fgetl(fid); t2 = fgetl(fid); fclose(fid);
    chk(['fgetl_' nm], [codes(t1) '/' codes(t2)]);
    fid = fopen(f); t = fgets(fid); fclose(fid);
    chk(['fgets_' nm], codes(t));
    fid = fopen(f); t = fscanf(fid, '%c'); fclose(fid);
    chk(['fscanf_c_' nm], codes(t));
    fid = fopen(f); t = fscanf(fid, '%s'); fclose(fid);
    chk(['fscanf_s_' nm], codes(t));
    fid = fopen(f); t = fread(fid, '*char')'; fclose(fid);
    chk(['fread_char_' nm], codes(t));
    fid = fopen(f); t = fread(fid, inf, 'char=>char')'; fclose(fid);
    chk(['fread_c2c_' nm], codes(t));
    fid = fopen(f); t = fread(fid, 'char')'; fclose(fid);
    chk(['fread_dbl_' nm], codes(t));
    fid = fopen(f); t = fread(fid, '*uint8')'; fclose(fid);
    chk(['fread_u8_' nm], codes(t));
    fid = fopen(f); c = textscan(fid, '%s'); fclose(fid);
    chk(['textscan_' nm], codes(strjoin(c{1}', ' # ')));
    fid = fopen(f, 'r'); t = fgetl(fid); fclose(fid);
    chk(['r_fgetl_' nm], codes(t));
    fid = fopen(f, 'rt'); t = fgetl(fid); fclose(fid);
    chk(['rt_fgetl_' nm], codes(t));
    fid = fopen(f, 'r', 'n', 'windows-1252'); [~, ~, ~, enc] = fopen(fid); t = fgetl(fid); fclose(fid);
    chk(['named1252_fgetl_' nm], [enc ' # ' codes(t)]);
    fid = fopen(f); t = fread(fid, 2, '*char')'; t2 = fgetl(fid); fclose(fid);
    chk(['partial_' nm], [codes(t) '/' codes(t2)]);
end
f = fullfile(lib, 'utf8.txt');
fid = fopen(f); t = fread(fid, [1 2], '*char'); p = ftell(fid); fclose(fid);
chk('ftell_after_chars', sprintf('%s # %d', codes(t), p));
fid = fopen(f); t = fgetl(fid); p = ftell(fid); fclose(fid);
chk('ftell_after_line', sprintf('%s # %d', codes(t), p));
fid = fopen(f); c1 = fread(fid, 1, '*char'); [t, n] = fread(fid, inf, '*char'); fclose(fid);
chk('fread_count', sprintf('%s # %s # %d', codes(c1), codes(t'), n));
precisions = {'uchar', 'schar', 'char*1', 'uint8=>char', 'int8', 'char=>uint8', 'uchar=>char', '*uchar', 'int8=>char'};
for k = 1:numel(precisions)
    fid = fopen(f); t = fread(fid, inf, precisions{k})'; fclose(fid);
    chk(sprintf('precision_%d', k), sprintf('%s # %s # %s', precisions{k}, class(t), codes(t)));
end
fid = fopen(f);
try
    t = fread(fid, inf, 'bogus');
    chk('precision_bogus', codes(t));
catch e
    chk('precision_bogus', ['ERR ' e.identifier ' ' e.message]);
end
fclose(fid);
fid = fopen(fullfile(lib, 'cp1252.txt'), 'r+'); [~, ~, ~, enc] = fopen(fid); t = fgetl(fid); fclose(fid);
chk('rplus', [enc ' # ' codes(t)]);
fid = fopen(fullfile(lib, 'cp1252.txt')); fseek(fid, 1, 'bof'); t = fgetl(fid); fclose(fid);
chk('seek_then_line', codes(t));
fid = fopen(fullfile(lib, 'new.txt'), 'w'); [~, ~, ~, enc] = fopen(fid); fclose(fid);
chk('enc_w', enc);
fid = fopen(fullfile(lib, 'empty.txt'), 'w'); fclose(fid);
fid = fopen(fullfile(lib, 'empty.txt')); [~, ~, ~, enc] = fopen(fid); t = fgetl(fid); fclose(fid);
chk('enc_empty', [enc ' # ' codes(t)]);
encs = {'latin1', 'ISO-8859-1', 'US-ASCII', 'UTF-16BE', 'UTF-16LE', 'UTF-8', 'windows-1252', 'cp1252', ''};
for k = 1:numel(encs)
    fid = fopen(f, 'r', 'n', encs{k}); [~, ~, ~, enc] = fopen(fid); t = fgetl(fid); fclose(fid);
    chk(sprintf('named_%d', k), sprintf('%s # %s # %s', encs{k}, enc, codes(t)));
end
rmdir(lib, 's');

% --- fwrite to the console ----------------------------------------------------------------------
chk('fw1', esc(evalc('fwrite(1, ''abc'')')));
chk('fw1_codes', esc(evalc('fwrite(1, [72 105 10])')));
chk('fw1_count', esc(evalc('c = fwrite(1, ''xyz''); fprintf(''<%d>'', c)')));
chk('fw1_nul', codes(evalc('fwrite(1, [65 0 66])')));
chk('fw1_uint16', codes(evalc('fwrite(1, [65 66], ''uint16'')')));
chk('fw1_double', codes(evalc('fwrite(1, 65.7)')));
chk('fw1_high', codes(evalc('fwrite(1, char(233))')));
chk('fw1_neg', codes(evalc('fwrite(1, -1)')));
chk('fw1_string', codes(evalc('fwrite(1, "ab")')));
chk('fw1_logical', codes(evalc('fwrite(1, true)')));
chk('fw2', esc(evalc('fwrite(2, ''err'')')));
chk('fw2_count', esc(evalc('c = fwrite(2, ''e2''); fprintf(''<%d>'', c)')));
chk('fprintf2', esc(evalc('fprintf(2, ''to err'')')));
tries = {'fwrite(0, ''a'')', 'fwrite(1, {1})', 'fwrite(1, ''a'', ''bogus'')', 'fwrite(1, ''abc'', ''char'', 1)', '[c, d] = fwrite(1, ''a'')'};
for k = 1:numel(tries)
    try
        evalc(tries{k});
        chk(sprintf('fw_refused_%d', k), 'none');
    catch e
        chk(sprintf('fw_refused_%d', k), [e.identifier ' ' e.message]);
    end
end

% --- the metaclass of a built-in class, and MException's read-only properties ------------------
names = {'MException', 'double', 'char', 'cell', 'struct', 'function_handle', 'logical', 'string', 'int8', 'single', 'uint64'};
for k = 1:numel(names)
    mc = eval(['?' names{k}]);
    chk(['meta_' names{k}], sprintf('%s # %s # %s', class(mc), mat2str(size(mc)), mc.Name));
end
mc = ?MException;
p = mc.PropertyList(strcmp({mc.PropertyList.Name}, 'Correction'));
chk('meta_mex_correction', [p.SetAccess ' # ' p.GetAccess]);
p = mc.PropertyList(strcmp({mc.PropertyList.Name}, 'message'));
chk('meta_mex_message', [p.SetAccess ' # ' p.GetAccess]);
chk('meta_fromname', [class(meta.class.fromName('MException')) ' # ' meta.class.fromName('MException').Name]);
chk('meta_fromname_double', meta.class.fromName('double').Name);
me = MException('oi318:a', 'b');
fields = {'Correction', 'message', 'identifier', 'stack'};
for k = 1:numel(fields)
    try
        me.(fields{k}) = 5;
        chk(['mex_set_' fields{k}], 'set');
    catch e
        chk(['mex_set_' fields{k}], [e.identifier ' ' e.message]);
    end
end

% --- checkcode's ignored arguments --------------------------------------------------------------
lib = tempname;
mkdir(lib);
fid = fopen(fullfile(lib, 'oi318unused.m'), 'w'); fprintf(fid, 'function oi318unused(a)\nx = 1;\nend\n'); fclose(fid);
lastwarn('');
r = checkcode({fullfile(lib, 'oi318unused.m')}, 'extra');
[m, id] = lastwarn;
chk('checkcode_unused', sprintf('%s # %d # %s # %s', class(r), numel(r), id, m));
warning('error', 'MATLAB:mlint:UnusedArgs');
try
    r = checkcode({fullfile(lib, 'oi318unused.m')}, 'extra');
    chk('checkcode_unused_error_state', class(r));
catch e
    chk('checkcode_unused_error_state', ['ERR ' e.identifier ' ' e.message]);
end
warning('on', 'MATLAB:mlint:UnusedArgs');
rmdir(lib, 's');

% --- an identifier's message is a format with nothing to read -----------------------------------
msgs = {'x%%>', 'x %d', '50%', '100%% sure'};
for k = 1:numel(msgs)
    try
        error('oi318:m', msgs{k});
    catch e
        chk(sprintf('error_format_%d', k), e.message);
    end
end
lastwarn('');
warning('off', 'oi318:w');
warning('oi318:w', 'x%%y');
[m, ~] = lastwarn;
chk('warning_format', m);
warning('on', 'oi318:w');
chk('mexception_format', MException('oi318:x', 'p%%q').message);

function t = lines(t)
t = strjoin(splitlines(t), '/');
end

function t = codes(t)
t = mat2str(double(t));
end

function t = esc(t)
t = strrep(t, newline, '\n');
end

function chk(name, v, rule)
if nargin < 3
    rule = 'exact';
end
fprintf('CHK|%s|%s|%s\n', name, v, rule);
end
