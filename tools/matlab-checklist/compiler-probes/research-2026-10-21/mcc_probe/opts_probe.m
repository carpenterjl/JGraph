function opts_probe()
out = fullfile(pwd, 'env');
opts = {'StandaloneApplicationOptions','hello.m'; ...
    'PythonPackageOptions','addtwo.m'; 'DotNETAssemblyOptions','addtwo.m'; 'CppSharedLibraryOptions','typedadd.m'; ...
    'JavaPackageOptions','addtwo.m'; 'ProductionServerArchiveOptions','addtwo.m'; 'CSharedLibraryOptions','addtwo.m'; ...
    'ExcelAddInOptions','addtwo.m'; 'COMComponentOptions','addtwo.m'; 'WebAppArchiveOptions','hello.m'};
for i = 1:size(opts,1)
    s = '';
    try
        o = feval(['compiler.build.', opts{i,1}], opts{i,2});
        p = properties(o);
        for k = 1:numel(p)
            try
                v = o.(p{k});
                if ischar(v) || isstring(v)
                    vs = ['"', char(strjoin(string(v), '", "')), '"'];
                elseif isnumeric(v) || islogical(v)
                    vs = mat2str(v);
                elseif iscell(v)
                    vs = ['{', strjoin(cellfun(@(x) char(string(x)), v, 'UniformOutput', false), ', '), '}'];
                else
                    vs = ['<', class(v), '> ', strtrim(evalc('disp(v)'))];
                end
            catch e2
                vs = ['<error: ', e2.message, '>'];
            end
            s = [s, sprintf('%-28s %s\n', p{k}, vs)]; %#ok<AGROW>
        end
    catch e
        s = ['construction failed: ', e.message];
    end
    fid = fopen(fullfile(out, ['opts_', opts{i,1}, '.txt']), 'w'); fwrite(fid, s); fclose(fid);
end
% enumerations of interface options if any
try
    s = evalc('o = compiler.build.DotNETAssemblyOptions(''addtwo.m''); disp(o)');
    fid = fopen(fullfile(out, 'disp_DotNETAssemblyOptions.txt'), 'w'); fwrite(fid, s); fclose(fid);
catch
end
end
