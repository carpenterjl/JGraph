function w8_read_back(outFile, binary, plain)
%W8_READ_BACK What R2025b's own readers make of the files JGraph wrote in the W8a window check. Headless.
fid = fopen(outFile, 'w');
closer = onCleanup(@() fclose(fid));
    function say(varargin)
        fprintf(fid, varargin{:});
        fprintf(fid, '\n');
    end

say('== mlapp metadata');
reader = mlapp.internal.MLAPPMetadataReader(binary);
m = reader.readMLAPPMetadata();
for f = fieldnames(m)'
    value = m.(f{1});
    if ischar(value)
        say('%s = [%s]', f{1}, strrep(value, newline, '\n'));
    end
end
[bytes, format] = reader.readMLAPPScreenshot();
say('screenshot format [%s] bytes %d', format, numel(bytes));
uri = appdesigner.internal.application.AppThumbnailUtils.getThumbnailDataURI(binary);
say('uri head [%s]', uri(1:min(30, numel(uri))));

say('== mlapp through the loader');
[outcome, data] = appdesigner.internal.application.loadApp(binary);
say('outcome [%s]', jsonencode(outcome));
say('ClassName [%s]', data.CodeData.ClassName);
say('HelpSummary [%s]', data.CodeData.HelpComments.Summary);
say('HelpDescription [%s]', strrep(data.CodeData.HelpComments.Description, newline, '\n'));
say('InputParameters [%s]', jsonencode(data.CodeData.InputParameters));
say('SingletonMode [%s]', data.CodeData.SingletonMode);
say('ScreenshotMode via loader [%s]', data.AppMetaData.ScreenshotMode);
say('Author via loader [%s]', data.AppMetaData.Author);

say('== plain text');
reader2 = appdesigner.internal.serialization.MAppMetadataReader(plain);
p = reader2.readMetadata();
for f = fieldnames(p)'
    value = p.(f{1});
    if ischar(value)
        say('%s = [%s]', f{1}, strrep(value, newline, '\n'));
    end
end
code = fileread(plain);
section = regexp(code, '<Thumbnail autoCapture=''(\w+)''>([^<]{0,30})', 'tokens', 'once');
say('thumbnail [%s] [%s]', section{1}, section{2});
end
