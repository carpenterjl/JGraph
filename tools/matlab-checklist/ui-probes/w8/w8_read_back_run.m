here = fileparts(mfilename('fullpath'));
root = getenv('W8_WORK');
try
    if isfile(fullfile(root, 'W8aBin.mlapp')) && isfile(fullfile(root, 'W8aPlain.m'))
        w8_read_back(fullfile(here, 'w8_read_back.txt'), fullfile(root, 'W8aBin.mlapp'), fullfile(root, 'W8aPlain.m'));
    end
    if isfile(fullfile(root, 'W8bApp.mlapp'))
        fid = fopen(fullfile(here, 'w8_read_back_run.txt'), 'w');
        [outcome, data] = appdesigner.internal.application.loadApp(fullfile(root, 'W8bApp.mlapp'));
        fprintf(fid, 'outcome [%s]\n', jsonencode(outcome));
        fprintf(fid, 'RunConfigurations %s size %s [%s]\n', class(data.RunConfigurations), mat2str(size(data.RunConfigurations)), jsonencode(data.RunConfigurations));
        reader = mlapp.internal.MLAPPMetadataReader(fullfile(root, 'W8bApp.mlapp'));
        [bytes, format] = reader.readMLAPPScreenshot();
        fprintf(fid, 'screenshot [%s] %d bytes, mode [%s]\n', format, numel(bytes), data.AppMetaData.ScreenshotMode);
        fclose(fid);
    end
    disp('done');
catch e
    disp(getReport(e));
end
