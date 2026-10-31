here = fileparts(mfilename('fullpath'));
root = getenv('W8_WORK');
try
    fid = fopen(fullfile(here, 'w8_read_back_convert.txt'), 'w');
    file = fullfile(root, 'W8cBin.mlapp');
    [outcome, data] = appdesigner.internal.application.loadApp(file);
    fprintf(fid, 'outcome [%s]\n', jsonencode(outcome));
    fprintf(fid, 'class [%s]\n', data.CodeData.ClassName);
    fprintf(fid, 'AppType [%s]\n', data.AppMetaData.AppType);
    children = data.ComponentData.Children;
    if ~iscell(children), children = num2cell(children); end
    for k = 1:numel(children)
        fprintf(fid, 'child %s %s\n', children{k}.Type, children{k}.PropertyValues.CodeName);
        inner = children{k}.Children;
        if ~iscell(inner), inner = num2cell(inner); end
        for j = 1:numel(inner)
            fprintf(fid, '  %s %s\n', inner{j}.Type, inner{j}.PropertyValues.CodeName);
            deep = inner{j}.Children;
            if ~iscell(deep), deep = num2cell(deep); end
            for i = 1:numel(deep)
                fprintf(fid, '    %s %s %s\n', deep{i}.Type, deep{i}.PropertyValues.CodeName, mat2str(deep{i}.PropertyValues.Position));
            end
        end
    end
    fclose(fid);
    disp('done');
catch e
    disp(getReport(e));
end
