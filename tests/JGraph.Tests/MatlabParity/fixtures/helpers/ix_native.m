function out = ix_native(kind)
% IX_NATIVE  The cases of interop_host_crash and interop_env_sync (interop plan, stage 7, ADR 0180):
%   the native host that loadlibrary and calllib will run in, reached through JGraph's test-only
%   jgraph.internal.nativehost. There is no MATLAB counterpart (R2025b runs native code in its own
%   process, and a crash ends MATLAB), so the answers are written from the rule. Answers text.
p = interop_paths();
nh = jgraph.internal.nativehost;
here = pwd;
cd(p.root); % the library by its bare name, found in the current folder
restore = onCleanup(@() cd(here));
switch kind
    case 'call'
        % The first native call starts a host; the next calls use the same one.
        a = nh('call', 'jgtestlib', 'int32 jg_int32(int32)', 41);
        pid1 = nh('pid');
        b = nh('call', 'jgtestlib', 'double jg_double(double)', 1.5);
        out = sprintf('%d %g %d', a, b, pid1 > 0 && nh('pid') == pid1);
    case 'crash'
        % A NULL dereference ends the host, not JGraph, and says how.
        nh('call', 'jgtestlib', 'int32 jg_int32(int32)', 1);
        try
            nh('call', 'jgtestlib', 'void jg_crash(void)');
            out = 'no error';
        catch e
            out = sprintf('%s ## %s', e.identifier, e.message);
        end
    case 'after_crash'
        % The host is gone after a crash, and the next call starts another.
        nh('call', 'jgtestlib', 'int32 jg_int32(int32)', 1);
        before = nh('pid');
        try
            nh('call', 'jgtestlib', 'void jg_crash(void)');
        catch
        end
        gone = nh('pid');
        again = nh('call', 'jgtestlib', 'int32 jg_int32(int32)', 9);
        out = sprintf('%d %d %d', gone, again, nh('pid') ~= before && nh('pid') > 0);
    case 'stop'
        % Stopping the host unloads everything; the next call starts a new one.
        nh('call', 'jgtestlib', 'int32 jg_int32(int32)', 1);
        before = nh('pid');
        nh('stop');
        stopped = nh('pid');
        value = nh('call', 'jgtestlib', 'int32 jg_version(void)');
        out = sprintf('%d %d %d', stopped, value, nh('pid') ~= before);
    case 'strings'
        out = sprintf('%s %d', nh('call', 'jgtestlib', 'cstring jg_greeting(void)'), ...
            nh('call', 'jgtestlib', 'int32 jg_strlen(cstring)', 'native'));
    case 'printf'
        % What native code prints goes nowhere, as in the MATLAB desktop; the call still answers.
        out = sprintf('%d', nh('call', 'jgtestlib', 'int32 jg_printf(cstring)', 'from C'));
    case 'find'
        [~, name, ext] = fileparts(nh('find', 'jgtestlib'));
        out = [name ext ' ' nh('find', 'kernel32')];
    case 'load_failed'
        try
            nh('call', 'jg_no_such_library', 'int32 f(int32)', 1);
            out = 'no error';
        catch e
            out = sprintf('%s ## %s', e.identifier, strrep(e.message, newline, ' ## '));
        end
    case 'not_a_dll'
        bad = fullfile(tempdir, 'ix_native_not_a_dll.dll');
        fid = fopen(bad, 'w'); fprintf(fid, 'not a library'); fclose(fid);
        try
            nh('call', bad, 'int32 f(int32)', 1);
            out = 'no error';
        catch e
            out = sprintf('%s ## %s', e.identifier, strrep(strrep(e.message, bad, '<file>'), newline, ' ## '));
        end
        delete(bad);
    case 'no_export'
        try
            nh('call', 'jgtestlib', 'double jg_not_exported(double)', 1);
            out = 'no error';
        catch e
            out = sprintf('%s ## %s', e.identifier, e.message);
        end
    case 'setenv'
        % setenv is seen by native code, changed and cleared.
        setenv('JG_IX_NATIVE', 'first');
        a = nh('call', 'jgtestlib', 'cstring jg_getenv(cstring)', 'JG_IX_NATIVE');
        setenv('JG_IX_NATIVE', 'second');
        b = nh('call', 'jgtestlib', 'cstring jg_getenv(cstring)', 'JG_IX_NATIVE');
        setenv('JG_IX_NATIVE', '');
        c = nh('call', 'jgtestlib', 'cstring jg_getenv(cstring)', 'JG_IX_NATIVE');
        out = sprintf('%s %s [%s]', a, b, c);
    case 'dotnet_env'
        % A variable .NET sets is seen by native code too, as in R2025b's one process.
        System.Environment.SetEnvironmentVariable('JG_IX_FROM_NET', 'from .NET');
        out = nh('call', 'jgtestlib', 'cstring jg_getenv(cstring)', 'JG_IX_FROM_NET');
        System.Environment.SetEnvironmentVariable('JG_IX_FROM_NET', '');
    case 'cd'
        % Native code's working folder follows cd, and comes back with it.
        lib = fullfile(p.root, 'jgtestlib.dll');
        cd(tempdir);
        moved = strcmpi(nh('call', lib, 'cstring jg_getcwd(void)'), pwd);
        cd(p.root);
        back = strcmpi(nh('call', lib, 'cstring jg_getcwd(void)'), p.root);
        out = sprintf('%d %d', moved, back);
end
end
