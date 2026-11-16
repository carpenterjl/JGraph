% record: -noFigureWindows
% Open item 350 (ADR 0385): fprintf to a file as a statement binds and shows ans (to the console it does
% not); fopen knows R2025b's code pages (their names, their bytes, the substitution byte of a character a page
% does not hold, InvalidEncoding and the UnsupportedEncoding warning); 'a+' reads anywhere and writes at the
% end; fopen(fid) names a permission's 'b'; ?MException lists R2025b's ten properties.
d = tempname;
mkdir(d);
f = fullfile(d, 'a.txt');

% --- fprintf as a statement -------------------------------------------------------------------
fid = fopen(f, 'w');
one = 1;
stmts = {
    'file_nosemi', 'fprintf(fid, ''abc'')'
    'file_semi', 'fprintf(fid, ''abc'');'
    'file_vector', 'fprintf(fid, ''%d\n'', [1 2 3])'
    'file_empty', 'fprintf(fid, '''')'
    'file_comma', 'fprintf(fid, ''xy''), 3;'
    'console_1', 'fprintf(1, ''z'')'
    'console_var', 'fprintf(one, ''z'')'
    'console_bare', 'fprintf(''z'')'
    'sprintf_nosemi', 'sprintf(''abc'')'
    'fwrite_file', 'fwrite(fid, ''abc'')'
    'fwrite_console', 'fwrite(1, ''z'')'
    'assigned', 'nb = fprintf(fid, ''q'')'
    };
for k = 1:size(stmts, 1)
    ans = -1; %#ok<NOANS,NASGU>
    o = evalc(stmts{k, 2});
    a = ans; %#ok<NOANS>
    if ischar(a)
        a = ['''' a ''''];
    else
        a = mat2str(a);
    end
    chk(['fprintf_' stmts{k, 1}], sprintf('shows ans %d # ans %s', contains(o, 'ans'), a));
end
fclose(fid);
chk('fprintf_file_text', strrep(fileread(f), newline, '\n'));

% --- 'a+', 'a', the permission's b ------------------------------------------------------------
fid = fopen(f, 'w'); fwrite(fid, 'abc'); fclose(fid);
fid = fopen(f, 'a+');
chk('ap_tell0', sprintf('%d', ftell(fid)));
[x, n] = fread(fid, Inf, '*char');
chk('ap_read_at_end', sprintf('%d # %d', numel(x), n));
c = fwrite(fid, 'xyz');
chk('ap_write', sprintf('%d %d', c, ftell(fid)));
frewind(fid);
chk('ap_read_all', fread(fid, Inf, '*char')');
fseek(fid, 0, 'bof');
c = fwrite(fid, 'Q');
chk('ap_seek_then_write', sprintf('%d %d', c, ftell(fid)));
fseek(fid, 1, 'bof');
x = fread(fid, 1, '*char');
chk('ap_seek_then_read', sprintf('%s %d', x, ftell(fid)));
fseek(fid, 0, 'cof'); % C needs a seek between a read and a write
fprintf(fid, '%d', 42);
frewind(fid);
chk('ap_after_fprintf', fread(fid, Inf, '*char')');
[~, perm] = fopen(fid);
chk('ap_perm', perm);
fclose(fid);
chk('ap_file', fileread(f));
fid = fopen(fullfile(d, 'new.txt'), 'a+');
chk('ap_new_tell', sprintf('%d', ftell(fid)));
fprintf(fid, 'hi');
frewind(fid);
chk('ap_new_read', fread(fid, Inf, '*char')');
fclose(fid);
modes = {'r', 'w', 'a', 'r+', 'w+', 'a+', 'rb', 'wt', 'at+', 'a+t', 'rt'};
for k = 1:numel(modes)
    if modes{k}(1) == 'w'
        g = fullfile(d, 'w.txt');
    else
        g = f;
    end
    fid = fopen(g, modes{k});
    [~, perm] = fopen(fid);
    [x, n] = fread(fid, 2);
    chk(['perm_' regexprep(modes{k}, '\W', '_')], sprintf('%s # %s # %d', perm, num2str(x(:)'), n));
    fclose(fid);
end

% --- fopen's code pages -------------------------------------------------------------------------
s = ['A' char(233) char(1046) char(26085) char(8364)];
encs = {'Big5', 'GB18030', 'GB2312', 'GBK', 'EUC-KR', 'windows-949', 'Shift_JIS', 'IBM866', 'KOI8-R', 'KOI8-U', 'Macintosh', 'US-ASCII', 'UTF-8', ...
    'ISO-8859-1', 'ISO-8859-2', 'ISO-8859-3', 'ISO-8859-4', 'ISO-8859-5', 'ISO-8859-6', 'ISO-8859-7', 'ISO-8859-8', 'ISO-8859-9', 'ISO-8859-10', ...
    'ISO-8859-11', 'ISO-8859-13', 'ISO-8859-14', 'ISO-8859-15', 'ISO-8859-16', 'windows-874', 'windows-932', 'windows-936', 'windows-950', ...
    'windows-1250', 'windows-1251', 'windows-1252', 'windows-1253', 'windows-1254', 'windows-1255', 'windows-1256', 'windows-1257', 'windows-1258', ...
    'UTF-16LE', 'UTF-32LE', 'UTF-32BE', 'latin1', 'sjis', 'cp932', 'shift-jis', 'ascii', 'utf8', 'cp1251', 'CP1252', 'ISO8859-1', 'Latin-1', ...
    'gb2312', 'big5', 'euc-kr', 'koi8-r', 'ms932', 'cp936', 'x-mac-roman', 'IBM437', 'cp437', 'IBM850', 'windows-31j', 'ISO-2022-JP', 'EUC-CN', ...
    'TIS-620', 'bogus-enc', 'system', 'native', ...
    'Big5-HKSCS', 'EUC-JP', 'EUC-TW', 'CP949', 'cp950', 'UTF-16', 'UTF-16BE', 'UTF-32', 'UTF-7'};
diverging = {'Big5-HKSCS', 'EUC-JP', 'EUC-TW', 'CP949', 'cp950', 'UTF-16', 'UTF-16BE', 'UTF-32', 'UTF-7', 'EUC-KR', 'euc-kr'};
for k = 1:numel(encs)
    g = fullfile(d, sprintf('e%d.txt', k));
    rule = 'exact';
    if any(strcmp(encs{k}, diverging))
        rule = 'div=ADR0385';
    end
    key = ['enc_' regexprep(encs{k}, '\W', '_')];
    lastwarn('');
    try
        fid = fopen(g, 'w', 'n', encs{k});
        [~, wid] = lastwarn;
        [~, ~, ~, en] = fopen(fid);
        fprintf(fid, '%s', s);
        fclose(fid);
        h = fopen(g, 'r');
        b = fread(h, Inf, 'uint8')';
        fclose(h);
        h = fopen(g, 'r', 'n', encs{k});
        t = fread(h, Inf, '*char')';
        fclose(h);
        chk(key, sprintf('%s # %s # %s # %s', en, wid, mat2str(b), mat2str(double(t))), rule);
    catch e
        chk(key, ['ERR ' e.identifier ' ' e.message], rule);
    end
end
lastwarn('');
fid = fopen(fullfile(d, 'u.txt'), 'w', 'n', 'IBM437');
[m, id] = lastwarn;
fclose(fid);
chk('enc_unsupported_warning', sprintf('%s # %s', id, strrep(m, newline, ' / ')));
rmdir(d, 's');

% --- ?MException --------------------------------------------------------------------------------
mc = ?MException;
p = mc.PropertyList;
chk('mex_nprops', sprintf('%d', numel(p)));
for k = 1:numel(p)
    q = p(k);
    chk(sprintf('mex_prop%d', k), sprintf('%s # hidden=%d # get=%s # set=%s # dependent=%d # constant=%d # hasdefault=%d # %s', ...
        q.Name, q.Hidden, q.GetAccess, q.SetAccess, q.Dependent, q.Constant, q.HasDefault, q.DefiningClass.Name));
end
chk('mex_visible', strjoin({p(~[p.Hidden]).Name}, ','));
chk('mex_properties_name', strjoin(properties('MException')', ','));
chk('mex_properties_value', strjoin(properties(MException('oi350:a', 'b'))', ','));
chk('mex_fromname', sprintf('%d', numel(meta.class.fromName('MException').PropertyList)));

function chk(name, v, rule)
if nargin < 3
    rule = 'exact';
end
fprintf('CHK|%s|%s|%s\n', name, v, rule);
end
