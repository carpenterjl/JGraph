% probe_shrlib_warnids: the identifier of each warning loadlibrary raises (interop plan, stage 8).
% Each case loads jgtestlib.dll through a hand-written prototype file (or a one-line header) that
% provokes exactly one kind of warning, so lastwarn names it.
a = ip_assets();
addpath(a.here);
addpath(a.root);
top = tempname; mkdir(top);
here = pwd;
cd(top);
addpath(top);
thunk = fullfile(a.root, 'jgtestlib_thunk_pcwin64');

head = ['function [methodinfo,structs,enuminfo,ThunkLibName]=@NAME@' newline ...
    'ival={cell(1,0)};structs=[];enuminfo=[];fcnNum=1;' newline ...
    'fcns=struct(''name'',ival,''calltype'',ival,''LHS'',ival,''RHS'',ival,''alias'',ival,''thunkname'', ival);' newline ...
    'ThunkLibName=''' thunk ''';' newline];
version = ['fcns.thunkname{fcnNum}=''int32voidThunk'';fcns.name{fcnNum}=''jg_version''; fcns.calltype{fcnNum}=''Thunk''; fcns.LHS{fcnNum}=''int32''; fcns.RHS{fcnNum}=[];fcnNum=fcnNum+1;' newline];
tail = ['methodinfo=fcns;' newline];

cases = {
    'pw_error', ['fcns.thunkname{fcnNum}=''int32jg_unionThunk'';fcns.name{fcnNum}=''jg_union_in''; fcns.calltype{fcnNum}=''Thunk''; fcns.LHS{fcnNum}=''int32''; fcns.RHS{fcnNum}={''error''};fcnNum=fcnNum+1;' newline]
    'pw_structret', ['fcns.thunkname{fcnNum}=''jg_pointdoubledoubleThunk'';fcns.name{fcnNum}=''jg_point_make''; fcns.calltype{fcnNum}=''Thunk''; fcns.LHS{fcnNum}=''jg_point''; fcns.RHS{fcnNum}={''double'', ''double''};fcnNum=fcnNum+1;' newline 'structs.jg_point.members=struct(''x'', ''double'', ''y'', ''double'');' newline]
    'pw_member', [version 'structs.pw_s.members=struct(''f'', ''FcnPtr'');' newline]
    'pw_nofunctions', ['fcns.thunkname{fcnNum}=''int32voidThunk'';fcns.name{fcnNum}=''pw_absent''; fcns.calltype{fcnNum}=''Thunk''; fcns.LHS{fcnNum}=''int32''; fcns.RHS{fcnNum}=[];fcnNum=fcnNum+1;' newline]
    'pw_unknowntype', ['fcns.thunkname{fcnNum}=''int32voidPtrThunk'';fcns.name{fcnNum}=''jg_is_null''; fcns.calltype{fcnNum}=''Thunk''; fcns.LHS{fcnNum}=''int32''; fcns.RHS{fcnNum}={''nosuchtypePtr''};fcnNum=fcnNum+1;' newline]
    'pw_unknownret', ['fcns.thunkname{fcnNum}=''int32voidThunk'';fcns.name{fcnNum}=''jg_version''; fcns.calltype{fcnNum}=''Thunk''; fcns.LHS{fcnNum}=''nosuchtype''; fcns.RHS{fcnNum}=[];fcnNum=fcnNum+1;' newline]
    'pw_enum', [version 'enuminfo.pw_e=struct(''PW_A'',1,''PW_B'',2);' newline]
    'pw_struct', [version 'structs.pw_t.members=struct(''x'', ''double'');' newline]
    };
for k = 1:size(cases, 1)
    name = [cases{k, 1} '_proto'];
    fid = fopen(fullfile(top, [name '.m']), 'w');
    fprintf(fid, '%s', strrep(head, '@NAME@', name), cases{k, 2}, tail);
    fclose(fid);
end
rehash;

for k = 1:size(cases, 1)
    name = [cases{k, 1} '_proto'];
    al = cases{k, 1};
    lastwarn('');
    try
        out = evalc('loadlibrary(a.lib, str2func(name), ''alias'', al)');
        [m, id] = lastwarn;
        fprintf('%s\t%s\t%s\t%s\n', al, id, strrep(m, newline, ' | '), strtrim(regexprep(out, '\s*\n\s*', ' | ')));
    catch e
        fprintf('%s\tERR %s %s\n', al, e.identifier, strrep(e.message, newline, ' | '));
    end
    if libisloaded(al), unloadlibrary(al); end
end

% the enum and struct type already existing: load the same prototype under two aliases
loadlibrary(a.lib, @pw_enum_proto, 'alias', 'pw_enum_1');
lastwarn('');
out = evalc('loadlibrary(a.lib, @pw_enum_proto, ''alias'', ''pw_enum_2'')');
[m, id] = lastwarn;
fprintf('enum.exists\t%s\t%s\t%s\n', id, strrep(m, newline, ' | '), strtrim(regexprep(out, '\s*\n\s*', ' | ')));
unloadlibrary('pw_enum_1'); unloadlibrary('pw_enum_2');
loadlibrary(a.lib, @pw_struct_proto, 'alias', 'pw_struct_1');
lastwarn('');
out = evalc('loadlibrary(a.lib, @pw_struct_proto, ''alias'', ''pw_struct_2'')');
[m, id] = lastwarn;
fprintf('struct.exists\t%s\t%s\t%s\n', id, strrep(m, newline, ' | '), strtrim(regexprep(out, '\s*\n\s*', ' | ')));
unloadlibrary('pw_struct_1'); unloadlibrary('pw_struct_2');
ip_pr('struct.exists.after.unload', 'libisloaded(''pw_struct_1'')');

% the parse-warnings notice: a header whose only warning is a parse warning
fid = fopen(fullfile(top, 'pw_parse.h'), 'w');
fprintf(fid, 'struct pw_q;\n__declspec(dllimport) int jg_is_null(struct pw_q *p);\n');
fclose(fid);
lastwarn('');
try
    out = evalc('loadlibrary(a.lib, ''pw_parse.h'', ''alias'', ''pw_parse'')');
    [m, id] = lastwarn;
    fprintf('parse.notice\t%s\t%s\t%s\n', id, strrep(m, newline, ' | '), strtrim(regexprep(out, '\s*\n\s*', ' | ')));
catch e
    fprintf('parse.notice\tERR %s %s\n', e.identifier, strrep(e.message, newline, ' | '));
end
if libisloaded('pw_parse')
    ip_px('parse.notice.full', 'libfunctions(''pw_parse'', ''-full'')');
    unloadlibrary('pw_parse');
end
lastwarn('');
[nf, w] = loadlibrary(a.lib, 'pw_parse.h', 'alias', 'pw_parse2');
[m, id] = lastwarn;
fprintf('parse.notice.with.outputs\t%s\t%s\n', id, strrep(m, newline, ' | '));
fprintf('parse.notice.warnings.begin\n%s\nparse.notice.warnings.end\n', w);
fprintf('parse.notice.warnings.escaped\t%s\n', strrep(strrep(w, char(13), '\r'), newline, '\n'));
ip_pr('parse.notice.nf', 'nf');
unloadlibrary('pw_parse2');

% a header whose preprocessing fails
fid = fopen(fullfile(top, 'pw_bad.h'), 'w');
fprintf(fid, '#include "no_such_include.h"\nint jg_version(void);\n');
fclose(fid);
ip_px('cpp.failure', 'loadlibrary(a.lib, ''pw_bad.h'', ''alias'', ''pw_bad'')');
if libisloaded('pw_bad'), unloadlibrary('pw_bad'); end

cd(here);
