% w0_analyzer - the Code Analyzer messages R2025b raises most over a corpus (stage W0 of the App
% Designer window plan, decision WO5: W10's first checks are the top ids), headless:
%   powershell -ExecutionPolicy Bypass -File .\run-probe.ps1 -Name w0_analyzer -TimeoutSec 1200
% The corpus: the .mlapp files R2025b ships, JGraph's parity fixtures (tests/JGraph.Tests/MatlabParity/
% fixtures/**/*.m) and examples/**/*.m. codeIssues gives each issue's check id, severity, fixability and
% text; the ranking (by files, then by count) goes to w0_analyzer.csv with one sample text per id, and
% the texts of every id seen go to w0_analyzer_texts.csv, since a check's words vary with the code.
here = fileparts(mfilename('fullpath'));
repo = fileparts(fileparts(fileparts(fileparts(here))));
groups = struct('name', {'shipped apps', 'parity fixtures', 'examples'}, 'files', {{}, {}, {}});
d = dir(fullfile(matlabroot, 'toolbox', '**', '*.mlapp'));
groups(1).files = fullfile({d.folder}, {d.name});
d = dir(fullfile(repo, 'tests', 'JGraph.Tests', 'MatlabParity', 'fixtures', '**', '*.m'));
groups(2).files = fullfile({d.folder}, {d.name});
d = dir(fullfile(repo, 'examples', '**', '*.m'));
groups(3).files = fullfile({d.folder}, {d.name});

all = table();
for g = 1:numel(groups)
    n = numel(groups(g).files);
    t0 = tic;
    failed = 0;
    for k = 1:n
        f = groups(g).files{k};
        try
            ci = codeIssues(f);
            iss = ci.Issues;
        catch e
            failed = failed + 1;
            if failed <= 3, fprintf('  codeIssues failed on %s: %s\n', short(f, repo), e.message); end
            continue
        end
        if isempty(iss), continue, end
        iss.Group = repmat(string(groups(g).name), height(iss), 1);
        iss.File = repmat(string(short(f, repo)), height(iss), 1);
        all = [all; iss(:, {'Group', 'File', 'CheckID', 'Severity', 'Fixability', 'Description'})]; %#ok<AGROW>
    end
    fprintf('== %s: %d files, %d failed, %.0f s\n', groups(g).name, n, failed, toc(t0));
end
fprintf('== %d issues in all\n', height(all));
all.CheckID = string(all.CheckID);
all.Severity = string(all.Severity);
all.Fixability = string(all.Fixability);
all.Description = string(all.Description);
[ids, ~, j] = unique(all.CheckID);
rank = table(ids, accumarray(j, 1), 'VariableNames', {'CheckID', 'Count'});
rank.Files = splitapply(@(f) numel(unique(f)), all.File, j);
for g = 1:numel(groups)
    rank.(matlab.lang.makeValidName(groups(g).name)) = accumarray(j, double(all.Group == groups(g).name));
end
rank.Severity = splitapply(@(s) strjoin(unique(s)', '/'), all.Severity, j);
rank.Fixability = splitapply(@(s) strjoin(unique(s)', '/'), all.Fixability, j);
rank.Sample = splitapply(@(s) s(1), all.Description, j);
rank = sortrows(rank, {'Files', 'Count'}, 'descend');
writetable(rank, fullfile(here, 'w0_analyzer.csv'));
texts = unique(all(:, {'CheckID', 'Severity', 'Description'}), 'rows');
writetable(texts, fullfile(here, 'w0_analyzer_texts.csv'));
fprintf('== %d distinct ids; the top 60 by files\n', height(rank));
for k = 1:min(60, height(rank))
    fprintf('%3d %-22s files %4d count %5d  %-7s %-8s %s\n', k, rank.CheckID(k), rank.Files(k), rank.Count(k), ...
        rank.Severity(k), rank.Fixability(k), extractBefore(rank.Sample(k) + blanks(90), 91));
end
fprintf('done\n');

function s = short(f, repo)
s = strrep(strrep(f, matlabroot, '<matlabroot>'), repo, '<repo>');
end
