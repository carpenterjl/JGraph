function probe_k14_routes(work)
%PROBE_K14_ROUTES Builds Production Server archives with routes files whose "component" names the
%archive (compiler and SDK plan, stage K14), which probe_k14_build's did not, and the shapes a
%routes file may take; prints what mcc said of each. Run under matlab -batch -noFigureWindows.
beep('off');
here = fileparts(mfilename('fullpath'));
addpath(fullfile(here, 'src'));
if exist(work, 'dir'), rmdir(work, 's'); end
mkdir(work);
src = fullfile(work, 'src');
mkdir(src);
copyfile(fullfile(here, 'src', 'k14', 'k14add.m'), src);
copyfile(fullfile(here, 'src', 'k14', 'k14multi.m'), src);
old = cd(src);
cleanup = onCleanup(@() cd(old));
put(src, 'r_ok.json', {'{"version":"1.0.0","pathmap":[{"match":"^/k14/add$","webhandler":{"component":"k14r","function":"k14add"}}]}'});
put(src, 'r_nocomponent.json', {'{"version":"1.0.0","pathmap":[{"match":"^/k14/add$","webhandler":{"function":"k14add"}}]}'});
put(src, 'r_two.json', {'{"version":"1.0.0","pathmap":[{"match":"^/k14/add$","webhandler":{"function":"k14add"}},{"match":"^/k14/multi/.*","webhandler":{"component":"k14r","function":"k14multi"}}]}'});
put(src, 'r_noversion.json', {'{"pathmap":[{"match":"^/a$","webhandler":{"function":"k14add"}}]}'});
put(src, 'r_badversion.json', {'{"version":"2.0.0","pathmap":[{"match":"^/a$","webhandler":{"function":"k14add"}}]}'});
put(src, 'r_version_number.json', {'{"version":1,"pathmap":[{"match":"^/a$","webhandler":{"function":"k14add"}}]}'});
put(src, 'r_nomatch.json', {'{"version":"1.0.0","pathmap":[{"webhandler":{"function":"k14add"}}]}'});
put(src, 'r_badregex.json', {'{"version":"1.0.0","pathmap":[{"match":"^/a[$","webhandler":{"function":"k14add"}}]}'});
put(src, 'r_extra.json', {'{"version":"1.0.0","pathmap":[{"match":"^/a$","webhandler":{"function":"k14add"},"extra":1}],"more":2}'});
put(src, 'r_extra_handler.json', {'{"version":"1.0.0","pathmap":[{"match":"^/a$","webhandler":{"function":"k14add","other":"x"}}]}'});
put(src, 'r_nosuchfn.json', {'{"version":"1.0.0","pathmap":[{"match":"^/a$","webhandler":{"function":"k14nosuch"}}]}'});
put(src, 'r_match_number.json', {'{"version":"1.0.0","pathmap":[{"match":5,"webhandler":{"function":"k14add"}}]}'});
put(src, 'r_function_number.json', {'{"version":"1.0.0","pathmap":[{"match":"^/a$","webhandler":{"function":5}}]}'});
put(src, 'r_handler_text.json', {'{"version":"1.0.0","pathmap":[{"match":"^/a$","webhandler":"k14add"}]}'});
put(src, 'r_othercomponent.json', {'{"version":"1.0.0","pathmap":[{"match":"^/a$","webhandler":{"component":"other","function":"k14add"}}]}'});
put(src, 'r_array.json', {'[1,2]'});
put(src, 'r_empty.json', {'{}'});
put(src, 'r_dup.json', {'{"version":"1.0.0","pathmap":[{"match":"^/a$","webhandler":{"function":"k14add"}},{"match":"^/a$","webhandler":{"function":"k14multi"}}]}'});
files = {'r_ok', 'r_nocomponent', 'r_two', 'r_noversion', 'r_badversion', 'r_version_number', 'r_nomatch', 'r_badregex', 'r_extra', ...
    'r_extra_handler', 'r_nosuchfn', 'r_match_number', 'r_function_number', 'r_handler_text', 'r_othercomponent', 'r_array', 'r_empty', 'r_dup'};
for k = 1:numel(files)
    key = files{k};
    d = fullfile(work, key);
    mkdir(d);
    lastwarn('', '');
    try
        txt = evalc('mcc(''-W'', [''CTF:k14r,ROUTES:'' key ''.json''], ''-U'', ''k14add.m'', ''k14multi.m'', ''-d'', d)');
        fprintf('%s\tok\n', key);
        lines = splitlines(strrep(txt, sprintf('\r\n'), newline));
        for j = 1:numel(lines)
            if ~isempty(strtrim(lines{j})), fprintf('  |%s\n', lines{j}); end
        end
    catch ME
        fprintf('%s\tERR %s | %s\n', key, ME.identifier, k0_flat(ME.message));
    end
    k0_warned(key);
end
% compiler.build with a routes file that names the archive, and with one naming another.
try
    r = compiler.build.productionServerArchive(fullfile(src, 'k14add.m'), 'ArchiveName', 'k14r', 'RoutesFile', fullfile(src, 'r_ok.json'), 'OutputDir', fullfile(work, 'api_ok'));
    fprintf('api_ok\t%s\n', strjoin(strrep(r.Files(:)', [work filesep], '<W>\'), ' | '));
catch ME
    fprintf('api_ok\tERR %s | %s\n', ME.identifier, k0_flat(ME.message));
end
k0_warned('api_ok');
try
    r = compiler.build.productionServerArchive(fullfile(src, 'k14add.m'), 'ArchiveName', 'k14other', 'RoutesFile', fullfile(src, 'r_ok.json'), 'OutputDir', fullfile(work, 'api_other'));
    fprintf('api_other\t%s\n', strjoin(strrep(r.Files(:)', [work filesep], '<W>\'), ' | '));
catch ME
    fprintf('api_other\tERR %s | %s\n', ME.identifier, k0_flat(ME.message));
end
k0_warned('api_other');
end

function put(folder, name, lines)
fid = fopen(fullfile(folder, name), 'w');
fprintf(fid, '%s\n', lines{:});
fclose(fid);
end
