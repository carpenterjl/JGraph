% probe_shrlib_readback: can R2025b load a prototype file JGraph wrote (interop plan, stage 8)?
% jgraph_written_proto.m is JGraph's loadlibrary(jgtestlib.dll, jgtestlib.h, 'mfilename', …) output.
% R2025b calls a 'Thunk' function through the thunk library ThunkLibName names, which JGraph never
% builds; this asks what happens as written, beside R2025b's own thunk for the same header, with
% ThunkLibName empty, and with every call type 'cdecl'. It also records the identifier of the
% "warnings were produced while parsing" notice.
a = ip_assets();
addpath(a.here);
top = tempname; mkdir(top);
here = pwd;
cd(top);
addpath(top);
written = fileread(fullfile(a.here, 'jgraph_written_proto.m'));

variants = {
    'rb_as_written', written
    'rb_empty_thunk', regexprep(written, 'ThunkLibName=[^\n]*', 'ThunkLibName='''';', 'once')
    'rb_cdecl', regexprep(regexprep(regexprep(written, 'ThunkLibName=[^\n]*', 'ThunkLibName='''';', 'once'), ...
        'fcns\.thunkname\{fcnNum\}=''[^'']*'';', ''), 'calltype\{fcnNum\}=''Thunk''', 'calltype{fcnNum}=''cdecl''')
    };
for k = 1:size(variants, 1)
    name = variants{k, 1};
    text = regexprep(variants{k, 2}, 'jgraph_written_proto', [name '_proto'], 'once');
    fid = fopen(fullfile(top, [name '_proto.m']), 'w'); fprintf(fid, '%s', text); fclose(fid);
end
% the file as written, next to R2025b's own thunk for jgtestlib.h
copyfile(fullfile(a.root, 'jgtestlib_thunk_pcwin64.*'), top);
fid = fopen(fullfile(top, 'rb_with_thunk_proto.m'), 'w');
fprintf(fid, '%s', regexprep(written, 'jgraph_written_proto', 'rb_with_thunk_proto', 'once'));
fclose(fid);
rehash;

names = [variants(:, 1); {'rb_with_thunk'}];
for k = 1:numel(names)
    al = names{k};
    lastwarn('');
    try
        evalc('[nf, w] = loadlibrary(a.lib, str2func([al ''_proto'']), ''alias'', al);');
        fprintf('%s.load\tOK notfound=%s\n', al, strjoin(nf, ','));
        ip_pr([al '.n'], 'numel(libfunctions(al))');
        ip_pr([al '.version'], 'calllib(al, ''jg_version'')');
        ip_pr([al '.double'], 'calllib(al, ''jg_double'', 2.5)');
        ip_pr([al '.greeting'], 'calllib(al, ''jg_greeting'')');
        ip_pr([al '.strlen'], '{calllib(al, ''jg_strlen'', ''abcd'')}');
        ip_pr([al '.color'], 'calllib(al, ''jg_color_next'', ''JG_RED'')');
    catch e
        fprintf('%s.load\tERR %s %s\n', al, e.identifier, strrep(e.message, newline, ' | '));
    end
    if libisloaded(al), unloadlibrary(al); end
end

% the parse notice's identifier: a header whose only warning is a parse warning
fid = fopen(fullfile(top, 'rb_notice.h'), 'w');
fprintf(fid, 'typedef union rb_u { int i; float f; } rb_u;\n__declspec(dllimport) int jg_version(void);\n');
fclose(fid);
lastwarn('');
out = evalc('loadlibrary(a.lib, ''rb_notice.h'', ''alias'', ''rbn'')');
[m, id] = lastwarn;
fprintf('notice\t%s\t%s\t%s\n', id, strrep(m, newline, ' | '), strtrim(regexprep(out, '\s*\n\s*', ' | ')));
if libisloaded('rbn'), unloadlibrary('rbn'); end
lastwarn('');
[nf, w] = loadlibrary(a.lib, 'rb_notice.h', 'alias', 'rbn2');
[m, id] = lastwarn;
fprintf('notice.with.outputs\t%s\t%s\n', id, strrep(m, newline, ' | '));
fprintf('notice.warnings.escaped\t%s\n', strrep(strrep(w, char(13), '\r'), newline, '\n'));
if libisloaded('rbn2'), unloadlibrary('rbn2'); end

cd(here);
