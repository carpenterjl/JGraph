% probe_shrlib_parse: what R2025b's header parser records for C declarations jgtestlib.h does not
% cover (interop plan, stage 8): typedef'd tags, anonymous structs, enum values and pointers,
% wchar_t, long double, __stdcall, arrays, externs, which structs and enums reach the prototype
% file, addheader, the warning identifiers loadlibrary raises, the loadlibrary / libisloaded /
% libfunctions / unloadlibrary refusals, mexext, and mex.getCompilerConfigurations.
a = ip_assets();
addpath(a.here);
addpath(a.root); % jgtestlib_proto and its thunk library
top = tempname; mkdir(top);
copyfile(fullfile(a.here, 'probe_shrlib_parse.h'), top);
copyfile(fullfile(a.here, 'probe_shrlib_parse_inc.h'), top);
here = pwd;
cd(top);

% ---- the prototype file R2025b writes for the probe header (every function is notfound)
lastwarn('');
[nf, w] = loadlibrary(a.lib, 'probe_shrlib_parse.h', 'alias', 'qp', 'mfilename', 'qp_proto');
[lm, lid] = lastwarn;
fprintf('parse.lastwarn\t%s\t%s\n', lid, strrep(lm, newline, ' | '));
ip_pr('parse.notfound.size', 'size(nf)');
ip_pr('parse.notfound', 'nf''');
fprintf('parse.warnings.begin\n%s\nparse.warnings.end\n', w);
fprintf('proto.begin\n%s\nproto.end\n', fileread(fullfile(top, 'qp_proto.m')));
ip_pr('parse.libfunctions', 'libfunctions(''qp'')');
unloadlibrary('qp');

% ---- addheader: with and without the extension
[nf2, ~] = loadlibrary(a.lib, 'probe_shrlib_parse.h', 'alias', 'qa', 'addheader', 'probe_shrlib_parse_inc');
ip_pr('addheader.noext.has_inc', 'any(strcmp(nf2, ''q_inc_fn''))');
ip_pr('addheader.noext.n', 'numel(nf2)');
unloadlibrary('qa');
[nf3, ~] = loadlibrary(a.lib, 'probe_shrlib_parse.h', 'alias', 'qb', 'addheader', 'probe_shrlib_parse_inc.h');
ip_pr('addheader.ext.has_inc', 'any(strcmp(nf3, ''q_inc_fn''))');
unloadlibrary('qb');
ip_pr('noaddheader.has_inc', 'any(strcmp(nf, ''q_inc_fn''))');

% ---- warning identifiers: the header route, silencing each identifier found in turn
for k = 1:8
    lastwarn('');
    al = sprintf('qw%d', k);
    evalc('loadlibrary(a.lib, ''probe_shrlib_parse.h'', ''alias'', al)');
    [m, id] = lastwarn;
    if libisloaded(al), unloadlibrary(al); end
    if isempty(m), break; end
    fprintf('warnid.header.%d\t%s\t%s\n', k, id, strrep(m, newline, ' | '));
    if isempty(id), break; end
    warning('off', id);
end
warning('on', 'all');

% ---- warning identifiers: the prototype route with jgtestlib
for k = 1:8
    lastwarn('');
    al = sprintf('pw%d', k);
    evalc('loadlibrary(a.lib, @jgtestlib_proto, ''alias'', al)');
    [m, id] = lastwarn;
    if libisloaded(al), unloadlibrary(al); end
    if isempty(m), break; end
    fprintf('warnid.proto.%d\t%s\t%s\n', k, id, strrep(m, newline, ' | '));
    if isempty(id), break; end
    warning('off', id);
end
warning('on', 'all');

% ---- outputs of the prototype route
[nf4, w4] = loadlibrary(a.lib, @jgtestlib_proto, 'alias', 'z1');
ip_pr('proto.nf.size', 'size(nf4)');
ip_pr('proto.w.class', 'class(w4)');
ip_pr('proto.w.size', 'size(w4)');
ip_px('proto.nargout1', 'x = loadlibrary(a.lib, @jgtestlib_proto, ''alias'', ''z2'')');
if libisloaded('z2'), unloadlibrary('z2'); end
ip_px('calllib.notfound.fn', 'calllib(''z1'', ''jg_not_exported'', 1)');
ip_px('calllib.pointmake', 'calllib(''z1'', ''jg_point_make'', 1, 2)');
ip_px('calllib.nosuchfn', 'calllib(''z1'', ''nosuch'')');
ip_px('calllib.extra.arg', 'calllib(''z1'', ''jg_version'', 1)');
ip_px('calllib.toofew.args', 'calllib(''z1'', ''jg_int32'')');
ip_px('calllib.libonly', 'calllib(''z1'')');
ip_px('calllib.none', 'calllib()');
ip_px('calllib.numeric.fn', 'calllib(''z1'', 5)');
ip_pr('calllib.void.class', 'class(evalc(''calllib(''''z1'''', ''''jg_void'''')''))');
ip_px('calllib.void.out', 'x = calllib(''z1'', ''jg_void'')');
ip_px('calllib.echo', 'calllib(''z1'', ''jg_version'')');
ip_pr('libfunctions.lib.prefix', 'numel(libfunctions(''lib.z1''))');
ip_px('methods.lib', 'methods(''lib.z1'')');
ip_px('libfunctions.cmd.full', 'libfunctions z1 -full');
ip_px('libfunctions.bad.flag', 'libfunctions(''z1'', ''-bogus'')');
unloadlibrary('z1');

% ---- loadlibrary refusals and option forms
ip_px('load.both.missing', 'loadlibrary(''C:\no\lib.dll'', ''C:\no\h.h'')');
ip_px('load.proto.missing', 'loadlibrary(a.lib, @no_such_proto_fn, ''alias'', ''np'')');
ip_px('load.odd.options', 'loadlibrary(a.lib, @jgtestlib_proto, ''alias'')');
ip_px('load.alias.numeric', 'loadlibrary(a.lib, @jgtestlib_proto, ''alias'', 5)');
ip_px('load.option.case', 'loadlibrary(a.lib, @jgtestlib_proto, ''ALIAS'', ''caseal'')');
ip_pr('load.option.case.loaded', 'libisloaded(''caseal'')');
if libisloaded('caseal'), unloadlibrary('caseal'); end
ip_px('load.option.string', 'loadlibrary(a.lib, @jgtestlib_proto, "alias", "stral")');
ip_pr('load.option.string.loaded', 'libisloaded(''stral'')');
if libisloaded('stral'), unloadlibrary('stral'); end
ip_px('load.proto.as.char', 'loadlibrary(a.lib, ''jgtestlib_proto'', ''alias'', ''pc'')');
if libisloaded('pc'), unloadlibrary('pc'); end
ip_px('load.proto.mfilename', 'loadlibrary(a.lib, @jgtestlib_proto, ''mfilename'', ''x_proto'', ''alias'', ''pm'')');
ip_pr('load.proto.mfilename.written', 'isfile(fullfile(top, ''x_proto.m''))');
if libisloaded('pm'), unloadlibrary('pm'); end
ip_px('load.header.cell.addheader', 'loadlibrary(a.lib, ''probe_shrlib_parse.h'', ''alias'', ''qc'', ''addheader'', {''probe_shrlib_parse_inc''})');
if libisloaded('qc'), unloadlibrary('qc'); end
ip_px('load.includepath.missing', 'loadlibrary(a.lib, ''probe_shrlib_parse.h'', ''alias'', ''qi'', ''includepath'', ''C:\no\such\folder'')');
if libisloaded('qi'), unloadlibrary('qi'); end
copyfile(a.lib, fullfile(top, 'solo.dll'));
ip_px('load.lib.only.noheader', 'loadlibrary(''solo'')');
if libisloaded('solo'), unloadlibrary('solo'); end
ip_px('load.lib.only.header.on.path', 'loadlibrary(a.lib)');
if libisloaded('jgtestlib'), unloadlibrary('jgtestlib'); end
ip_px('load.header.dir', 'loadlibrary(a.lib, top, ''alias'', ''hd'')');
if libisloaded('hd'), unloadlibrary('hd'); end

% ---- libisloaded / libfunctions / unloadlibrary refusals
ip_pr('libisloaded.numeric', 'libisloaded(5)');
ip_pr('libisloaded.none', 'libisloaded()');
ip_pr('libisloaded.two', 'libisloaded(''a'', ''b'')');
ip_pr('libisloaded.cell', 'libisloaded({''a''})');
ip_pr('libisloaded.empty', 'libisloaded('''')');
ip_px('libfunctions.none', 'libfunctions()');
ip_px('libfunctions.unloaded.disp', 'libfunctions(''nosuchlib'')');
ip_px('libfunctions.unloaded.full', 'libfunctions(''nosuchlib'', ''-full'')');
ip_pr('libfunctions.unloaded.full.out', 'libfunctions(''nosuchlib'', ''-full'')');
ip_px('libfunctions.numeric', 'libfunctions(5)');
ip_px('unload.none', 'unloadlibrary()');
ip_px('unload.numeric', 'unloadlibrary(5)');
ip_px('unload.two', 'unloadlibrary(''a'', ''b'')');

% ---- mexext
ip_pr('mexext', 'mexext');
ip_pr('mexext.all.class', 'class(mexext(''all''))');
ip_pr('mexext.all.size', 'size(mexext(''all''))');
ip_pr('mexext.all.fields', 'fieldnames(mexext(''all''))''');
ip_px('mexext.all.list', 's = mexext(''all''); for k = 1:numel(s), fprintf(''%s=%s\n'', s(k).arch, s(k).ext); end');
ip_px('mexext.bad', 'mexext(''x'')');
ip_px('mexext.numeric', 'mexext(5)');
ip_px('mexext.two', 'mexext(''all'', 1)');

% ---- mex.getCompilerConfigurations and the missing-compiler sentence
c = mex.getCompilerConfigurations('C', 'Selected');
ip_pr('cc.class', 'class(c)');
ip_pr('cc.size', 'size(c)');
props = properties(c);
ip_pr('cc.props', 'props''');
for k = 1:numel(props)
    ip_pr(['cc.' props{k}], ['c.' props{k}]);
end
ip_px('cc.disp', 'c');
ip_pr('cc.details.class', 'class(c.Details)');
dprops = properties(c.Details);
for k = 1:numel(dprops)
    ip_pr(['cc.details.' dprops{k}], ['c.Details.' dprops{k}]);
end
ip_pr('cc.C.names', '{mex.getCompilerConfigurations(''C'').Name}');
ip_pr('cc.C.installed.names', '{mex.getCompilerConfigurations(''C'', ''Installed'').Name}');
ip_pr('cc.C.supported.n', 'numel(mex.getCompilerConfigurations(''C'', ''Supported''))');
ip_pr('cc.all.names', '{mex.getCompilerConfigurations().Name}');
ip_pr('cc.all.langs', '{mex.getCompilerConfigurations().Language}');
ip_pr('cc.any.names', '{mex.getCompilerConfigurations(''Any'').Name}');
ip_pr('cc.cpp.selected', '{mex.getCompilerConfigurations(''C++'', ''Selected'').Name}');
ip_pr('cc.lower', '{mex.getCompilerConfigurations(''c'', ''selected'').Name}');
ip_px('cc.bad.lang', 'mex.getCompilerConfigurations(''Cobol'')');
ip_px('cc.bad.list', 'mex.getCompilerConfigurations(''C'', ''Nope'')');
ip_pr('cc.noarg.class', 'class(mex.getCompilerConfigurations())');
ip_pr('msg.nocompiler.link', 'getString(message(''MATLAB:mex:NoCompilerFound_link_Win64''))');
ip_pr('msg.nocompiler', 'getString(message(''MATLAB:mex:NoCompilerFound_Win64''))');

cd(here);
