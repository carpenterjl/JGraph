function w8_details(outFile, sourceApp, image)
%W8_DETAILS What R2025b's FileWriter writes for App Details and a thumbnail, and what its readers give back.
%   Headless. Copies sourceApp twice and writes the details each way, and writes down the package's parts.
fid = fopen(outFile, 'w');
closer = onCleanup(@() fclose(fid));
    function say(varargin)
        fprintf(fid, varargin{:});
        fprintf(fid, '\n');
    end

folder = tempname;
mkdir(folder);
target = fullfile(folder, 'W8Probe.mlapp');
copyfile(sourceApp, target, 'f');
fileattrib(target, '+w');

writer = appdesigner.internal.serialization.FileWriter(target);
details = struct('Name', 'A <name> & more', 'Author', 'An "author"', 'Version', '2.5', 'Summary', 'A summary', ...
    'Description', sprintf('Line one\nLine two'), 'ScreenshotMode', 'manual');
writer.writeAppMetadata(details);
writer.writeAppScreenshot(image);

say('== versions');
for v = {'1', '1.2', '1.2.3', '1.2.3.4', 'abc', '', ' 3.0 ', '3.0 x', '.5', '5.'}
    writer.writeAppMetadata(struct('Version', v{1}));
    reader = mlapp.internal.MLAPPMetadataReader(target);
    m = reader.readMLAPPMetadata();
    say('[%s] -> [%s]', v{1}, m.Version);
end
writer.writeAppMetadata(struct('Version', '2.5'));

reader = mlapp.internal.MLAPPMetadataReader(target);
m = reader.readMLAPPMetadata();
say('== read back');
for f = fieldnames(m)'
    value = m.(f{1});
    if ischar(value)
        say('%s = [%s]', f{1}, strrep(value, newline, '\n'));
    end
end
[bytes, format] = reader.readMLAPPScreenshot();
say('screenshot format [%s] bytes %d', format, numel(bytes));
uri = appdesigner.internal.application.AppThumbnailUtils.getThumbnailDataURI(target);
say('uri head [%s]', uri(1:min(40, numel(uri))));

say('== parts');
list = unzip(target, fullfile(folder, 'x'));
for k = 1:numel(list)
    d = dir(list{k});
    if ~d.isdir
        say('%s %d', strrep(extractAfter(list{k}, fullfile(folder, 'x')), '\', '/'), d.bytes);
    end
end
for part = {'[Content_Types].xml', fullfile('_rels', '.rels'), fullfile('metadata', 'appMetadata.xml'), fullfile('metadata', 'coreProperties.xml')}
    say('--- %s', part{1});
    say('%s', fileread(fullfile(folder, 'x', part{1})));
end

say('== thumbnail sizes');
for sz = {[100 100], [1000 1000], [200 100], [100 200], [300 1000], [1000 300], [165 220], [166 221], [120 400]}
    s = sz{1};
    file = fullfile(folder, 't.png');
    imwrite(zeros(s(1), s(2), 3, 'uint8'), file);
    u = appdesigner.internal.application.AppThumbnailUtils.createAppThumbnailDataURI(file);
    raw = matlab.net.base64decode(extractAfter(u, 'base64,'));
    jpg = fullfile(folder, 't.jpg');
    f2 = fopen(jpg, 'w');
    fwrite(f2, raw);
    fclose(f2);
    info = imfinfo(jpg);
    say('%dx%d (h x w) -> %dx%d', s(1), s(2), info.Height, info.Width);
end

say('== thumbnail section');
for mode = {'auto', 'manual', 'none'}
    say('%s', appdesigner.internal.application.AppThumbnailUtils.createThumbnailAppendixSectionStr(target, mode{1}, ''));
end

say('== help');
cases = { ...
    {'One line', ''}, ...
    {'A long summary that is long enough to wrap over more than one line of the file as the class builder wraps it', ''}, ...
    {'', 'Only a description'}, ...
    {'Sum', sprintf('First line\n\nThird line')}, ...
    {'Sum', sprintf('  Indented line\nA description long enough that it goes over more than one line when the class builder wraps its text there')}};
for k = 1:numel(cases)
    summary = cases{k}{1};
    description = cases{k}{2};
    lines = helpLines('W8Probe', summary, description);
    code = strjoin([{'classdef W8Probe < matlab.apps.AppBase'}, lines, {'    properties (Access = public)'}, {'    end'}, {'end'}], newline);
    p = appdesigner.internal.serialization.loader.HelpCommentsParser(stubLoader(), code);
    data = p.load();
    say('in  [%s] [%s]', summary, strrep(description, newline, '\n'));
    say('out [%s] [%s]', data.code.HelpComments.Summary, strrep(data.code.HelpComments.Description, newline, '\n'));
end
end

function lines = helpLines(className, summary, description)
% What ClassBuilder writes, at depth 1 with an indent of 4 and a limit of 75.
limit = 75 - 4;
lines = {};
first = ['%' upper(className)];
if ~isempty(summary)
    first = [first ' ' summary];
end
lines = [lines, cellfun(@(l) ['    ' l], wrap(first, '%', limit), 'UniformOutput', false)];
if ~isempty(description)
    parts = strsplit(description, newline);
    for k = 1:numel(parts)
        lines = [lines, cellfun(@(l) ['    ' l], wrap(['%   ' parts{k}], '%   ', limit), 'UniformOutput', false)]; %#ok<AGROW>
    end
end
end

function out = wrap(text, prefix, limit)
if startsWith(text, '%   ')
    body = text(5:end);
else
    body = text(2:end);
    prefix = '%';
end
words = strsplit(body, ' ', 'CollapseDelimiters', false);
out = {[prefix words{1}]};
for k = 2:numel(words)
    candidate = [out{end} ' ' words{k}];
    if numel(candidate) <= limit
        out{end} = candidate;
    else
        out{end + 1} = [prefix words{k}]; %#ok<AGROW>
    end
end
end

function loader = stubLoader()
loader = w8_stub_loader();
end
