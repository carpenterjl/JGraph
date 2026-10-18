% DS1b window check (run with the user's leave, 2026-10-18, by ds1b_view.ps1 in R2025b's full
% desktop): opens each app ds1b_viewbuild wrote in App Designer's window, JGraph's tree
% (rt/view/jgraph) and then the control made by the app's own code (rt/view/code), through App
% Designer's own document API, which returns once the canvas has made every component. For each
% it reports whether App Designer marks the app modified on opening, then writes
% rt/view/sync/ready.txt and waits for ds1b_view.ps1 to capture the window (sync/ack.txt), and
% closes the app without saving. Nothing is saved; MATLAB exits at the end.
here = fileparts(mfilename('fullpath'));
view = fullfile(here, 'rt', 'view');
sync = fullfile(view, 'sync');
if ~isfolder(sync), mkdir(sync); end
log = fopen(fullfile(view, 'ds1b_view.log'), 'w');
files = dir(fullfile(view, 'jgraph', '*.mlapp'));
only = getenv('DS1B_ONLY');
appdesigner;
pause(10);
for k = 1:numel(files)
    app = extractBefore(files(k).name, '.mlapp');
    if ~isempty(only) && ~any(strcmp(app, split(only, ','))), continue; end
    for variant = {'jgraph', 'code'}
        fn = fullfile(view, variant{1}, files(k).name);
        if ~isfile(fn), continue; end
        t = tic;
        try
            doc = appdesigner.internal.document.AppDocument.open(fn);
            pause(4); % let the canvas draw
            line = sprintf('%s %s: open in %.1f s, modified %d', app, variant{1}, toc(t), doc.Modified);
        catch e
            doc = [];
            line = sprintf('%s %s: open failed %s: %s', app, variant{1}, e.identifier, strtok(e.message, newline));
        end
        fprintf(log, '%s\n', line);
        fprintf('%s\n', line);
        tmp = fullfile(sync, 'ready.tmp');
        f = fopen(tmp, 'w'); fprintf(f, '%s__%s', app, variant{1}); fclose(f);
        movefile(tmp, fullfile(sync, 'ready.txt'));
        ack = fullfile(sync, 'ack.txt');
        waited = tic;
        while ~isfile(ack) && toc(waited) < 60, pause(0.5); end
        if isfile(ack), delete(ack); end
        if ~isempty(doc) && isvalid(doc), doc.closeNoPrompt(); end
        pause(1);
    end
end
fprintf(log, 'done\n');
fclose(log);
f = fopen(fullfile(sync, 'done.txt'), 'w'); fclose(f);
exit
