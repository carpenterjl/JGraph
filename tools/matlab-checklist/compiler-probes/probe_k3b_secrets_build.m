function probe_k3b_secrets_build(work)
%PROBE_K3B_SECRETS_BUILD Builds k3bs_main, which calls every secret function in a compiled program
%(compiler and SDK plan, stage K3b): getSecret with and without an output, isSecret,
%getSecretMetadata, setSecret, removeSecret, setSecretMetadata and their argument refusals.
%record-k3b.ps1 runs it with and without K3B_SECRET in its environment. Run under matlab -batch
%-noFigureWindows; nothing built is run here.
beep('off');
here = fileparts(mfilename('fullpath'));
addpath(fullfile(here, 'src'));
if exist(work, 'dir'), rmdir(work, 's'); end
mkdir(work);
src = fullfile(work, 'src');
mkdir(src);
copyfile(fullfile(here, 'src', 'k0_quiet.m'), src);
copyfile(fullfile(here, 'src', 'k0_flat.m'), src);
put(src, 'k3bs_main.m', {
    'function k3bs_main(varargin)'
    'k0_quiet;'
    'calls = {'
    '    ''getSecret.out'',            @() class(getSecret(''K3B_SECRET''))'
    '    ''getSecret.value'',          @() char(getSecret(''K3B_SECRET''))'
    '    ''getSecret.noout'',          @() evalc(''getSecret(''''K3B_SECRET'''')'')'
    '    ''getSecret.empty'',          @() getSecret('''')'
    '    ''getSecret.number'',         @() getSecret(5)'
    '    ''getSecret.lower'',          @() char(getSecret(''k3b_secret''))'
    '    ''isSecret'',                 @() isSecret(''K3B_SECRET'')'
    '    ''isSecret.missing'',         @() isSecret(''K3B_NOSUCH'')'
    '    ''getSecretMetadata'',        @() class(getSecretMetadata(''K3B_SECRET''))'
    '    ''getSecretMetadata.missing'', @() class(getSecretMetadata(''K3B_NOSUCH''))'
    '    ''setSecret'',                @() setSecret(''K3B_NEW'')'
    '    ''removeSecret'',             @() removeSecret(''K3B_SECRET'')'
    '    ''setSecretMetadata'',        @() setSecretMetadata(''K3B_SECRET'', dictionary("a", {1}))'
    '    ''secretID'',                 @() char(getSecret(secretID(''K3B_SECRET'')))'
    '    };'
    'for i = 1:size(calls, 1)'
    '    try'
    '        f = calls{i, 2};'
    '        if any(strcmp(calls{i, 1}, {''setSecret'', ''removeSecret'', ''setSecretMetadata''}))'
    '            f();'
    '            fprintf(''%s\tok\n'', calls{i, 1});'
    '        else'
    '            v = f();'
    '            if islogical(v), v = sprintf(''%d'', v); end'
    '            fprintf(''%s\t%s\n'', calls{i, 1}, k0_flat(v));'
    '        end'
    '    catch e'
    '        fprintf(''%s\tERR %s | %s\n'', calls{i, 1}, e.identifier, k0_flat(e.message));'
    '    end'
    'end'
    'end'
    });
d = fullfile(work, 'b');
mkdir(d);
cd(src);
try
    evalc('mcc(''-m'', ''k3bs_main.m'', ''-C'', ''-d'', d)');
    fprintf('build\tok\n');
catch e
    fprintf('build\tERR %s | %s\n', e.identifier, k0_flat(e.message));
end
cd(here);
end

function put(folder, name, lines)
fid = fopen(fullfile(folder, name), 'w');
fprintf(fid, '%s\n', lines{:});
fclose(fid);
end
