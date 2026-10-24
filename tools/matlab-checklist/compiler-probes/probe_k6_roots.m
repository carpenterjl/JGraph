function probe_k6_roots(work)
%PROBE_K6_ROOTS Whether mcc searches its main file's own folder when the main is built from another
%folder and is not on the path (compiler and SDK plan, stage K6; stage K2 measured that
%requiredFilesAndProducts does not). The main calls a helper beside it, a helper in private/, and
%reads a data file beside it by a literal. Each build is -C, so the archive's entries can be listed.
%Run under matlab -batch -noFigureWindows; nothing built is run.
beep('off');
if exist(work, 'dir'), rmdir(work, 's'); end
app = fullfile(work, 'app');
mkdir(fullfile(app, 'private'));
writelines(["function k6r_main", "k6r_helper();", "k6r_secret();", "disp(fileread('k6r_data.txt'));", "end"], fullfile(app, 'k6r_main.m'));
writelines(["function k6r_helper", "disp('helper');", "end"], fullfile(app, 'k6r_helper.m'));
writelines(["function k6r_secret", "disp('private');", "end"], fullfile(app, 'private', 'k6r_secret.m'));
writelines("data", fullfile(app, 'k6r_data.txt'));
elsewhere = fullfile(work, 'elsewhere');
mkdir(elsewhere);
old = cd(elsewhere);
cleanup = onCleanup(@() cd(old));
cases = {
    'mcc.elsewhere', @(d) mcc('-m', fullfile(app, 'k6r_main.m'), '-C', '-d', d)
    'mcc.elsewhere.I', @(d) mcc('-m', fullfile(app, 'k6r_main.m'), '-C', '-I', app, '-d', d)
    'build.elsewhere', @(d) compiler.build.standaloneApplication(fullfile(app, 'k6r_main.m'), 'EmbedArchive', 'off', 'OutputDir', d)
    };
for k = 1:size(cases, 1)
    [key, f] = cases{k, :};
    d = fullfile(work, matlab.lang.makeValidName(key));
    mkdir(d);
    lastwarn('', '');
    try
        evalc('f(d)');
        ctf = dir(fullfile(d, '*.ctf'));
        names = unzip(fullfile(d, ctf(1).name), fullfile(d, 'x'));
        names = strrep(names, [fullfile(d, 'x') filesep], '');
        names = names(startsWith(names, 'fsroot'));
        fprintf('%s\tok %s\n', key, strjoin(sort(strrep(strrep(names, app(4:end), '<APP-NODRIVE>'), '\', '/')), ' | '));
    catch ME
        fprintf('%s\tERR %s | %s\n', key, ME.identifier, k0_flat(ME.message));
    end
    [msg, id] = lastwarn;
    if ~isempty(msg), fprintf('%s.warn\t%s | %s\n', key, id, k0_flat(msg)); end
end
fprintf('rfp.elsewhere\t%s\n', strjoin(strrep(matlab.codetools.requiredFilesAndProducts(fullfile(app, 'k6r_main.m')), app, '<APP>'), ' | '));

% From the main's parent folder: is a file placed under the component's folder by where mcc runs,
% or by where the main is?
cd(work);
d = fullfile(work, 'mcc_parent');
mkdir(d);
try
    evalc('mcc(''-m'', fullfile(''app'', ''k6r_main.m''), ''-C'', ''-d'', d)');
    names = unzip(fullfile(d, 'k6r_main.ctf'), fullfile(d, 'x'));
    names = strrep(names, [fullfile(d, 'x') filesep], '');
    names = names(startsWith(names, 'fsroot') & ~contains(names, {'toolbox', '.META', '.matlab'}));
    fprintf('mcc.parent\tok %s\n', strjoin(sort(strrep(strrep(names, work(4:end), '<W-NODRIVE>'), '\', '/')), ' | '));
catch ME
    fprintf('mcc.parent\tERR %s | %s\n', ME.identifier, k0_flat(ME.message));
end
end
