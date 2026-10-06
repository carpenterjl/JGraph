function u9b_nargout
% U9b: the rows audit-nargout.m would record for the stage's names.
names = {'uihtml', 'sendEventToHTMLSource'};
for k = 1:numel(names)
    try
        n = num2str(nargout(names{k}));
    catch e
        n = ['ERR:' e.identifier];
    end
    fprintf('%s\t%s\t%d\n', names{k}, n, exist(names{k}));
end
end
