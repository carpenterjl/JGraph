outdir = pwd;
set(groot, 'DefaultFigureVisible', 'off');
efid = fopen(fullfile(outdir, 'run2_errors.txt'), 'w');
probes = {'probe_behave'};
for pi_ = 1:numel(probes)
    t_ = tic;
    try
        feval(probes{pi_}, outdir);
        fprintf(efid, '%s OK (%.1f s)\n', probes{pi_}, toc(t_));
    catch err_
        fprintf(efid, '%s FAILED: %s | %s\n', probes{pi_}, err_.identifier, err_.message);
        for k_ = 1:numel(err_.stack)
            fprintf(efid, '   at %s line %d\n', err_.stack(k_).name, err_.stack(k_).line);
        end
    end
    fprintf(efid, '  figures alive: %d visible: %d\n', numel(findall(groot, 'type', 'figure')), numel(findall(groot, 'type', 'figure', 'Visible', 'on')));
end
fclose(efid);
close all force;
