% m145_run.m -- run(path): a script named by path runs in its own folder. Measured in R2025b:
% pwd inside run('other/s.m') is other, a file beside the script answers a name there (max.m for
% a char argument, which no method of the built-in claims; sib.m), the caller is back where it was
% afterwards, an error included, a cd the script makes is the caller's to keep, a run inside the
% script resolves beside it, and the stem names the file with or without its .m. ADR 0149 had
% left the folder off the implicit path; this pins the fix. The fixture builds its folders under
% tempdir and removes them.

start = pwd;
root = fullfile(tempdir, 'm145_run');
if exist(root, 'dir') == 7
    rmdir(root, 's');
end
other = fullfile(root, 'other');
elsewhere = fullfile(root, 'elsewhere');
mkdir(other);
mkdir(elsewhere);
put(fullfile(other, 'max.m'), {'function y = max(varargin)', 'y = ''file max'';', 'end'});
put(fullfile(other, 'sib.m'), {'function y = sib()', 'y = ''sibling'';', 'end'});
put(fullfile(other, 's.m'), {'S_PWD = pwd;', 'S_SIB = sib();', 'S_MAX = max(''tag'');'});
put(fullfile(other, 'mover.m'), {'M_PWD = pwd;', 'cd(''../elsewhere'');'});
put(fullfile(other, 'failer.m'), {'F_PWD = pwd;', 'error(''m145:boom'', ''boom'');'});
put(fullfile(other, 'u.m'), {'U_PWD = pwd;'});
put(fullfile(other, 't.m'), {'run(''u.m'');', 'T_SIB = sib();', 'T_PWD = pwd;'});
put(fullfile(root, 'local.m'), {'L_PWD = pwd;'});
cd(root);
rel = @(p) strrep(p, root, '<root>');

% A relative path: the script's folder for the duration, the caller's after.
run('other/s.m');
chk('run_pwd_inside', rel(S_PWD));
chk('run_pwd_after', rel(pwd));
chk('run_sibling', S_SIB);
chk('run_file_beside_script', S_MAX);
chk('run_sibling_gone_after', refused(@() sib()));

% A script that moves: its move stands.
run(fullfile(other, 'mover.m'));
chk('run_mover_inside', rel(M_PWD));
chk('run_mover_after', rel(pwd));
cd(root);

% An error inside: the caller is still put back.
try
    run('other/failer.m');
catch e
    chk('run_error_id', e.identifier);
end
chk('run_error_pwd_inside', rel(F_PWD));
chk('run_error_pwd_after', rel(pwd));

% A run inside the script names the file beside it.
run('other/t.m');
chk('run_nested_inside', rel(U_PWD));
chk('run_nested_sibling', T_SIB);
chk('run_nested_pwd', rel(T_PWD));
chk('run_nested_after', rel(pwd));

% The stem, with or without its extension; a missing file is refused.
run(fullfile(root, 'local'));
chk('run_without_extension', rel(L_PWD));
chk('run_missing_refused', refused(@() run('nowhere.m')));

% From a moved folder, a relative path is relative to it, and it is the folder put back.
cd(elsewhere);
run('../other/s.m');
chk('run_relative_to_moved', rel(S_PWD));
chk('run_relative_after', rel(pwd));

cd(start);
rmdir(root, 's');

function put(path, lines)
fid = fopen(path, 'w');
for k = 1:length(lines)
    fprintf(fid, '%s\n', lines{k});
end
fclose(fid);
end

function chk(name, v)
fprintf('CHK|%s|%s|exact\n', name, show(v));
end

function s = show(v)
if ischar(v)
    s = v;
elseif isstring(v)
    s = char(v);
elseif isscalar(v) && (isnumeric(v) || islogical(v))
    s = sprintf('%.17g', double(v));
else
    s = ['<' class(v) '>'];
end
end

function no = refused(call)
no = false;
try
    call();
catch
    no = true;
end
end
