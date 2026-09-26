% probe_shrlib_load: loadlibrary forms, outputs, prototype files, reloading, errors, listings.
a = ip_assets();
addpath(a.here);
addpath(a.root);
% loadlibrary writes the prototype file and the thunk library into the CURRENT folder whatever
% folder mfilename names, so the first load runs from the staged interop folder.
here = pwd; cd(a.root);
lib = 'jgtestlib';

ip_pr('libisloaded.before', 'libisloaded(lib)');
ip_pr('mexext', 'mexext');
ip_pr('compiler', 'mex.getCompilerConfigurations(''C'', ''Selected'').Name');

% ---- the first load, with a prototype file written next to the staged assets
t0 = tic;
try
    [nf, wn] = loadlibrary(a.lib, a.header, 'mfilename', fullfile(a.root, 'jgtestlib_proto'));
    fprintf('load.seconds\t%.1f\n', toc(t0));
    ip_pr('load.notfound', 'nf');
    ip_pr('load.warnings', 'wn');
catch e
    fprintf('load\tERR %s %s\n', e.identifier, strrep(e.message, newline, ' | '));
end
cd(here);
ip_pr('libisloaded.after', 'libisloaded(lib)');
ip_pr('proto.written', 'exist(fullfile(a.root, ''jgtestlib_proto.m''), ''file'')');
ip_pr('proto.written.probe.folder', 'exist(fullfile(a.here, ''jgtestlib_proto.m''), ''file'')');
ip_pr('proto.thunk', '{dir(fullfile(a.root, ''*thunk*'')).name}');
ip_pr('libfunctions', 'libfunctions(lib)');
ip_px('libfunctions.disp', 'libfunctions(lib)');
ip_px('libfunctions.full', 'libfunctions(lib, ''-full'')');
ip_px('libfunctions.cmd', 'libfunctions jgtestlib');
ip_pr('libmethods', 'numel(libmethods(lib))');
ip_pr('libfunctions.unloaded', 'libfunctions(''nosuchlib'')');
ip_pr('libfunctions.nooutput.numel', 'numel(libfunctions(lib))');

% ---- a second load of the same library
ip_px('load.twice', '[nf2, wn2] = loadlibrary(a.lib, a.header)');
ip_px('load.twice.name', 'loadlibrary(lib, a.header)');

% ---- unload and reload through the prototype file
ip_px('unload', 'unloadlibrary(lib)');
ip_pr('libisloaded.unloaded', 'libisloaded(lib)');
ip_px('unload.again', 'unloadlibrary(lib)');
ip_px('unload.cmd.form', 'unloadlibrary jgtestlib');
t0 = tic;
ip_px('load.proto', 'loadlibrary(a.lib, @jgtestlib_proto)');
fprintf('load.proto.seconds\t%.1f\n', toc(t0));
ip_pr('libisloaded.proto', 'libisloaded(lib)');
ip_pr('libfunctions.proto.n', 'numel(libfunctions(lib))');
ip_pr('call.after.proto', 'calllib(lib, ''jg_version'')');
unloadlibrary(lib);

% ---- aliases
ip_px('load.alias', 'loadlibrary(a.lib, a.header, ''alias'', ''jgt'')');
ip_pr('alias.isloaded', 'libisloaded(''jgt'')');
ip_pr('alias.isloaded.real', 'libisloaded(lib)');
ip_pr('alias.call', 'calllib(''jgt'', ''jg_version'')');
ip_px('alias.second', 'loadlibrary(a.lib, a.header, ''alias'', ''jgt2'')');
ip_pr('alias.second.call', 'calllib(''jgt2'', ''jg_version'')');
unloadlibrary('jgt'); unloadlibrary('jgt2');

% ---- name forms: no extension, no path, header by name only
here = pwd; cd(a.root);
ip_px('load.bare.names', 'loadlibrary(''jgtestlib'', ''jgtestlib.h'')');
ip_pr('bare.isloaded', 'libisloaded(lib)');
unloadlibrary(lib);
ip_px('load.lib.only', 'loadlibrary(''jgtestlib'')');
ip_pr('lib.only.isloaded', 'libisloaded(lib)');
if libisloaded(lib), unloadlibrary(lib); end
cd(here);

% ---- addheader / includepath with the windows.h header
t0 = tic;
ip_px('load.win', '[nfw, wnw] = loadlibrary(a.lib, a.winheader)');
fprintf('load.win.seconds\t%.1f\n', toc(t0));
ip_pr('win.functions.n', 'numel(libfunctions(lib))');
ip_pr('win.has.tick', 'any(strcmp(libfunctions(lib), ''jg_tick''))');
ip_pr('win.has.GetTickCount', 'any(strcmp(libfunctions(lib), ''GetTickCount''))');
ip_pr('win.call.tick', 'class(calllib(lib, ''jg_tick''))');
ip_pr('win.call.is_even', 'calllib(lib, ''jg_is_even'', uint32(4))');
unloadlibrary(lib);
ip_px('load.addheader', 'loadlibrary(a.lib, a.winheader, ''addheader'', ''jgtestlib'')');
ip_pr('addheader.functions.n', 'numel(libfunctions(lib))');
unloadlibrary(lib);
ip_px('load.includepath', 'loadlibrary(a.lib, ''jgtestlib_win.h'', ''includepath'', a.root)');
if libisloaded(lib), unloadlibrary(lib); end
ip_px('load.kernel32', 'loadlibrary(''kernel32'', a.winheader, ''alias'', ''k32'')');
if libisloaded('k32')
    ip_pr('kernel32.functions.n', 'numel(libfunctions(''k32''))');
    ip_pr('kernel32.GetTickCount64', 'class(calllib(''k32'', ''GetTickCount64''))');
    unloadlibrary('k32');
end

% ---- errors
ip_px('err.missing.lib', 'loadlibrary(''C:\no\such\lib.dll'', a.header)');
ip_px('err.missing.header', 'loadlibrary(a.lib, ''C:\no\such\header.h'')');
ip_px('err.no.args', 'loadlibrary()');
ip_px('err.numeric', 'loadlibrary(5, a.header)');
ip_px('err.bad.option', 'loadlibrary(a.lib, a.header, ''nosuchoption'', 1)');
ip_px('err.managed.dll', 'loadlibrary(a.assembly, a.header, ''alias'', ''asm'')');
if libisloaded('asm'), unloadlibrary('asm'); end
ip_px('err.unload.never', 'unloadlibrary(''neverloaded'')');
ip_px('err.calllib.unloaded', 'calllib(''neverloaded'', ''f'')');
ip_px('err.header.syntax', 'ip_write(fullfile(tempdir, ''bad_jg.h''), ''int broken(;''); loadlibrary(a.lib, fullfile(tempdir, ''bad_jg.h''), ''alias'', ''badh'')');
if libisloaded('badh'), unloadlibrary('badh'); end

% ---- thunkfilename and the -batch listing view
ip_px('load.thunkfilename', 'loadlibrary(a.lib, a.header, ''thunkfilename'', fullfile(tempdir, ''jg_custom_thunk''))');
ip_pr('thunk.custom.written', 'numel(dir(fullfile(tempdir, ''jg_custom_thunk*'')))');
if libisloaded(lib), unloadlibrary(lib); end
