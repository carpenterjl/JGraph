% probe_shrlib_search: where loadlibrary finds a library (interop plan, stage 7). Every case loads
% a copy of jgtestlib.dll under its own file name through the prototype file, so no compiler runs,
% and asks the process which file it mapped.
a = ip_assets();
addpath(a.here);
addpath(a.root); % the prototype file and its thunk library
top = tempname; mkdir(top);
A = fullfile(top, 'A'); B = fullfile(top, 'B'); mkdir(A); mkdir(B); mkdir(fullfile(A, 'sub'));
here = pwd;
cd(A);
addpath(B);

copyfile(a.lib, fullfile(A, 's1.dll')); copyfile(a.lib, fullfile(B, 's1.dll'));
copyfile(a.lib, fullfile(B, 's2.dll'));
copyfile(a.lib, fullfile(A, 's3.mexw64'));
copyfile(a.lib, fullfile(A, 's4.dll')); copyfile(a.lib, fullfile(A, 's4.mexw64'));
copyfile(a.lib, fullfile(A, 's5.dll'));
copyfile(a.lib, fullfile(B, 's9.dll'));
copyfile(a.lib, fullfile(A, 'sub', 's11.dll'));
copyfile(a.lib, fullfile(A, 's12.lib'));
copyfile(a.lib, fullfile(B, 's13.mexw64'));
fid = fopen(fullfile(A, 'bad.dll'), 'w'); fprintf(fid, 'not a library'); fclose(fid);
x32 = 'C:\Windows\SysWOW64\version.dll';
if isfile(x32), copyfile(x32, fullfile(A, 'x32.dll')); end

cases = {
    's1_cwd_and_path',   's1'
    's2_path_only',      's2'
    's3_mexw64_only',    's3'
    's4_dll_and_mexw64', 's4'
    's5_with_extension', 's5.dll'
    's9_absolute_no_ext', fullfile(B, 's9')
    's11_relative_sub',  fullfile('sub', 's11')
    's12_other_ext',     's12.lib'
    's13_mexw64_path',   's13'
    'missing',           'nosuchlib_zz'
    'missing_abs',       fullfile(A, 'nosuchlib_zz.dll')
    'not_a_dll',         'bad'
    'x32',               'x32'
    };
for k = 1:size(cases, 1)
    key = cases{k, 1};
    name = cases{k, 2};
    alias = sprintf('probe_s%d', k);
    try
        loadlibrary(name, @jgtestlib_proto, 'alias', alias);
        modules = System.Diagnostics.Process.GetCurrentProcess().Modules;
        where = '';
        for m = 0:modules.Count - 1
            f = char(modules.Item(m).FileName);
            if startsWith(f, top, 'IgnoreCase', true)
                where = [where ' ' strrep(f, top, '<top>')]; %#ok<AGROW>
            end
        end
        fprintf('%s\tLOADED%s\n', key, where);
        fprintf('%s.call\t%d\n', key, calllib(alias, 'jg_int32', 4));
        unloadlibrary(alias);
    catch e
        fprintf('%s\tERR %s %s\n', key, e.identifier, strrep(strrep(e.message, top, '<top>'), newline, ' | '));
    end
end

cd(here);
rmpath(B);
