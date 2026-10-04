function u1_closereq
% U1 probe: what an empty CloseRequestFcn holds and does.
forms = {'', [], {}, "", 'closereq', 'closereq;'};
names = {'empty char', 'empty double', 'empty cell', 'empty string', 'closereq', 'closereq;'};
for k = 1:numel(forms)
    f = figure('Visible', 'off');
    f.CloseRequestFcn = forms{k};
    v = f.CloseRequestFcn;
    r = close(f);
    fprintf('%s: stored class=%s size=%s value=[%s] close=%d alive=%d\n', names{k}, class(v), mat2str(size(v)), char(string(v)), r, isgraphics(f));
    if isgraphics(f), delete(f); end
end
