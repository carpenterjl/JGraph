function p6_which()
%P6_WHICH Which class folder a bare method name reaches in the dependency analysis when several
%folders hold a method file of that name (packages and class folders plan, stage P6): three class
%folders @Alpha, @Sub < Thing and @Thing each with extent.m, a fresh session, the analysis first,
%then which and which -all. One key<TAB>value line per answer.
beep('off');
here = fileparts(mfilename('fullpath'));
root = fullfile(here, 'rt', 'which');
if exist(root, 'dir'), rmdir(root, 's'); end
cur = fullfile(root, 'cur');
lib = fullfile(root, 'lib');
mkdir(cur); mkdir(lib);
w(lib, '@Thing/Thing.m', {'classdef Thing', '    properties', '        W = 0', '    end', '    methods', '        function o = Thing(w)', '            if nargin, o.W = w; end', '        end', '        r = extent(o)', '    end', 'end'});
w(lib, '@Thing/extent.m', {'function r = extent(o)', 'r = o.W * o.W;', 'end'});
w(lib, '@Sub/Sub.m', {'classdef Sub < Thing', '    methods', '        function o = Sub(w)', '            o@Thing(w);', '        end', '    end', 'end'});
w(lib, '@Sub/extent.m', {'function r = extent(o)', 'r = -1;', 'end'});
w(lib, '@Alpha/Alpha.m', {'classdef Alpha', '    properties', '        A = 0', '    end', 'end'});
w(lib, '@Alpha/extent.m', {'function r = extent(o)', 'r = 5;', 'end'});
w(lib, '@Zed/Zed.m', {'classdef Zed', '    properties', '        Z = 0', '    end', 'end'});
w(lib, '@Zed/extent.m', {'function r = extent(o)', 'r = 6;', 'end'});
w(cur, 'usemethodhandle.m', {'function r = usemethodhandle()', 'h = @extent;', 'r = h(Thing(2));', 'end'});
w(cur, 'usebarecall.m', {'function r = usebarecall(t)', 'r = extent(t);', 'end'});
old = cd(cur);
cleanup = onCleanup(@() cd(old));
addpath(lib);
rfp(root, 'rfp.handle.fresh', 'usemethodhandle.m');
rfp(root, 'rfp.barecall.fresh', 'usebarecall.m');
fprintf('which.extent\t%s\n', rel(which('extent'), root));
fprintf('which.all\t%s\n', rel(strjoin(which('extent', '-all'), ' | '), root));
rfp(root, 'rfp.handle.again', 'usemethodhandle.m');
end

function rfp(root, key, varargin)
try
    f = matlab.codetools.requiredFilesAndProducts(varargin{:});
    fprintf('%s\t%s\n', key, strjoin(cellfun(@(x) rel(x, root), sort(f), 'UniformOutput', false), ' ; '));
catch ME
    fprintf('%s\tERR %s | %s\n', key, ME.identifier, ME.message);
end
end

function w(root, relative, lines)
file = fullfile(root, relative);
folder = fileparts(file);
if ~exist(folder, 'dir'), mkdir(folder); end
fid = fopen(file, 'w');
fprintf(fid, '%s\n', lines{:});
fclose(fid);
end

function s = rel(s, root)
s = strrep(s, root, '<T>');
end
