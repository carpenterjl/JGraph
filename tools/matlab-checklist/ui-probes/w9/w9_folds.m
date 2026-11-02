% W9c's code folding oracle: the blocks R2025b's mtree finds in the Smart Indent oracle's programs, each with its kind and its first and
% last lines, which the rich text component's MatlabMTreeFoldCandidateContributor folds by kind. Run headless, after w9_indent:
%   matlab -noFigureWindows -batch "cd('<this folder>'); w9_folds" > ../../../../tests/JGraph.Tests/Scripting/Editing/FoldOracle.json
cases = jsondecode(fileread('../../../../tests/JGraph.Tests/Scripting/Editing/SmartIndentOracle.json'));
out = {};
for i = 1:numel(cases)
    text = cases(i).result;
    t = mtree(text, '-comments');
    if ~isempty(t.mtfind('Kind', 'ERR')) || t.count == 0
        out{end + 1} = struct('text', text, 'error', true, 'blocks', []); %#ok<SAGROW>
        continue;
    end
    blocks = {};
    kinds = unique(t.kinds);
    for k = 1:numel(kinds)
        kind = kinds{k};
        nodes = t.mtfind('Kind', kind);
        idx = nodes.indices;
        for j = 1:numel(idx)
            n = t.select(idx(j));
            startLine = n.lineno;
            [endLine, ~] = t.pos2lc(n.righttreepos);
            blocks{end + 1} = struct('kind', kind, 'start', startLine, 'end', endLine); %#ok<SAGROW>
        end
    end
    out{end + 1} = struct('text', text, 'error', false, 'blocks', [blocks{:}]); %#ok<SAGROW>
end
fprintf('%s\n', jsonencode(out));
